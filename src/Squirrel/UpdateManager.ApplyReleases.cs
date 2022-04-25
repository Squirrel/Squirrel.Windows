using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;
using System.Threading;
using Squirrel.Shell;
using Microsoft.Win32;
using NuGet.Versioning;

namespace Squirrel
{
    public partial class UpdateManager
    {
        /// <inheritdoc/>
        protected async Task<string> ApplyReleases(UpdateInfo updateInfo, bool silentInstall, bool attemptingFullInstall, Action<int> progress = null)
        {
            await acquireUpdateLock().ConfigureAwait(false);
            progress = progress ?? (_ => { });

            progress(0);

            // Progress range: 00 -> 40
            var release = await createFullPackagesFromDeltas(updateInfo.ReleasesToApply, updateInfo.CurrentlyInstalledVersion, new ApplyReleasesProgress(updateInfo.ReleasesToApply.Count, x => progress(CalculateProgress(x, 0, 40)))).ConfigureAwait(false);

            progress(40);

            //if (release == null) {
            //    if (attemptingFullInstall) {
            //        this.Log().Info("No release to install, running the app");
            //        await invokePostInstall(updateInfo.CurrentlyInstalledVersion.Version, false, true, silentInstall).ConfigureAwait(false);
            //    }

            //    progress(100);
            //    return getDirectoryForRelease(updateInfo.CurrentlyInstalledVersion.Version).FullName;
            //}

            // Progress range: 40 -> 80
            var ret = await this.ErrorIfThrows(() => installPackageToAppDir(updateInfo, release, x => progress(CalculateProgress(x, 40, 80))),
                "Failed to install package to app dir").ConfigureAwait(false);

            progress(80);

            var currentReleases = await this.ErrorIfThrows(() => updateLocalReleasesFile(),
                "Failed to update local releases file").ConfigureAwait(false);

            progress(85);

            var newVersion = currentReleases.MaxBy(x => x.Version).First().Version;
            executeSelfUpdate(newVersion);

            progress(90);

            await this.ErrorIfThrows(() => invokePostInstall(newVersion, attemptingFullInstall, false, silentInstall),
                "Failed to invoke post-install").ConfigureAwait(false);

            progress(95);

            try {
                var currentVersion = updateInfo.CurrentlyInstalledVersion != null ?
                    updateInfo.CurrentlyInstalledVersion.Version : null;

                await cleanDeadVersions(currentVersion, newVersion).ConfigureAwait(false);
            } catch (Exception ex) {
                this.Log().WarnException("Failed to clean dead versions, continuing anyways", ex);
            }

            progress(100);

            return ret;
        }

        /// <inheritdoc/>
        public async Task FullUninstall()
        {
            var rootAppDirectory = AppDirectory;
            await acquireUpdateLock().ConfigureAwait(false);

            KillAllExecutablesBelongingToPackage();

            var releases = Utility.GetAppVersionDirectories(rootAppDirectory).ToArray();
            if (!releases.Any())
                return;

            var latest = releases.OrderByDescending(x => x.Version).FirstOrDefault();
            var currentRelease = new DirectoryInfo(latest.DirectoryPath);
            var currentVersion = latest.Version;

            this.Log().Info("Starting full uninstall");
            if (currentRelease.Exists) {
                try {
                    var squirrelAwareApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(currentRelease.FullName);

                    if (isAppFolderDead(currentRelease.FullName)) throw new Exception("App folder is dead, but we're trying to uninstall it?");

                    var allApps = currentRelease.EnumerateFiles()
                        .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        .Where(x => !x.Name.StartsWith("squirrel.", StringComparison.OrdinalIgnoreCase) && !x.Name.StartsWith("update.", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (squirrelAwareApps.Count > 0) {
                        await squirrelAwareApps.ForEachAsync(async exe => {
                            using (var cts = new CancellationTokenSource()) {
                                cts.CancelAfter(10 * 1000);

                                try {
                                    await Utility.InvokeProcessAsync(exe, new string[] { "--squirrel-uninstall", currentVersion.ToString() }, cts.Token).ConfigureAwait(false);
                                } catch (Exception ex) {
                                    this.Log().ErrorException("Failed to run cleanup hook, continuing: " + exe, ex);
                                }
                            }
                        }, 1 /*at a time*/).ConfigureAwait(false);
                    } else {
                        //allApps.ForEach(x => RemoveShortcutsForExecutable(x.Name, ShortcutLocation.StartMenu | ShortcutLocation.Desktop));
                    }
                } catch (Exception ex) {
                    this.Log().WarnException("Failed to run pre-uninstall hooks, uninstalling anyways", ex);
                }
            }

            //try {
            //    this.ErrorIfThrows(() => fixPinnedExecutables(new SemanticVersion(255, 255, 255), true));
            //} catch { }

            this.ErrorIfThrows(() => Utility.DeleteFileOrDirectoryHardOrGiveUp(rootAppDirectory),
                "Failed to delete app directory: " + rootAppDirectory);

            // NB: We drop this file here so that --checkInstall will ignore 
            // this folder - if we don't do this, users who "accidentally" run as 
            // administrator will find the app reinstalling itself on every
            // reboot
            if (!Directory.Exists(rootAppDirectory)) {
                Directory.CreateDirectory(rootAppDirectory);
            }

            File.WriteAllText(Path.Combine(rootAppDirectory, ".dead"), " ");
        }

        Task<string> installPackageToAppDir(UpdateInfo updateInfo, ReleaseEntry release, Action<int> progressCallback)
        {
            return Task.Run(async () => {
                var target = getDirectoryForRelease(release.Version);

                // NB: This might happen if we got killed partially through applying the release
                if (target.Exists) {
                    this.Log().Warn("Found partially applied release folder, killing it: " + target.FullName);
                    Utility.DeleteFileOrDirectoryHardOrGiveUp(target.FullName);
                }

                target.Create();

                this.Log().Info("Writing files to app directory: {0}", target.FullName);
                await ReleasePackage.ExtractZipForInstall(
                    Path.Combine(updateInfo.PackageDirectory, release.Filename),
                    target.FullName,
                    AppDirectory,
                    progressCallback).ConfigureAwait(false);

                return target.FullName;
            });
        }

        async Task<ReleaseEntry> createFullPackagesFromDeltas(IEnumerable<ReleaseEntry> releasesToApply, ReleaseEntry currentVersion, ApplyReleasesProgress progress)
        {
            var rootAppDirectory = AppDirectory;
            Contract.Requires(releasesToApply != null);

            progress = progress ?? new ApplyReleasesProgress(releasesToApply.Count(), x => { });

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

            // Progress calculation is "complex" here. We need to known how many releases, and then give each release a similar amount of
            // progress. For example, when applying 5 releases:
            //
            // release 1: 00 => 20
            // release 2: 20 => 40
            // release 3: 40 => 60
            // release 4: 60 => 80
            // release 5: 80 => 100
            // 

            // Smash together our base full package and the nearest delta
            var ret = await Task.Run(() => {
                var basePkg = new ReleasePackage(Path.Combine(rootAppDirectory, "packages", currentVersion.Filename));
                var deltaPkg = new ReleasePackage(Path.Combine(rootAppDirectory, "packages", releasesToApply.First().Filename));

                var deltaBuilder = new DeltaPackageBuilder(Directory.GetParent(rootAppDirectory).FullName);

                return deltaBuilder.ApplyDeltaPackage(basePkg, deltaPkg,
                    Regex.Replace(deltaPkg.InputPackageFile, @"-delta.nupkg$", ".nupkg", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    x => progress.ReportReleaseProgress(x));
            }).ConfigureAwait(false);

            progress.FinishRelease();

            if (releasesToApply.Count() == 1) {
                return ReleaseEntry.GenerateFromFile(ret.InputPackageFile);
            }

            var fi = new FileInfo(ret.InputPackageFile);
            var entry = ReleaseEntry.GenerateFromFile(fi.OpenRead(), fi.Name);

            // Recursively combine the rest of them
            return await createFullPackagesFromDeltas(releasesToApply.Skip(1), entry, progress).ConfigureAwait(false);
        }

        void executeSelfUpdate(SemanticVersion currentVersion)
        {
            var targetDir = getDirectoryForRelease(currentVersion);
            var newSquirrel = Path.Combine(targetDir.FullName, "Squirrel.exe");
            if (!File.Exists(newSquirrel)) {
                return;
            }

            // If we're running in the context of Update.exe, we can't 
            // update ourselves. Instead, ask the new Update.exe to do it
            // once we exit
            var ourLocation = SquirrelRuntimeInfo.EntryExePath;
            if (ourLocation != null && Path.GetFileName(ourLocation).Equals("update.exe", StringComparison.OrdinalIgnoreCase)) {
                var appName = targetDir.Parent.Name;

                Process.Start(newSquirrel, "--updateSelf=" + ourLocation);
                return;
            }

            // If we're *not* Update.exe, this is easy, it's just a file copy
            Utility.Retry(() =>
                File.Copy(newSquirrel, Path.Combine(targetDir.Parent.FullName, "Update.exe"), true));
        }

        async Task invokePostInstall(SemanticVersion currentVersion, bool isInitialInstall, bool firstRunOnly, bool silentInstall)
        {
            var targetDir = getDirectoryForRelease(currentVersion);
            var command = isInitialInstall ? "--squirrel-install" : "--squirrel-updated";
            var args = new string[] { command, currentVersion.ToString() };

            var squirrelApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(targetDir.FullName);

            this.Log().Info("Squirrel Enabled Apps: [{0}]", String.Join(",", squirrelApps));

            // For each app, run the install command in-order and wait
            if (!firstRunOnly) await squirrelApps.ForEachAsync(async exe => {
                using (var cts = new CancellationTokenSource()) {
                    cts.CancelAfter(30 * 1000);

                    try {
                        await Utility.InvokeProcessAsync(exe, args, cts.Token, Path.GetDirectoryName(exe)).ConfigureAwait(false);
                    } catch (Exception ex) {
                        this.Log().ErrorException("Couldn't run Squirrel hook, continuing: " + exe, ex);
                    }
                }
            }, 1 /* at a time */).ConfigureAwait(false);

            // If this is the first run, we run the apps with first-run and 
            // *don't* wait for them, since they're probably the main EXE
            if (squirrelApps.Count == 0) {
                this.Log().Warn("No apps are marked as Squirrel-aware! Going to run them all");

                squirrelApps = targetDir.EnumerateFiles()
                    .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Where(x => !x.Name.StartsWith("squirrel.", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.FullName)
                    .ToList();

                // Create shortcuts for apps automatically if they didn't
                // create any Squirrel-aware apps
                //squirrelApps.ForEach(x => CreateShortcutsForExecutable(Path.GetFileName(x), ShortcutLocation.Desktop | ShortcutLocation.StartMenu, isInitialInstall == false, null, null));
            }

            if (!isInitialInstall || silentInstall) return;

            // for the hooks we run in the 'app-{ver}' directories, but for finally starting the app we run from 'current' junction
            var latestAppDir = Utility.UpdateAndRetrieveCurrentFolder(AppDirectory, true);
            squirrelApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(latestAppDir);
            squirrelApps
                .Select(exe => new ProcessStartInfo(exe, "--squirrel-firstrun") { WorkingDirectory = Path.GetDirectoryName(exe) })
                .ForEach(info => Process.Start(info));
        }

        async Task cleanDeadVersions(SemanticVersion currentVersion, SemanticVersion newVersion, bool forceUninstall = false)
        {
            var rootAppDirectory = AppDirectory;
            if (newVersion == null) return;

            var di = new DirectoryInfo(AppDirectory);
            if (!di.Exists) return;

            this.Log().Info("cleanDeadVersions: checking for version {0}", newVersion);

            string currentVersionFolder = null;
            if (currentVersion != null) {
                currentVersionFolder = getDirectoryForRelease(currentVersion).Name;
                this.Log().Info("cleanDeadVersions: exclude current version folder {0}", currentVersionFolder);
            }

            string newVersionFolder = null;
            if (newVersion != null) {
                newVersionFolder = getDirectoryForRelease(newVersion).Name;
                this.Log().Info("cleanDeadVersions: exclude new version folder {0}", newVersionFolder);
            }

            var toCleanup = di.GetDirectories()
                .Where(x => x.Name.ToLowerInvariant().Contains("app-"))
                .Where(x => x.Name != newVersionFolder && x.Name != currentVersionFolder)
                .Where(x => !isAppFolderDead(x.FullName));

            if (forceUninstall == false) {
                await toCleanup.ForEachAsync(async x => {
                    var squirrelApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(x.FullName);
                    var args = new string[] { "--squirrel-obsolete", x.Name.Replace("app-", "") };

                    if (squirrelApps.Count > 0) {
                        // For each app, run the install command in-order and wait
                        await squirrelApps.ForEachAsync(async exe => {
                            using (var cts = new CancellationTokenSource()) {
                                cts.CancelAfter(10 * 1000);

                                try {
                                    await Utility.InvokeProcessAsync(exe, args, cts.Token).ConfigureAwait(false);
                                } catch (Exception ex) {
                                    this.Log().ErrorException("Coudln't run Squirrel hook, continuing: " + exe, ex);
                                }
                            }
                        }, 1 /* at a time */).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            }

            // Include dead folders in folders to :fire:
            toCleanup = di.GetDirectories()
                .Where(x => x.Name.ToLowerInvariant().Contains("app-"))
                .Where(x => x.Name != newVersionFolder && x.Name != currentVersionFolder);

            // Get the current process list in an attempt to not burn 
            // directories which have running processes
            var runningProcesses = Utility.EnumerateProcesses();

            // Finally, clean up the app-X.Y.Z directories
            await toCleanup.ForEachAsync(x => {
                try {
                    if (runningProcesses.All(p => p.Item1 == null || !p.Item1.StartsWith(x.FullName, StringComparison.OrdinalIgnoreCase))) {
                        Utility.DeleteFileOrDirectoryHardOrGiveUp(x.FullName);
                    }

                    if (Directory.Exists(x.FullName)) {
                        // NB: If we cannot clean up a directory, we need to make 
                        // sure that anyone finding it later won't attempt to run
                        // Squirrel events on it. We'll mark it with a .dead file
                        markAppFolderAsDead(x.FullName);
                    }
                } catch (UnauthorizedAccessException ex) {
                    this.Log().WarnException("Couldn't delete directory: " + x.FullName, ex);

                    // NB: Same deal as above
                    markAppFolderAsDead(x.FullName);
                }
            }).ConfigureAwait(false);

            // Clean up the packages directory too
            var releasesFile = Utility.LocalReleaseFileForAppDir(rootAppDirectory);
            var entries = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesFile, Encoding.UTF8));
            var pkgDir = Utility.PackageDirectoryForAppDir(rootAppDirectory);
            var releaseEntry = default(ReleaseEntry);

            foreach (var entry in entries) {
                if (entry.Version == newVersion) {
                    releaseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(pkgDir, entry.Filename));
                    continue;
                }

                File.Delete(Path.Combine(pkgDir, entry.Filename));
            }

            ReleaseEntry.WriteReleaseFile(new[] { releaseEntry }, releasesFile);
        }

        static void markAppFolderAsDead(string appFolderPath)
        {
            File.WriteAllText(Path.Combine(appFolderPath, ".dead"), "");
        }

        static bool isAppFolderDead(string appFolderPath)
        {
            return File.Exists(Path.Combine(appFolderPath, ".dead"));
        }

        internal async Task<List<ReleaseEntry>> updateLocalReleasesFile()
        {
            return await Task.Run(() => ReleaseEntry.BuildReleasesFile(PackagesDirectory)).ConfigureAwait(false);
        }

        DirectoryInfo getDirectoryForRelease(SemanticVersion releaseVersion)
        {
            return new DirectoryInfo(Path.Combine(AppDirectory, "app-" + releaseVersion));
        }
    }
}
