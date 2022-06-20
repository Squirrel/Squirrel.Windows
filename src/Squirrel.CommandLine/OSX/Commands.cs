using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.NET.HostModel.AppHost;
using NuGet.Versioning;
using Squirrel.NuGet;
using Squirrel.PropertyList;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine.OSX
{
    class Commands
    {
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Commands));

        public static CommandSet GetCommands()
        {
            return new CommandSet {
                "[ Package Authoring ]",
                { "pack", "Convert a build or '.app' dir into a Squirrel release", new PackOptions(), Pack },
            };
        }

        private static void Pack(PackOptions options)
        {
            var releaseDir = options.GetReleaseDirectory();
            string appBundlePath;

            if (options.packDirectory.EndsWith(".app", StringComparison.InvariantCultureIgnoreCase)) {
                Log.Info("Pack directory is already a '.app' bundle. Converting to Squirrel app.");

                if (options.icon != null)
                    Log.Warn("--icon is ignored if the pack directory is a '.app' bundle.");

                if (options.mainExe != null)
                    Log.Warn("--exeName is ignored if the pack directory is a '.app' bundle.");

                appBundlePath = Path.Combine(releaseDir.FullName, options.packId + ".app");

                if (Utility.PathPartStartsWith(releaseDir.FullName, appBundlePath))
                    throw new Exception("Pack directory is inside release directory. Please move the app bundle outside of the release directory first.");

                Log.Info("Copying app to release directory");
                if (Directory.Exists(appBundlePath)) Utility.DeleteFileOrDirectoryHard(appBundlePath);
                Directory.CreateDirectory(appBundlePath);
                CopyFiles(new DirectoryInfo(options.packDirectory), new DirectoryInfo(appBundlePath));
            } else {
                Log.Info("Pack directory is not a bundle. Will generate new '.app' bundle from a directory of application files.");

                if (options.icon == null || !File.Exists(options.icon))
                    throw new OptionValidationException("--icon is required when generating a new app bundle.");

                // auto-discover exe if it's the same as packId
                var exeName = options.mainExe;
                if (exeName == null && File.Exists(Path.Combine(options.packDirectory, options.packId)))
                    exeName = options.packId;

                if (exeName == null)
                    throw new OptionValidationException("--exeName is required when generating a new app bundle.");

                var mainExePath = Path.Combine(options.packDirectory, exeName);
                if (!File.Exists(mainExePath) || !PlatformUtil.IsMachOImage(mainExePath))
                    throw new OptionValidationException($"--exeName '{mainExePath}' does not exist or is not a mach-o executable.");

                var appleId = $"com.{options.packAuthors ?? options.packId}.{options.packId}";
                var escapedAppleId = Regex.Replace(appleId, @"[^\w\.]", "_");

                var appleSafeVersion = NuGetVersion.Parse(options.packVersion).Version.ToString();

                var info = new AppInfo {
                    CFBundleName = options.packTitle ?? options.packId,
                    CFBundleDisplayName = options.packTitle ?? options.packId,
                    CFBundleExecutable = options.mainExe,
                    CFBundleIdentifier = escapedAppleId,
                    CFBundlePackageType = "APPL",
                    CFBundleShortVersionString = appleSafeVersion,
                    CFBundleVersion = options.packVersion,
                    CFBundleSignature = "????",
                    NSPrincipalClass = "NSApplication",
                    NSHighResolutionCapable = true,
                    CFBundleIconFile = Path.GetFileName(options.icon),
                };

                Log.Info("Creating '.app' directory structure");
                var builder = new StructureBuilder(options.packId, releaseDir.FullName);
                if (Directory.Exists(builder.AppDirectory)) Utility.DeleteFileOrDirectoryHard(builder.AppDirectory);
                builder.Build();

                Log.Info("Writing Info.plist");
                var plist = new PlistWriter(info, builder.ContentsDirectory);
                plist.Write();

                Log.Info("Copying resources into new '.app' bundle");
                File.Copy(options.icon, Path.Combine(builder.ResourcesDirectory, Path.GetFileName(options.icon)));

                Log.Info("Copying application files into new '.app' bundle");
                CopyFiles(new DirectoryInfo(options.packDirectory), new DirectoryInfo(builder.MacosDirectory));

                appBundlePath = builder.AppDirectory;
            }

            Log.Info("Adding Squirrel resources to bundle.");
            var contentsDir = Path.Combine(appBundlePath, "Contents");

            if (!Directory.Exists(contentsDir))
                throw new Exception("Invalid bundle structure (missing Contents dir)");

            if (!File.Exists(Path.Combine(contentsDir, "Info.plist")))
                throw new Exception("Invalid bundle structure (missing Info.plist)");

            var nuspecText = NugetConsole.CreateNuspec(
                options.packId, options.packTitle, options.packAuthors, options.packVersion, options.releaseNotes, options.includePdb, "osx");

            var nuspecPath = Path.Combine(contentsDir, Utility.SpecVersionFileName);

            // nuspec and UpdateMac need to be in contents dir or this package can't update
            File.WriteAllText(nuspecPath, nuspecText);
            File.Copy(HelperExe.UpdateMacPath, Path.Combine(contentsDir, "UpdateMac"));

            var zipPath = Path.Combine(releaseDir.FullName, options.packId + ".zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);

            // code signing all mach-o binaries
            if (SquirrelRuntimeInfo.IsOSX && !String.IsNullOrEmpty(options.signAppIdentity) && !String.IsNullOrEmpty(options.notaryProfile)) {
                HelperExe.CodeSign(options.signAppIdentity, options.signEntitlements, new []{ appBundlePath });
                HelperExe.AssessCodeSign(appBundlePath);

                // notarize and staple the .app before creating a Squirrel release
                HelperExe.CreateDittoZip(appBundlePath, zipPath);
                HelperExe.Notarize(zipPath, options.notaryProfile);
                HelperExe.Staple(appBundlePath);

                // re-create the zip from the app with the stapled notarization
                File.Delete(zipPath);
                HelperExe.CreateDittoZip(appBundlePath, zipPath);
            } else {
                Log.Warn("Package will not be signed or notarized. Only supported on OSX with the --signAppIdentity and --notaryProfile options.");
                EasyZip.CreateZipFromDirectory(zipPath, appBundlePath, nestDirectory: true);
            }

            // create release / delta from notarized .app
            Log.Info("Creating Squirrel Release");
            using var _ = Utility.GetTempDirectory(out var tmp);
            var nupkgPath = NugetConsole.CreatePackageFromNuspecPath(tmp, appBundlePath, nuspecPath);

            var releaseFilePath = Path.Combine(releaseDir.FullName, "RELEASES");
            var releases = new List<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                releases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));
            }

            var rp = new ReleasePackageBuilder(nupkgPath);
            var newPkgPath = rp.CreateReleasePackage(Path.Combine(releaseDir.FullName, rp.SuggestedReleaseFileName));

            Log.Info("Creating Delta Packages");
            var prev = ReleasePackageBuilder.GetPreviousRelease(releases, rp, releaseDir.FullName);
            if (prev != null && !options.noDelta) {
                var deltaBuilder = new DeltaPackageBuilder();
                var deltaFile = Path.Combine(releaseDir.FullName, rp.SuggestedReleaseFileName.Replace("-full", "-delta"));
                var dp = deltaBuilder.CreateDeltaPackage(prev, rp, deltaFile);
                releases.Add(ReleaseEntry.GenerateFromFile(deltaFile));
            }

            releases.Add(ReleaseEntry.GenerateFromFile(newPkgPath));
            ReleaseEntry.WriteReleaseFile(releases, releaseFilePath);

            // create installer package and notarize
            if (SquirrelRuntimeInfo.IsOSX && !String.IsNullOrEmpty(options.signInstallIdentity) && !String.IsNullOrEmpty(options.notaryProfile)) {
                var pkgPath = Path.Combine(releaseDir.FullName, options.packId + ".pkg");
                if (File.Exists(pkgPath)) File.Delete(pkgPath);
                HelperExe.CreateInstallerPkg(appBundlePath, pkgPath, options.signInstallIdentity);
                HelperExe.Notarize(pkgPath, options.notaryProfile);
                HelperExe.Staple(pkgPath);
            } else {
                Log.Warn("Package installer (.pkg) will not be created. Only supported on OSX with the --signInstallIdentity and --notaryProfile options.");
            }

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