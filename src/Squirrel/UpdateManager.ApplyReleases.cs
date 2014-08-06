using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
    public sealed partial class UpdateManager
    {
        class ApplyReleases : IEnableLogger
        {
            public async Task ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null)
            {
                progress = progress ?? (_ => { });

                await cleanDeadVersions(updateInfo.CurrentlyInstalledVersion != null ? updateInfo.CurrentlyInstalledVersion.Version : null);
                progress(10);

                var release = await createFullPackagesFromDeltas(updateInfo.ReleasesToApply, updateInfo.CurrentlyInstalledVersion);
                progress(50);

                await installPackageToAppDir(updateInfo, release);
                progress(95);

                await updateLocalReleasesFile();
                progress(100);
            }

            async Task installPackageToAppDir(UpdateInfo updateInfo, ReleaseEntry release)
            {
                var pkg = new ZipPackage(Path.Combine(updateInfo.PackageDirectory, release.Filename));
                var target = getDirectoryForRelease(release.Version);

                // NB: This might happen if we got killed partially through applying the release
                if (target.Exists) {
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
                await Task.Run(() => toWrite.ForEach(x => CopyFileToLocation(target, x)));

                await pkg.GetContentFiles().ForEachAsync(x => CopyFileToLocation(target, x));

                var newCurrentVersion = updateInfo.FutureReleaseEntry.Version;

                // Perform post-install; clean up the previous version by asking it
                // which shortcuts to install, and nuking them. Then, run the app's
                // post install and set up shortcuts.
                runPostInstallAndCleanup(newCurrentVersion, updateInfo.IsBootstrapping);
            }

            void CopyFileToLocation(FileSystemInfo target, IPackageFile x)
            {
                var targetPath = Path.Combine(target.FullName, x.EffectivePath);

                var fi = new FileInfo(targetPath);
                if (fi.Exists) fi.Delete();

                var dir = new DirectoryInfo(Path.GetDirectoryName(targetPath));
                if (!dir.Exists) dir.Create();

                using (var inf = x.GetStream())
                using (var of = fi.Open(FileMode.CreateNew, FileAccess.Write)) {
                    inf.CopyTo(of);
                }
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

                // If there are no deltas in our list, we're already done
                if (!releasesToApply.Any() || releasesToApply.All(x => !x.IsDelta)) {
                    return releasesToApply.MaxBy(x => x.Version).First();
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
                    try {
                        await Utility.DeleteDirectory(x.FullName);
                    } catch (UnauthorizedAccessException ex) {
                        this.Log().WarnException("Couldn't delete directory: " + x.FullName, ex);
                    }
                });
            }

            async Task updateLocalReleasesFile()
            {
                await Task.Run(() => ReleaseEntry.BuildReleasesFile(PackageDirectory));
            }
        }
    }
}
