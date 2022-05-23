using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using Squirrel.NuGet;
using Squirrel.PropertyList;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine.OSX
{
    class CommandsOSX
    {
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(CommandsOSX));

        public static CommandSet GetCommands()
        {
            return new CommandSet {
                "[ Package Authoring ]",
                { "bundle", "Convert a build directory into a OSX '.app' bundle", new BundleOptions(), Bundle },
                { "pack", "Create a Squirrel release from a '.app' bundle", new PackOptions(), Pack },
            };
        }
        
        private static void Pack(PackOptions options)
        {
            var targetDir = options.GetReleaseDirectory();

            //var builder = new StructureBuilder(options.package);
            var plistPath = Path.Combine(options.package, "Contents", PlistWriter.PlistFileName);
            NSDictionary plist = (NSDictionary) PropertyListParser.Parse(plistPath);

            using var _ = Utility.GetTempDirectory(out var tmp);

            string getpStr(string name)
            {
                var res = plist.ObjectForKey(name);
                if (res == null)
                    throw new Exception($"Did not find required key '{name}' in Info.plist");
                return res.ToString();
            }

            var packId = getpStr("CFBundleIdentifier");
            var packVersion = getpStr("CFBundleVersion");
            var packTitle = getpStr("CFBundleName");

            var nupkgPath = NugetConsole.CreatePackageFromMetadata(
                tmp, options.package, packId, packTitle, packTitle,
                packVersion, options.releaseNotes, options.includePdb);

            var releaseFilePath = Path.Combine(targetDir.FullName, "RELEASES");
            var previousReleases = new List<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                previousReleases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));
            }

            Log.Info("Creating Squirrel Release");
            var rp = new ReleasePackageBuilder(nupkgPath);
            var newPkgPath = rp.CreateReleasePackage(Path.Combine(targetDir.FullName, rp.SuggestedReleaseFileName), contentsPostProcessHook: (pkgPath, zpkg) => {
                var nuspecPath = Directory.GetFiles(pkgPath, "*.nuspec", SearchOption.TopDirectoryOnly)
                 .ContextualSingle("package", "*.nuspec", "top level directory");
                var libDir = Directory.GetDirectories(Path.Combine(pkgPath, "lib"))
                    .ContextualSingle("package", "'lib' folder");
                var contentsDir = Path.Combine(libDir, "Contents");
                File.Copy(nuspecPath, Path.Combine(contentsDir, Utility.SpecVersionFileName));
                File.Copy(HelperExe.UpdateMacPath, Path.Combine(contentsDir, "UpdateMac"));
            });


            // we are not currently making any modifications to the package
            // so we can just copy it to the right place. uncomment the above otherwise.
            //var newPkgPath = Path.Combine(targetDir, rp.SuggestedReleaseFileName);
            //File.Move(rp.InputPackageFile, newPkgPath);

            Log.Info("Creating Delta Packages");
            var prev = ReleasePackageBuilder.GetPreviousRelease(previousReleases, rp, targetDir.FullName);
            if (prev != null && !options.noDelta) {
                var deltaBuilder = new DeltaPackageBuilder();
                var dp = deltaBuilder.CreateDeltaPackage(prev, rp,
                    Path.Combine(targetDir.FullName, rp.SuggestedReleaseFileName.Replace("full", "delta")));
            }

            ReleaseEntry.WriteReleaseFile(previousReleases.Concat(new[] { ReleaseEntry.GenerateFromFile(newPkgPath) }), releaseFilePath);
            
            Log.Info("Generating latest app archive");
            var finalAppDir = Path.Combine(targetDir.FullName, $"{rp.Id}.app");
            Utility.DeleteFileOrDirectoryHard(finalAppDir);
            ZipPackage.ExtractZipReleaseForInstallOSX(rp.SuggestedReleaseFileName, finalAppDir, null);
            EasyZip.CreateZipFromDirectory(Path.Combine(targetDir.FullName, $"{rp.Id}.app.zip"), finalAppDir);

            Log.Info("Done");
        }

        private static void Bundle(BundleOptions options)
        {
            var info = new AppInfo {
                CFBundleName = options.packTitle ?? options.packId,
                CFBundleDisplayName = options.packTitle ?? options.packId,
                CFBundleExecutable = options.exeName,
                CFBundleIdentifier = options.packId,
                CFBundlePackageType = "APPL",
                CFBundleShortVersionString = options.packVersion,
                CFBundleVersion = options.packVersion,
                CFBundleSignature = "????",
                NSPrincipalClass = "NSApplication",
                NSHighResolutionCapable = true,
                CFBundleIconFile = Path.GetFileName(options.icon),
            };

            Log.Info("Creating .app directory structure");
            var builder = new StructureBuilder(options.packId, options.outputDirectory);
            builder.Build();

            Log.Info("Writing Info.plist");
            var plist = new PlistWriter(info, builder.ContentsDirectory);
            plist.Write();

            Log.Info("Copying resources");
            File.Copy(options.icon, Path.Combine(builder.ResourcesDirectory, Path.GetFileName(options.icon)));

            Log.Info("Copying application files");
            CopyFiles(new DirectoryInfo(options.packDirectory), new DirectoryInfo(builder.MacosDirectory));

            Log.Info("MacOS application bundle (.app) created at: " + builder.AppDirectory);
            Log.Info("Done.");
        }

        private static void CopyFiles(DirectoryInfo source, DirectoryInfo target)
        {
            Directory.CreateDirectory(target.FullName);

            foreach (var fileInfo in source.GetFiles()) {
                var path = Path.Combine(target.FullName, fileInfo.Name);
                fileInfo.CopyTo(path, true);
            }

            foreach (var sourceSubDir in source.GetDirectories()) {
                var targetSubDir = target.CreateSubdirectory(sourceSubDir.Name);
                CopyFiles(sourceSubDir, targetSubDir);
            }
        }
    }
}