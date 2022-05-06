// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Claunia.PropertyList;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    class Program
    {
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        static string TempDir => Utility.GetDefaultTempDirectory(null);

        public static int Main(string[] args)
        {
            var commands = new CommandSet {
                "[ Package Authoring ]",
                { "bundle", "Reads a build directory and creates a OSX '.app' bundle", new BundleOptions(), Bundle },
                { "pack", "Convert a '.app' bundle into a Squirrel release", new PackOptions(), Pack },
            };

            return SquirrelHost.Run(args, commands);
        }

        private static void Pack(PackOptions options)
        {
            var targetDir = options.releaseDir ?? Path.Combine(".", "Releases");
            var di = new DirectoryInfo(targetDir);
            if (!Directory.Exists(targetDir)) {
                Directory.CreateDirectory(targetDir);
            }

            //var builder = new StructureBuilder(options.package);
            var plistPath = Path.Combine(options.package, "Contents", PlistWriter.PlistFileName);
            NSDictionary plist = (NSDictionary) PropertyListParser.Parse(plistPath);

            var _ = Utility.GetTempDir(TempDir, out var tmp);

            var packId = plist.ObjectForKey("CFBundleIdentifier").ToString();
            var packVersion = plist.ObjectForKey("CFBundleVersion").ToString();
            var packTitle = plist.ObjectForKey("CFBundleName").ToString();

            var nupkgPath = NugetConsole.CreatePackageFromMetadata(
                tmp, options.package, packId, packTitle, packTitle,
                packVersion, options.releaseNotes, options.includePdb);

            var releaseFilePath = Path.Combine(di.FullName, "RELEASES");
            var previousReleases = new List<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                previousReleases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));
            }

            var rp = new ReleasePackageBuilder(nupkgPath);
            //var newPkgPath = rp.CreateReleasePackage(TempDir, Path.Combine(options.releaseDir, rp.SuggestedReleaseFileName), contentsPostProcessHook: (pkgPath, zpkg) => {
            //    var nuspecPath = Directory.GetFiles(pkgPath, "*.nuspec", SearchOption.TopDirectoryOnly)
            //     .ContextualSingle("package", "*.nuspec", "top level directory");
            //    var libDir = Directory.GetDirectories(Path.Combine(pkgPath, "lib"))
            //        .ContextualSingle("package", "'lib' folder");
            //    var contentsDir = Path.Combine(libDir, "Contents");
            //    File.Copy(nuspecPath, Path.Combine(contentsDir, "current.version"));
            //});

            // we are not currently making any modifications to the package
            // so we can just copy it to the right place. uncomment the above otherwise.
            var newPkgPath = Path.Combine(targetDir, rp.SuggestedReleaseFileName);
            File.Move(rp.InputPackageFile, newPkgPath);

            var prev = ReleasePackageBuilder.GetPreviousRelease(previousReleases, rp, targetDir);
            if (prev != null && !options.noDelta) {
                var deltaBuilder = new DeltaPackageBuilder();
                var dp = deltaBuilder.CreateDeltaPackage(prev, rp,
                    Path.Combine(di.FullName, rp.SuggestedReleaseFileName.Replace("full", "delta")), TempDir);
            }

            ReleaseEntry.WriteReleaseFile(previousReleases.Concat(new [] { ReleaseEntry.GenerateFromFile(newPkgPath) }), releaseFilePath);

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