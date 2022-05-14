using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;
using System.Threading;
using System.Runtime.Versioning;
using Squirrel.NuGet;

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

            // Progress range: 00 -> 60
            // takes the latest local package, applies a series of delta to get the full package to update to
            var release = await createFullPackagesFromDeltas(updateInfo.ReleasesToApply, updateInfo.LatestLocalReleaseEntry, new ApplyReleasesProgress(updateInfo.ReleasesToApply.Count, x => progress(CalculateProgress(x, 0, 60)))).ConfigureAwait(false);

            progress(60);

            if (release == null) {
                if (attemptingFullInstall) {
                    throw new InvalidOperationException("No release was provided to install");
                }

                progress(100);
                return _config.GetLatestVersion().DirectoryPath;
            }

            // Progress range: 60 -> 80
            // extracts the new package to a version dir (app-{ver}) inside VersionStagingDir
            var newVersionDir = await this.ErrorIfThrows(() => installPackageToStagingDir(updateInfo, release, x => progress(CalculateProgress(x, 60, 80))),
                "Failed to install package to app dir").ConfigureAwait(false);

            progress(80);

            this.Log().Info("Updating local release file");
            var currentReleases = await Task.Run(() => ReleaseEntry.BuildReleasesFile(_config.PackagesDir)).ConfigureAwait(false);

            if (SquirrelRuntimeInfo.IsWindows) {
                progress(85);

                this.Log().Info("Running post-install hooks");
                newVersionDir = await invokePostInstall(newVersionDir, attemptingFullInstall, false, silentInstall).ConfigureAwait(false);

                progress(90);

                executeSelfUpdate(newVersionDir);
            }

            progress(95);

            try {
                await cleanDeadVersions().ConfigureAwait(false);
            } catch (Exception ex) {
                this.Log().WarnException("Failed to clean dead versions, continuing anyways", ex);
            }

            progress(100);

            return newVersionDir;
        }

        /// <inheritdoc/>
        [SupportedOSPlatform("windows")]
        public async Task FullUninstall()
        {
            var rootAppDirectory = _config.RootAppDir;
            await acquireUpdateLock().ConfigureAwait(false);

            this.Log().Info("Starting full uninstall");
            KillAllExecutablesBelongingToPackage();

            try {
                var releases = _config.GetVersions().ToArray();
                if (releases.Any()) {
                    var latest = releases.OrderByDescending(x => x.Version).FirstOrDefault();
                    var currentRelease = new DirectoryInfo(latest.DirectoryPath);
                    var currentVersion = latest.Version;

                    if (currentRelease.Exists) {
                        if (isAppFolderDead(currentRelease.FullName)) throw new Exception("App folder is dead, but we're trying to uninstall it?");
                        var squirrelAwareApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(currentRelease.FullName);
                        foreach (var exe in squirrelAwareApps) {
                            using (var cts = new CancellationTokenSource()) {
                                cts.CancelAfter(10 * 1000);
                                try {
                                    var args = new string[] { "--squirrel-uninstall", currentVersion.ToString() };
                                    await ProcessUtil.InvokeProcessAsync(exe, args, Path.GetDirectoryName(exe), cts.Token).ConfigureAwait(false);
                                } catch (Exception ex) {
                                    this.Log().ErrorException("Failed to run cleanup hook, continuing: " + exe, ex);
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                this.Log().WarnException("Unable to run uninstall hooks", ex);
            }

            try {
                RemoveAllShortcutsForPackage();
                RemoveUninstallerRegistryEntry();
            } catch (Exception ex) {
                this.Log().WarnException("Unable to uninstall shortcuts or registry entry. Continuing anyway...", ex);
            }

            this.Log().Info("Deleting files in app directory: " + rootAppDirectory);
            this.ErrorIfThrows(() => Utility.DeleteFileOrDirectoryHard(rootAppDirectory, throwOnFailure: false),
                "Failed to delete app directory: " + rootAppDirectory);

            // NB: We drop this file here so that --checkInstall will ignore 
            // this folder - if we don't do this, users who "accidentally" run as 
            // administrator will find the app reinstalling itself on every
            // reboot
            if (!Directory.Exists(rootAppDirectory)) {
                Directory.CreateDirectory(rootAppDirectory);
            }

            File.WriteAllText(Path.Combine(rootAppDirectory, ".dead"), " ");
            this.Log().Info("Done full uninstall.");
        }

        Task<string> installPackageToStagingDir(UpdateInfo updateInfo, ReleaseEntry release, Action<int> progressCallback)
        {
            return Task.Run(async () => {
                var target = new DirectoryInfo(_config.GetVersionStagingPath(release.Version));

                // NB: This might happen if we got killed partially through applying the release
                if (target.Exists) {
                    this.Log().Warn("Found partially applied release folder, killing it: " + target.FullName);
                    Utility.DeleteFileOrDirectoryHard(target.FullName, throwOnFailure: true, renameFirst: true);
                }

                target.Create();

                this.Log().Info("Writing files to app directory: {0}", target.FullName);
                await ZipPackage.ExtractZipReleaseForInstall(
                    Path.Combine(updateInfo.PackageDirectory, release.Filename),
                    target.FullName,
                    _config.RootAppDir,
                    progressCallback).ConfigureAwait(false);

                return target.FullName;
            });
        }

        async Task<ReleaseEntry> createFullPackagesFromDeltas(IEnumerable<ReleaseEntry> releasesToApply, ReleaseEntry currentVersion, ApplyReleasesProgress progress)
        {
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
            var outputPackageZip = await Task.Run(() => {
                var basePkg = Path.Combine(_config.PackagesDir, currentVersion.Filename);
                var deltaPkg = Path.Combine(_config.PackagesDir, releasesToApply.First().Filename);

                return new DeltaPackage(_config.AppTempDir).ApplyDeltaPackage(basePkg, deltaPkg,
                    Regex.Replace(deltaPkg, @"-delta.nupkg$", ".nupkg", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    x => progress.ReportReleaseProgress(x));
            }).ConfigureAwait(false);

            progress.FinishRelease();

            if (releasesToApply.Count() == 1) {
                return ReleaseEntry.GenerateFromFile(outputPackageZip);
            }

            var fi = new FileInfo(outputPackageZip);
            var entry = ReleaseEntry.GenerateFromFile(fi.OpenRead(), fi.Name);

            // Recursively combine the rest of them
            return await createFullPackagesFromDeltas(releasesToApply.Skip(1), entry, progress).ConfigureAwait(false);
        }

        [SupportedOSPlatform("windows")]
        void executeSelfUpdate(string newVersionDir)
        {
            var newSquirrel = Path.Combine(newVersionDir, "Squirrel.exe");
            if (!File.Exists(newSquirrel)) {
                return;
            }

            // If we're running in the context of Update.exe, we can't 
            // update ourselves. Instead, ask the new Update.exe to do it
            // once we exit
            var ourLocation = SquirrelRuntimeInfo.EntryExePath;
            if (ourLocation != null && Path.GetFileName(ourLocation).Equals("update.exe", StringComparison.OrdinalIgnoreCase)) {
                Process.Start(newSquirrel, "--updateSelf=" + ourLocation);
                return;
            }

            // If we're *not* Update.exe, this is easy, it's just a file copy
            Utility.Retry(() => File.Copy(newSquirrel, _config.UpdateExePath, true));
        }

        [SupportedOSPlatform("windows")]
        async Task<string> invokePostInstall(string targetDir, bool isInitialInstall, bool firstRunOnly, bool silentInstall)
        {
            var versionInfo = _config.GetVersionInfoFromDirectory(targetDir);

            var command = isInitialInstall ? "--squirrel-install" : "--squirrel-updated";
            var args = new string[] { command, versionInfo.Version.ToString() };

            var squirrelApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(targetDir);

            this.Log().Info("Squirrel Enabled Apps: [{0}]", String.Join(",", squirrelApps));

            // For each app, run the install command in-order and wait
            if (!firstRunOnly) await squirrelApps.ForEachAsync(async exe => {
                using (var cts = new CancellationTokenSource()) {
                    cts.CancelAfter(30 * 1000);

                    try {
                        await ProcessUtil.InvokeProcessAsync(exe, args, Path.GetDirectoryName(exe), cts.Token).ConfigureAwait(false);
                    } catch (Exception ex) {
                        this.Log().ErrorException("Couldn't run Squirrel hook, continuing: " + exe, ex);
                    }
                }
            }, 1 /* at a time */).ConfigureAwait(false);

            // If this is the first run, we run the apps with first-run and 
            // *don't* wait for them, since they're probably the main EXE
            if (squirrelApps.Count == 0) {
                this.Log().Warn("No apps are marked as Squirrel-aware! Going to run them all");

                squirrelApps = Directory.EnumerateFiles(targetDir)
                    .Where(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Where(x => !x.StartsWith("squirrel.", StringComparison.OrdinalIgnoreCase))
                    .Select(x => Path.GetFullPath(x))
                    .ToList();

                // Create shortcuts for apps automatically if they didn't
                // create any Squirrel-aware apps
                //squirrelApps.ForEach(x => CreateShortcutsForExecutable(Path.GetFileName(x), ShortcutLocation.Desktop | ShortcutLocation.StartMenu, isInitialInstall == false, null, null));
            }

            if (!isInitialInstall || silentInstall) return targetDir;

            // for the hooks we run in the 'app-{ver}' directories, but for finally starting the app we run from 'current' junction
            var latestAppDir = _config.UpdateAndRetrieveCurrentFolder(true);
            squirrelApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(latestAppDir);
            squirrelApps
                .Select(exe => new ProcessStartInfo(exe, "--squirrel-firstrun") { WorkingDirectory = Path.GetDirectoryName(exe) })
                .ForEach(info => Process.Start(info));

            return latestAppDir;
        }

        async Task cleanDeadVersions()
        {
            var versions = _config.GetVersions().ToArray();
            var latest = _config.GetLatestVersion(versions);

            List<string> toDelete = new List<string>();

            // execute --obsolete hooks on any old non-dead folders
            foreach (var v in versions) {
                // don't delete current / latest versions
                if (v.IsCurrent || v.IsExecuting) continue;
                if (v.Version == latest.Version) continue;

                toDelete.Add(v.DirectoryPath);

                // don't run hooks if the folder is already dead.
                if (isAppFolderDead(v.DirectoryPath)) continue;

                if (SquirrelRuntimeInfo.IsWindows) {
                    var squirrelApps = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(v.DirectoryPath);
                    var args = new string[] { "--squirrel-obsolete", v.Version.ToString() };

                    if (squirrelApps.Count > 0) {
                        // For each app, run the install command in-order and wait
                        foreach (var exe in squirrelApps) {
                            using (var cts = new CancellationTokenSource()) {
                                cts.CancelAfter(10 * 1000);
                                try {
                                    await ProcessUtil.InvokeProcessAsync(exe, args, Path.GetDirectoryName(exe), cts.Token).ConfigureAwait(false);
                                } catch (Exception ex) {
                                    this.Log().ErrorException("Coudln't run Squirrel hook, continuing: " + exe, ex);
                                }
                            }
                        }
                    }
                }

                // mark this as dead so we never run hooks again
                markAppFolderAsDead(v.DirectoryPath);
            }

            var runningProcesses = Utility.EnumerateProcesses();

            foreach (var dir in toDelete) {
                // skip any directories with running processes
                if (runningProcesses.Any(r => Utility.IsFileInDirectory(r.ProcessExePath, dir))) continue;

                // try to delete dir, don't care if this fails
                Utility.DeleteFileOrDirectoryHard(dir, throwOnFailure: false, renameFirst: true);

                // if the folder exists, just double check that it's dead.
                if (Directory.Exists(dir)) markAppFolderAsDead(dir);
            }

            // Clean up the packages directory too, everything except latest full package
            var localPackages = _config.GetLocalPackages();
            var latestFullPackage = localPackages
                .Where(p => !p.IsDelta)
                .OrderByDescending(v => v.PackageVersion)
                .FirstOrDefault();

            if (latestFullPackage != default) {
                foreach (var package in localPackages) {
                    // don't delete the latest package
                    if (package.PackageVersion == latestFullPackage.PackageVersion) continue;
                    Utility.DeleteFileOrDirectoryHard(package.PackagePath, throwOnFailure: false);
                }
            }

            // generate new RELEASES file
            ReleaseEntry.BuildReleasesFile(_config.PackagesDir);
        }

        static void markAppFolderAsDead(string appFolderPath)
        {
            File.WriteAllText(Path.Combine(appFolderPath, ".dead"), "");
        }

        static bool isAppFolderDead(string appFolderPath)
        {
            return File.Exists(Path.Combine(appFolderPath, ".dead"));
        }
    }
}
