using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NuGet;
using Splat;

namespace Squirrel
{
    public sealed partial class UpdateManager
    {
        internal class ApplyReleasesImpl : IEnableLogger
        {
            // TODO: Kill this entire concept
            readonly FrameworkVersion appFrameworkVersion = FrameworkVersion.Net45;

            readonly string rootAppDirectory;
            readonly string applicationName;

            public ApplyReleasesImpl(string applicationName, string rootAppDirectory)
            {
                this.applicationName = applicationName;
                this.rootAppDirectory = rootAppDirectory;
            }

            public async Task<string> ApplyReleases(UpdateInfo updateInfo, bool silentInstall, bool attemptingFullInstall, Action<int> progress = null)
            {
                progress = progress ?? (_ => { });

                var release = await createFullPackagesFromDeltas(updateInfo.ReleasesToApply, updateInfo.CurrentlyInstalledVersion);
                progress(10);

                if (release == null) {
                    if (attemptingFullInstall) {
                        this.Log().Info("No release to install, running the app");
                        await invokePostInstall(updateInfo.CurrentlyInstalledVersion.Version, false, true);
                    }

                    return getDirectoryForRelease(updateInfo.CurrentlyInstalledVersion.Version).FullName;
                }

                var ret = await this.ErrorIfThrows(() => installPackageToAppDir(updateInfo, release), 
                    "Failed to install package to app dir");
                progress(30);

                var currentReleases = await this.ErrorIfThrows(() => updateLocalReleasesFile(),
                    "Failed to update local releases file");
                progress(50);

                var newVersion = currentReleases.MaxBy(x => x.Version).First().Version;
                await this.ErrorIfThrows(() => invokePostInstall(newVersion, currentReleases.Count == 1 && !silentInstall, false),
                    "Failed to invoke post-install");
                progress(75);

                try {
                    await cleanDeadVersions(newVersion);
                } catch (Exception ex) {
                    this.Log().WarnException("Failed to clean dead versions, continuing anyways", ex);
                }
                progress(100);

                return ret;
            }

            public async Task FullUninstall()
            {
                var currentRelease = getReleases().MaxBy(x => x.Name.ToVersion()).FirstOrDefault();

                this.Log().Info("Starting full uninstall");
                if (currentRelease.Exists) {
                    var version = currentRelease.Name.ToVersion();

                    try {
                        var squirrelAwareApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(currentRelease.FullName);

                        if (squirrelAwareApps.Count > 0) {
                            await squirrelAwareApps.ForEachAsync(exe => Utility.InvokeProcessAsync(exe, String.Format("--squirrel-uninstall {0}", version)), 1);
                        } else {
                            var allApps = currentRelease.EnumerateFiles()
                                .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            allApps.ForEach(x => RemoveShortcutsForExecutable(x.Name, ShortcutLocation.StartMenu | ShortcutLocation.Desktop));
                        }
                    } catch (Exception ex) {
                        this.Log().WarnException("Failed to run pre-uninstall hooks, uninstalling anyways", ex);
                    }
                }

                await this.ErrorIfThrows(() => Utility.DeleteDirectoryWithFallbackToNextReboot(rootAppDirectory),
                    "Failed to delete app directory: " + rootAppDirectory);
            }

            public void CreateShortcutsForExecutable(string exeName, ShortcutLocation locations, bool updateOnly)
            {
                var releases = Utility.LoadLocalReleases(Utility.LocalReleaseFileForAppDir(rootAppDirectory));
                var thisRelease = Utility.FindCurrentVersion(releases);

                var zf = new ZipPackage(thisRelease.Filename);
                var exePath = Path.Combine(Utility.AppDirForRelease(rootAppDirectory, thisRelease), exeName);
                var fileVerInfo = FileVersionInfo.GetVersionInfo(exePath);

                foreach (var f in new[] { ShortcutLocation.StartMenu, ShortcutLocation.Desktop, }) {
                    if (!locations.HasFlag(f)) continue;

                    var file = linkTargetForVersionInfo(f, zf, fileVerInfo);
                    var fileExists = File.Exists(file);

                    this.Log().Info("Creating shortcut for {0} => {1}", exeName, file);

                    // NB: If we've already installed the app, but the shortcut
                    // is no longer there, we have to assume that the user didn't
                    // want it there and explicitly deleted it, so we shouldn't
                    // annoy them by recreating it.
                    if (!fileExists && updateOnly) continue;

                    if (fileExists) File.Delete(file);

                    var sl = new ShellLink {
                        Target = exePath,
                        IconPath = exePath,
                        IconIndex = 0,
                        WorkingDirectory = Path.GetDirectoryName(exePath),
                        Description = zf.Description,
                    };

                    // NB: Just don't if we're in a test runner for now
                    if (!ModeDetector.InUnitTestRunner()) {
                        sl.Save(file);
                    }
                }
            }

            public void RemoveShortcutsForExecutable(string exeName, ShortcutLocation locations)
            {
                var releases = Utility.LoadLocalReleases(Utility.LocalReleaseFileForAppDir(rootAppDirectory));
                var thisRelease = Utility.FindCurrentVersion(releases);

                var zf = new ZipPackage(thisRelease.Filename);
                var fileVerInfo = FileVersionInfo.GetVersionInfo(
                    Path.Combine(Utility.AppDirForRelease(rootAppDirectory, thisRelease), exeName));

                foreach (var f in new[] { ShortcutLocation.StartMenu, ShortcutLocation.Desktop, }) {
                    if (!locations.HasFlag(f)) continue;

                    var file = linkTargetForVersionInfo(f, zf, fileVerInfo);

                    this.Log().Info("Removing shortcut for {0} => {1}", exeName, file);
                    if (File.Exists(file)) File.Delete(file);
                }
            }

            async Task<string> installPackageToAppDir(UpdateInfo updateInfo, ReleaseEntry release)
            {
                var pkg = new ZipPackage(Path.Combine(updateInfo.PackageDirectory, release.Filename));
                var target = getDirectoryForRelease(release.Version);

                // NB: This might happen if we got killed partially through applying the release
                if (target.Exists) {
                    this.Log().Warn("Found partially applied release folder, killing it: " + target.FullName);
                    await Utility.DeleteDirectory(target.FullName);
                }

                target.Create();

                // Copy all of the files out of the lib/ dirs in the NuGet package
                // into our target App directory.
                //
                // NB: We sort this list in order to guarantee that if a Net20
                // and a Net40 version of a DLL get shipped, we always end up
                // with the 4.0 version.
                this.Log().Info("Writing files to app directory: {0}", target.FullName);

                var toWrite = pkg.GetLibFiles().Where(x => pathIsInFrameworkProfile(x, appFrameworkVersion))
                    .OrderBy(x => x.Path)
                    .ToList();

                // NB: Because of the above NB, we cannot use ForEachAsync here, we 
                // have to copy these files in-order. Once we fix assembly resolution, 
                // we can kill both of these NBs.
                await Task.Run(() => toWrite.ForEach(x => copyFileToLocation(target, x)));

                await pkg.GetContentFiles().ForEachAsync(x => copyFileToLocation(target, x));

                var newCurrentVersion = updateInfo.FutureReleaseEntry.Version;

                // Perform post-install; clean up the previous version by asking it
                // which shortcuts to install, and nuking them. Then, run the app's
                // post install and set up shortcuts.
                this.ErrorIfThrows(() => runPostInstallAndCleanup(newCurrentVersion, updateInfo.IsBootstrapping));

                return target.FullName;
            }

            void copyFileToLocation(FileSystemInfo target, IPackageFile x)
            {
                var targetPath = Path.Combine(target.FullName, x.EffectivePath);

                var fi = new FileInfo(targetPath);
                if (fi.Exists) fi.Delete();

                var dir = new DirectoryInfo(Path.GetDirectoryName(targetPath));
                if (!dir.Exists) dir.Create();

                this.ErrorIfThrows(() => {
                    using (var inf = x.GetStream())
                    using (var of = fi.Open(FileMode.CreateNew, FileAccess.Write)) {
                        inf.CopyTo(of);
                    }
                }, "Failed to write file: " + target.FullName);
            }

            void runPostInstallAndCleanup(Version newCurrentVersion, bool isBootstrapping)
            {
                fixPinnedExecutables(newCurrentVersion);

                this.Log().Info("runPostInstallAndCleanup: finished fixPinnedExecutables");
                cleanUpOldVersions(newCurrentVersion);
            }

            static bool pathIsInFrameworkProfile(IPackageFile packageFile, FrameworkVersion appFrameworkVersion)
            {
                if (!packageFile.Path.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase)) {
                    return false;
                }

                if (appFrameworkVersion == FrameworkVersion.Net40
                    && packageFile.Path.StartsWith("lib\\net45", StringComparison.InvariantCultureIgnoreCase)) {
                    return false;
                }

                return true;
            }

            async Task<ReleaseEntry> createFullPackagesFromDeltas(IEnumerable<ReleaseEntry> releasesToApply, ReleaseEntry currentVersion)
            {
                Contract.Requires(releasesToApply != null);

                // If there are no remote releases at all, bail
                if (!releasesToApply.Any()) {
                    return null;
                }

                // If there are no deltas in our list, we're already done
                if (releasesToApply.All(x => !x.IsDelta)) {
                    return releasesToApply.MaxBy(x => x.Version).FirstOrDefault();
                }

                if (!releasesToApply.All(x => x.IsDelta)) {
                    throw new Exception("Cannot apply combinations of delta and full packages");
                }

                // Smash together our base full package and the nearest delta
                var ret = await Task.Run(() => {
                    var basePkg = new ReleasePackage(Path.Combine(rootAppDirectory, "packages", currentVersion.Filename));
                    var deltaPkg = new ReleasePackage(Path.Combine(rootAppDirectory, "packages", releasesToApply.First().Filename));

                    var deltaBuilder = new DeltaPackageBuilder();

                    return deltaBuilder.ApplyDeltaPackage(basePkg, deltaPkg,
                        Regex.Replace(deltaPkg.InputPackageFile, @"-delta.nupkg$", ".nupkg", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                });

                if (releasesToApply.Count() == 1) {
                    return ReleaseEntry.GenerateFromFile(ret.InputPackageFile);
                }

                var fi = new FileInfo(ret.InputPackageFile);
                var entry = ReleaseEntry.GenerateFromFile(fi.OpenRead(), fi.Name);

                // Recursively combine the rest of them
                return await createFullPackagesFromDeltas(releasesToApply.Skip(1), entry);
            }

            void cleanUpOldVersions(Version newCurrentVersion)
            {
                var directory = new DirectoryInfo(rootAppDirectory);
                if (!directory.Exists) {
                    this.Log().Warn("cleanUpOldVersions: the directory '{0}' does not exist", rootAppDirectory);
                    return;
                }
                
                foreach (var v in getOldReleases(newCurrentVersion)) {
                    Utility.DeleteDirectoryAtNextReboot(v.FullName);
                }
            }

            async Task invokePostInstall(Version currentVersion, bool isInitialInstall, bool firstRunOnly)
            {
                var targetDir = getDirectoryForRelease(currentVersion);
                var args = isInitialInstall ?
                    String.Format("--squirrel-install {0}", currentVersion) :
                    String.Format("--squirrel-updated {0}", currentVersion);

                var squirrelApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(targetDir.FullName);

                this.Log().Info("Squirrel Enabled Apps: [{0}]", String.Join(",", squirrelApps));

                // For each app, run the install command in-order and wait
                if (!firstRunOnly) await squirrelApps.ForEachAsync(exe => Utility.InvokeProcessAsync(exe, args), 1 /* at a time */);

                if (!isInitialInstall) return;

                // If this is the first run, we run the apps with first-run and 
                // *don't* wait for them, since they're probably the main EXE
                if (squirrelApps.Count == 0) {
                    this.Log().Warn("No apps are marked as Squirrel-aware! Going to run them all");

                    squirrelApps = targetDir.EnumerateFiles()
                        .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.FullName)
                        .ToList();

                    // Create shortcuts for apps automatically if they didn't
                    // create any Squirrel-aware apps
                    squirrelApps.ForEach(x => CreateShortcutsForExecutable(Path.GetFileName(x), ShortcutLocation.Desktop | ShortcutLocation.StartMenu, isInitialInstall));
                }

                var firstRunParam = isInitialInstall ? "--squirrel-firstrun" : "";
                squirrelApps.ForEach(exe => Process.Start(exe, firstRunParam));
            }

            void fixPinnedExecutables(Version newCurrentVersion) 
            {
                if (Environment.OSVersion.Version < new Version(6, 1)) {
                    this.Log().Warn("fixPinnedExecutables: Found OS Version '{0}', exiting...", Environment.OSVersion.VersionString);
                    return;
                }

                var newCurrentFolder = "app-" + newCurrentVersion;
                var oldAppDirectories = (new DirectoryInfo(rootAppDirectory)).GetDirectories()
                    .Where(x => x.Name.StartsWith("app-", StringComparison.InvariantCultureIgnoreCase))
                    .Where(x => x.Name != newCurrentFolder)
                    .Select(x => x.FullName)
                    .ToArray();

                if (!oldAppDirectories.Any()) {
                    this.Log().Info("fixPinnedExecutables: oldAppDirectories is empty, this is pointless");
                    return;
                }

                var newAppPath = Path.Combine(rootAppDirectory, newCurrentFolder);

                var taskbarPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");

                Func<FileInfo, ShellLink> resolveLink = file => {
                    try {
                        return new ShellLink(file.FullName);
                    } catch (Exception ex) {
                        var message = String.Format("File '{0}' could not be converted into a valid ShellLink", file.FullName);
                        this.Log().WarnException(message, ex);
                        return null;
                    }
                };

                var shellLinks = (new DirectoryInfo(taskbarPath)).GetFiles("*.lnk")
                    .Select(resolveLink)
                    .Where(x => x != null)
                    .ToArray();

                foreach (var shortcut in shellLinks) {
                    try {
                        updateLink(shortcut, oldAppDirectories, newAppPath);
                    } catch (Exception ex) {
                        var message = String.Format("fixPinnedExecutables: shortcut failed: {0}", shortcut.Target);
                        this.Log().ErrorException(message, ex);
                    }
                }
            }

            void updateLink(ShellLink shortcut, string[] oldAppDirectories, string newAppPath)
            {
                this.Log().Info("Processing shortcut '{0}'", shortcut.Target);

                foreach (var oldAppDirectory in oldAppDirectories) {
                    if (!shortcut.Target.StartsWith(oldAppDirectory, StringComparison.OrdinalIgnoreCase)) {
                        this.Log().Info("Does not match '{0}', continuing to next directory", oldAppDirectory);
                        continue;
                    }

                    // replace old app path with new app path and check, if executable still exists
                    var newTarget = Path.Combine(newAppPath, shortcut.Target.Substring(oldAppDirectory.Length + 1));

                    if (File.Exists(newTarget)) {
                        shortcut.Target = newTarget;

                        // replace working directory too if appropriate
                        if (shortcut.WorkingDirectory.StartsWith(oldAppDirectory, StringComparison.OrdinalIgnoreCase)) {
                            this.Log().Info("Changing new directory to '{0}'", newAppPath);
                            shortcut.WorkingDirectory = Path.Combine(newAppPath,
                                shortcut.WorkingDirectory.Substring(oldAppDirectory.Length + 1));
                        }

                        shortcut.Save();
                    }
                    else {
                        this.Log().Info("Unpinning {0} from taskbar", shortcut.Target);
                        TaskbarHelper.UnpinFromTaskbar(shortcut.Target);
                    }

                    break;
                }
            }

            // NB: Once we uninstall the old version of the app, we try to schedule
            // it to be deleted at next reboot. Unfortunately, depending on whether
            // the user has admin permissions, this can fail. So as a failsafe,
            // before we try to apply any update, we assume previous versions in the
            // directory are "dead" (i.e. already uninstalled, but not deleted), and
            // we blow them away. This is to make sure that we don't attempt to run
            // an uninstaller on an already-uninstalled version.
            async Task cleanDeadVersions(Version currentVersion)
            {
                if (currentVersion == null) return;

                var di = new DirectoryInfo(rootAppDirectory);
                if (!di.Exists) return;

                this.Log().Info("cleanDeadVersions: for version {0}", currentVersion);

                string currentVersionFolder = null;
                if (currentVersion != null) {
                    currentVersionFolder = getDirectoryForRelease(currentVersion).Name;
                    this.Log().Info("cleanDeadVersions: exclude folder {0}", currentVersionFolder);
                }

                // NB: If we try to access a directory that has already been 
                // scheduled for deletion by MoveFileEx it throws what seems like
                // NT's only error code, ERROR_ACCESS_DENIED. Squelch errors that
                // come from here.
                var toCleanup = di.GetDirectories()
                    .Where(x => x.Name.ToLowerInvariant().Contains("app-"))
                    .Where(x => x.Name != currentVersionFolder);

                await toCleanup.ForEachAsync(async x => {
                    var squirrelApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(x.FullName);
                    var args = String.Format("--squirrel-obsolete {0}", x.Name.Replace("app-", ""));

                    if (squirrelApps.Count > 0) {
                        // For each app, run the install command in-order and wait
                        await squirrelApps.ForEachAsync(exe => Utility.InvokeProcessAsync(exe, args), 1 /* at a time */);
                    }
                });

                await toCleanup.ForEachAsync(async x => {
                    try {
                        await Utility.DeleteDirectoryWithFallbackToNextReboot(x.FullName);
                    } catch (UnauthorizedAccessException ex) {
                        this.Log().WarnException("Couldn't delete directory: " + x.FullName, ex);
                    }
                });
            }

            internal async Task<List<ReleaseEntry>> updateLocalReleasesFile()
            {
                return await Task.Run(() => ReleaseEntry.BuildReleasesFile(Utility.PackageDirectoryForAppDir(rootAppDirectory)));
            }

            IEnumerable<DirectoryInfo> getReleases()
            {
                var rootDirectory = new DirectoryInfo(rootAppDirectory);

                if (!rootDirectory.Exists) return Enumerable.Empty<DirectoryInfo>();

                return rootDirectory.GetDirectories()
                    .Where(x => x.Name.StartsWith("app-", StringComparison.InvariantCultureIgnoreCase));
            }

            IEnumerable<DirectoryInfo> getOldReleases(Version version)
            {
                return getReleases()
                    .Where(x => x.Name.ToVersion() < version)
                    .ToArray();
            }

            DirectoryInfo getDirectoryForRelease(Version releaseVersion)
            {
                return new DirectoryInfo(Path.Combine(rootAppDirectory, "app-" + releaseVersion));
            }

            string linkTargetForVersionInfo(ShortcutLocation location, IPackage package, FileVersionInfo versionInfo)
            {
                var possibleProductNames = new[] {
                    versionInfo.ProductName,
                    package.Title,
                    versionInfo.FileDescription,
                    versionInfo.FileName,
                };

                var possibleCompanyNames = new[] {
                    package.Title,
                    versionInfo.CompanyName,
                    package.Id,
                };

                var prodName = possibleCompanyNames.First(x => !String.IsNullOrWhiteSpace(x));
                var pkgName = possibleProductNames.First(x => !String.IsNullOrWhiteSpace(x));

                return getLinkTarget(location, pkgName, prodName);
            }

            string getLinkTarget(ShortcutLocation location, string title, string applicationName, bool createDirectoryIfNecessary = true)
            {
                var dir = default(string);

                switch (location) {
                case ShortcutLocation.Desktop:
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    break;
                case ShortcutLocation.StartMenu:
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), applicationName);
                    break;
                }

                if (createDirectoryIfNecessary && Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }

                return Path.Combine(dir, title + ".lnk");
            }

        }
    }
}