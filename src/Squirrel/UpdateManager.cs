using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Threading.Tasks;
using NuGet;
using ReactiveUIMicro;
using Squirrel.Core;

// NB: These are whitelisted types from System.IO, so that we always end up 
// using fileSystem instead.
using Squirrel.Core.Extensions;
using FileAccess = System.IO.FileAccess;
using FileMode = System.IO.FileMode;
using MemoryStream = System.IO.MemoryStream;
using Path = System.IO.Path;
using StreamReader = System.IO.StreamReader;

namespace Squirrel
{
    public sealed class UpdateManager : IUpdateManager, IEnableLogger
    {
        readonly IRxUIFullLogger log;
        readonly IFileSystemFactory fileSystem;
        readonly string rootAppDirectory;
        readonly string applicationName;
        readonly IUrlDownloader urlDownloader;
        readonly string updateUrlOrPath;
        readonly FrameworkVersion appFrameworkVersion;

        IDisposable updateLock;

        public UpdateManager(string urlOrPath, 
            string applicationName,
            FrameworkVersion appFrameworkVersion,
            string rootDirectory = null,
            IFileSystemFactory fileSystem = null,
            IUrlDownloader urlDownloader = null)
        {
            Contract.Requires(!String.IsNullOrEmpty(urlOrPath));
            Contract.Requires(!String.IsNullOrEmpty(applicationName));

            // XXX: ALWAYS BE LOGGING
            log = new WrappingFullLogger(new FileLogger(applicationName), typeof(UpdateManager));

            updateUrlOrPath = urlOrPath;
            this.applicationName = applicationName;
            this.appFrameworkVersion = appFrameworkVersion;

            this.rootAppDirectory = Path.Combine(rootDirectory ?? getLocalAppDataDirectory(), applicationName);
            this.fileSystem = fileSystem ?? AnonFileSystem.Default;

            this.urlDownloader = urlDownloader ?? new DirectUrlDownloader(fileSystem);
        }

        public string PackageDirectory {
            get { return Path.Combine(rootAppDirectory, "packages"); }
        }

        public string LocalReleaseFile {
            get { return Path.Combine(PackageDirectory, "RELEASES"); }
        }

        public IObservable<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates = false, IObserver<int> progress = null)
        {
            return acquireUpdateLock().SelectMany(_ => checkForUpdate(ignoreDeltaUpdates, progress));
        }

        IObservable<UpdateInfo> checkForUpdate(bool ignoreDeltaUpdates = false, IObserver<int> progress = null)
        {
            var localReleases = Enumerable.Empty<ReleaseEntry>();
            progress = progress ?? new Subject<int>();

            try {
                var file = fileSystem.GetFileInfo(LocalReleaseFile).OpenRead();

                // NB: sr disposes file
                using (var sr = new StreamReader(file, Encoding.UTF8)) {
                    localReleases = ReleaseEntry.ParseReleaseFile(sr.ReadToEnd());
                }
            } catch (Exception ex) {
                // Something has gone wrong, we'll start from scratch.
                log.WarnException("Failed to load local release list", ex);
                initializeClientAppDirectory();
            }

            IObservable<string> releaseFile;

            // Fetch the remote RELEASES file, whether it's a local dir or an 
            // HTTP URL
            try {
                if (isHttpUrl(updateUrlOrPath)) {
                    log.Info("Downloading RELEASES file from {0}", updateUrlOrPath);
                    releaseFile = urlDownloader.DownloadUrl(String.Format("{0}/{1}", updateUrlOrPath, "RELEASES"), progress)
                        .Catch<string, TimeoutException>(ex => {
                            log.Info("Download timed out (returning blank release list)");
                            return Observable.Return(String.Empty);
                        })
                        .Catch<string, WebException>(ex => {
                            log.InfoException("Download resulted in WebException (returning blank release list)", ex);
                            return Observable.Return(String.Empty);
                        });
                } else {
                    log.Info("Reading RELEASES file from {0}", updateUrlOrPath);

                    if (!fileSystem.GetDirectoryInfo(updateUrlOrPath).Exists) {
                        var message =
                            String.Format(
                                "The directory {0} does not exist, something is probably broken with your application", updateUrlOrPath);
                        var ex = new SquirrelConfigurationException(message);
                        return Observable.Throw<UpdateInfo>(ex);
                    }

                    var fi = fileSystem.GetFileInfo(Path.Combine(updateUrlOrPath, "RELEASES"));
                    if (!fi.Exists) {
                        var message = String.Format(
                            "The file {0} does not exist, something is probably broken with your application", fi.FullName);

                        log.Warn(message);

                        var packages = fileSystem.GetDirectoryInfo(updateUrlOrPath).GetFiles("*.nupkg");
                        if (packages.Length == 0) {
                            var ex = new SquirrelConfigurationException(message);
                            return Observable.Throw<UpdateInfo>(ex);
                        }

                        // NB: Create a new RELEASES file since we've got a directory of packages
                        ReleaseEntry.WriteReleaseFile(
                            packages.Select(x => ReleaseEntry.GenerateFromFile(x.FullName)), fi.FullName);
                    }

                    using (var sr = new StreamReader(fi.OpenRead(), Encoding.UTF8)) {
                        var text = sr.ReadToEnd();
                        releaseFile = Observable.Return(text);
                    }

                    progress.OnNext(100);
                    progress.OnCompleted();
                }               
            } catch (Exception ex) {
                progress.OnCompleted();
                return Observable.Throw<UpdateInfo>(ex);
            }

            // Return null if no updates found
            var ret = releaseFile
                .Select(ReleaseEntry.ParseReleaseFile)
                .SelectMany(releases =>
                    releases.Any() ? determineUpdateInfo(localReleases, releases, ignoreDeltaUpdates)
                        : Observable.Return<UpdateInfo>(null))
                .PublishLast();

            ret.Connect();
            return ret;
        }

        public IObservable<Unit> DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, IObserver<int> progress = null)
        {
            return acquireUpdateLock().SelectMany(_ => downloadReleases(releasesToDownload, progress));
        }

        IObservable<Unit> downloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, IObserver<int> progress = null)
        {
            progress = progress ?? new Subject<int>();
            IObservable<Unit> downloadResult = null;

            if (isHttpUrl(updateUrlOrPath)) {
                var urls = releasesToDownload.Select(x => String.Format("{0}/{1}", updateUrlOrPath, x.Filename));
                var paths = releasesToDownload.Select(x => Path.Combine(rootAppDirectory, "packages", x.Filename));

                downloadResult = urlDownloader.QueueBackgroundDownloads(urls, paths, progress);
            } else {
                var toIncrement = 100.0 / releasesToDownload.Count();

                // Do a parallel copy from the remote directory to the local
                var downloads = releasesToDownload.ToObservable()
                    .Select(x => fileSystem.CopyAsync(
                        Path.Combine(updateUrlOrPath, x.Filename),
                        Path.Combine(rootAppDirectory, "packages", x.Filename)))
                    .Merge(4)
                    .Publish();

                downloads
                    .Scan(0.0, (acc, _) => acc + toIncrement)
                    .Select(x => (int) x)
                    .Subscribe(progress);

                downloadResult = downloads.TakeLast(1);
                downloads.Connect();
            }

            return downloadResult.SelectMany(_ => checksumAllPackages(releasesToDownload));
        }

        public IObservable<List<string>> ApplyReleases(UpdateInfo updateInfo, IObserver<int> progress = null)
        {
            progress = progress ?? new Subject<int>();

            // NB: It's important that we update the local releases file *only* 
            // once the entire operation has completed, even though we technically
            // could do it after DownloadUpdates finishes. We do this so that if
            // we get interrupted / killed during this operation, we'll start over
            return Observable.Using(_ => acquireUpdateLock().ToTask(), (dontcare, ct) => {
                var obs = cleanDeadVersions(updateInfo.CurrentlyInstalledVersion != null ? updateInfo.CurrentlyInstalledVersion.Version : null)
                    .Do(_ => progress.OnNext(10))
                    .SelectMany(_ => createFullPackagesFromDeltas(updateInfo.ReleasesToApply, updateInfo.CurrentlyInstalledVersion))
                    .Do(_ => progress.OnNext(50))
                    .Select(release => installPackageToAppDir(updateInfo, release))
                    .Do(_ => progress.OnNext(95))
                    .SelectMany(ret => UpdateLocalReleasesFile().Select(_ => ret))
                    .Finally(() => progress.OnCompleted())
                    .PublishLast();

                obs.Connect();

                // NB: This overload of Using is high as a kite.
                var tcs = new TaskCompletionSource<IObservable<List<string>>>();
                tcs.SetResult(obs);
                return tcs.Task;
            });
        }

        public IObservable<Unit> UpdateLocalReleasesFile()
        {
            return acquireUpdateLock().SelectMany(_ => Observable.Start(() => 
                ReleaseEntry.BuildReleasesFile(PackageDirectory, fileSystem), RxApp.TaskpoolScheduler));
        }

        public IObservable<Unit> FullUninstall(Version version = null)
        {
            version = version ?? new Version(255, 255, 255, 255);
            log.Info("Uninstalling version '{0}'", version);
            return acquireUpdateLock().SelectMany(_ => fullUninstall(version));
        }

        IEnumerable<DirectoryInfoBase> getReleases()
        {
            var rootDirectory = fileSystem.GetDirectoryInfo(rootAppDirectory);

            if (!rootDirectory.Exists)
                return Enumerable.Empty<DirectoryInfoBase>();

            return rootDirectory.GetDirectories()
                        .Where(x => x.Name.StartsWith("app-", StringComparison.InvariantCultureIgnoreCase));
        }

        IEnumerable<DirectoryInfoBase> getOldReleases(Version version)
        {
            return getReleases()
                    .Where(x => x.Name.ToVersion() < version)
                    .ToArray();
        }

        IObservable<Unit> fullUninstall(Version version)
        {
           // find all the old releases (and this one)
           return getOldReleases(version)
                    .Concat(new [] { getDirectoryForRelease(version) })
                    .Where(d => d.Exists)
                    .OrderBy(d => d.Name)
                    .Select(d => d.FullName)
                    .ToObservable()
                    .SelectMany(dir => {
                        // cleanup each version
                        runAppCleanup(dir);
                        runAppUninstall(dir);
                        // and then force a delete on each folder
                        return Utility.DeleteDirectory(dir)
                                .Catch<Unit, Exception>(ex => {
                                    var message = String.Format("Uninstall failed to delete dir '{0}', punting to next reboot", dir);
                                    log.WarnException(message, ex);
                                    return Observable.Start(
                                        () => Utility.DeleteDirectoryAtNextReboot(rootAppDirectory));
                                });
                    })
                    .Aggregate(Unit.Default, (acc, x) => acc)
                    .SelectMany(_ => {
                        // if there are no other relases found
                        // delete the root directory too
                        if (!getReleases().Any()) {
                            return Utility.DeleteDirectory(rootAppDirectory);
                        }
                        return Observable.Return(Unit.Default);
                    });
        }

        public void Dispose()
        {
            var disp = Interlocked.Exchange(ref updateLock, null);
            if (disp != null) {
                disp.Dispose();
            }
        }

        ~UpdateManager()
        {
            if (updateLock != null) {
                throw new Exception("You must dispose UpdateManager!");
            }
        }

        IObservable<IDisposable> acquireUpdateLock()
        {
            if (updateLock != null) return Observable.Return(updateLock);

            return Observable.Start(() => {
                var key = Utility.CalculateStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(rootAppDirectory)));

                IDisposable theLock;
                try {
                    theLock = RxApp.InUnitTestRunner() ?
                        Disposable.Empty : new SingleGlobalInstance(key, 2000);
                } catch (TimeoutException) {
                    throw new TimeoutException("Couldn't acquire update lock, another instance may be running updates");
                }

                var ret = Disposable.Create(() => {
                    theLock.Dispose();
                    updateLock = null;
                });

                updateLock = ret;
                return ret;
            });
        }

        static string getLocalAppDataDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        DirectoryInfoBase getDirectoryForRelease(Version releaseVersion)
        {
            return fileSystem.GetDirectoryInfo(Path.Combine(rootAppDirectory, "app-" + releaseVersion));
        }

        //
        // CheckForUpdate methods
        //

        void initializeClientAppDirectory()
        {
            // On bootstrap, we won't have any of our directories, create them
            var pkgDir = Path.Combine(rootAppDirectory, "packages");
            if (fileSystem.GetDirectoryInfo(pkgDir).Exists) {
                fileSystem.DeleteDirectoryRecursive(pkgDir);
            }

            fileSystem.CreateDirectoryRecursive(pkgDir);
        }

        IObservable<UpdateInfo> determineUpdateInfo(IEnumerable<ReleaseEntry> localReleases, IEnumerable<ReleaseEntry> remoteReleases, bool ignoreDeltaUpdates)
        {
            localReleases = localReleases ?? Enumerable.Empty<ReleaseEntry>();

            if (remoteReleases == null) {
                log.Warn("Release information couldn't be determined due to remote corrupt RELEASES file");
                return Observable.Throw<UpdateInfo>(new Exception("Corrupt remote RELEASES file"));
            }

            if (localReleases.Count() == remoteReleases.Count()) {
                log.Info("No updates, remote and local are the same");

                var latestFullRelease = findCurrentVersion(remoteReleases);
                var currentRelease = findCurrentVersion(localReleases);

                var info = UpdateInfo.Create(currentRelease, new[] {latestFullRelease}, PackageDirectory,appFrameworkVersion);
                return Observable.Return(info);
            }

            if (ignoreDeltaUpdates) {
                remoteReleases = remoteReleases.Where(x => !x.IsDelta);
            }

            if (!localReleases.Any()) {
                log.Warn("First run or local directory is corrupt, starting from scratch");

                var latestFullRelease = findCurrentVersion(remoteReleases);
                return Observable.Return(UpdateInfo.Create(findCurrentVersion(localReleases), new[] {latestFullRelease}, PackageDirectory, appFrameworkVersion));
            }

            if (localReleases.Max(x => x.Version) > remoteReleases.Max(x => x.Version)) {
                log.Warn("hwhat, local version is greater than remote version");

                var latestFullRelease = findCurrentVersion(remoteReleases);
                return Observable.Return(UpdateInfo.Create(findCurrentVersion(localReleases), new[] {latestFullRelease}, PackageDirectory, appFrameworkVersion));
            }

            return Observable.Return(UpdateInfo.Create(findCurrentVersion(localReleases), remoteReleases, PackageDirectory, appFrameworkVersion));
        }

        static ReleaseEntry findCurrentVersion(IEnumerable<ReleaseEntry> localReleases)
        {
            if (!localReleases.Any()) {
                return null;
            }

            return localReleases.MaxBy(x => x.Version).SingleOrDefault(x => !x.IsDelta);
        }

        //
        // DownloadReleases methods
        //
        
        static bool isHttpUrl(string urlOrPath)
        {
            try {
                var url = new Uri(urlOrPath);
                return new[] {"https", "http"}.Contains(url.Scheme.ToLowerInvariant());
            } catch (Exception) {
                return false;
            }
        }

        IObservable<Unit> checksumAllPackages(IEnumerable<ReleaseEntry> releasesDownloaded)
        {
            return releasesDownloaded
                .MapReduce(x => Observable.Start(() => checksumPackage(x)))
                .Select(_ => Unit.Default);
        }

        void checksumPackage(ReleaseEntry downloadedRelease)
        {
            var targetPackage = fileSystem.GetFileInfo(
                Path.Combine(rootAppDirectory, "packages", downloadedRelease.Filename));

            if (!targetPackage.Exists) {
                log.Error("File {0} should exist but doesn't", targetPackage.FullName);
                throw new Exception("Checksummed file doesn't exist: " + targetPackage.FullName);
            }

            if (targetPackage.Length != downloadedRelease.Filesize) {
                log.Error("File Length should be {0}, is {1}", downloadedRelease.Filesize, targetPackage.Length);
                targetPackage.Delete();
                throw new Exception("Checksummed file size doesn't match: " + targetPackage.FullName);
            }

            using (var file = targetPackage.OpenRead()) {
                var hash = Utility.CalculateStreamSHA1(file);
                if (!hash.Equals(downloadedRelease.SHA1,StringComparison.OrdinalIgnoreCase)) {
                    log.Error("File SHA1 should be {0}, is {1}", downloadedRelease.SHA1, hash);
                    targetPackage.Delete();
                    throw new Exception("Checksum doesn't match: " + targetPackage.FullName);
                }
            }
        }

        //
        // ApplyReleases methods
        //

        List<string> installPackageToAppDir(UpdateInfo updateInfo, ReleaseEntry release)
        {
            var pkg = new ZipPackage(Path.Combine(updateInfo.PackageDirectory, release.Filename));
            var target = getDirectoryForRelease(release.Version);

            // NB: This might happen if we got killed partially through applying the release
            if (target.Exists) {
                Utility.DeleteDirectory(target.FullName).Wait();
            }
            target.Create();

            // Copy all of the files out of the lib/ dirs in the NuGet package
            // into our target App directory.
            //
            // NB: We sort this list in order to guarantee that if a Net20
            // and a Net40 version of a DLL get shipped, we always end up
            // with the 4.0 version.
            log.Info("Writing files to app directory: {0}", target.FullName);

            pkg.GetLibFiles().Where(x => pathIsInFrameworkProfile(x, appFrameworkVersion))
                             .OrderBy(x => x.Path)
                             .ForEach(x => CopyFileToLocation(target, x));

            pkg.GetContentFiles().ForEach(x => CopyFileToLocation(target, x));

            var newCurrentVersion = updateInfo.FutureReleaseEntry.Version;

            // Perform post-install; clean up the previous version by asking it
            // which shortcuts to install, and nuking them. Then, run the app's
            // post install and set up shortcuts.
            return runPostInstallAndCleanup(newCurrentVersion, updateInfo.IsBootstrapping);
        }

        void CopyFileToLocation(FileSystemInfoBase target, IPackageFile x)
        {
            var targetPath = Path.Combine(target.FullName, x.EffectivePath);

            var fi = fileSystem.GetFileInfo(targetPath);
            if (fi.Exists) fi.Delete();

            var dir = fileSystem.GetDirectoryInfo(Path.GetDirectoryName(targetPath));
            if (!dir.Exists) dir.Create();

            using (var inf = x.GetStream())
            using (var of = fi.Open(FileMode.CreateNew, FileAccess.Write)) {
                inf.CopyTo(of);
            }
        }

        List<string> runPostInstallAndCleanup(Version newCurrentVersion, bool isBootstrapping)
        {
            log.Debug("AppDomain ID: {0}", AppDomain.CurrentDomain.Id);

            fixPinnedExecutables(newCurrentVersion);

            log.Info("runPostInstallAndCleanup: finished fixPinnedExecutables");

            var shortcutsToIgnore = cleanUpOldVersions(newCurrentVersion);
            var targetPath = getDirectoryForRelease(newCurrentVersion);

            return runPostInstallOnDirectory(targetPath.FullName, isBootstrapping, newCurrentVersion, shortcutsToIgnore);
        }

        List<string> runPostInstallOnDirectory(string newAppDirectoryRoot, bool isFirstInstall, Version newCurrentVersion, IEnumerable<ShortcutCreationRequest> shortcutRequestsToIgnore)
        {
            var postInstallInfo = new PostInstallInfo {
                NewAppDirectoryRoot = newAppDirectoryRoot,
                IsFirstInstall = isFirstInstall,
                NewCurrentVersion = newCurrentVersion,
                ShortcutRequestsToIgnore = shortcutRequestsToIgnore.ToArray()
            };

            var installerHooks = new InstallerHookOperations(fileSystem, applicationName);
            return AppDomainHelper.ExecuteInNewAppDomain(postInstallInfo, installerHooks.RunAppSetupInstallers).ToList();
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

        IObservable<ReleaseEntry> createFullPackagesFromDeltas(IEnumerable<ReleaseEntry> releasesToApply, ReleaseEntry currentVersion)
        {
            Contract.Requires(releasesToApply != null);

            // If there are no deltas in our list, we're already done
            if (!releasesToApply.Any() || releasesToApply.All(x => !x.IsDelta)) {
                return Observable.Return(releasesToApply.MaxBy(x => x.Version).First());
            }

            if (!releasesToApply.All(x => x.IsDelta)) {
                return Observable.Throw<ReleaseEntry>(new Exception("Cannot apply combinations of delta and full packages"));
            }

            // Smash together our base full package and the nearest delta
            var ret = Observable.Start(() => {
                var basePkg = new ReleasePackage(Path.Combine(rootAppDirectory, "packages", currentVersion.Filename));
                var deltaPkg = new ReleasePackage(Path.Combine(rootAppDirectory, "packages", releasesToApply.First().Filename));

                var deltaBuilder = new DeltaPackageBuilder();

                return deltaBuilder.ApplyDeltaPackage(basePkg, deltaPkg,
                    Regex.Replace(deltaPkg.InputPackageFile, @"-delta.nupkg$", ".nupkg", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }, RxApp.TaskpoolScheduler);

            if (releasesToApply.Count() == 1) {
                return ret.Select(x => ReleaseEntry.GenerateFromFile(x.InputPackageFile));
            }

            return ret.SelectMany(x => {
                var fi = fileSystem.GetFileInfo(x.InputPackageFile);
                var entry = ReleaseEntry.GenerateFromFile(fi.OpenRead(), fi.Name);

                // Recursively combine the rest of them
                return createFullPackagesFromDeltas(releasesToApply.Skip(1), entry);
            });
        }

        IEnumerable<ShortcutCreationRequest> cleanUpOldVersions(Version newCurrentVersion)
        {
            var directory = fileSystem.GetDirectoryInfo(rootAppDirectory);
            if (!directory.Exists) {
                log.Warn("cleanUpOldVersions: the directory '{0}' does not exist", rootAppDirectory);
                return Enumerable.Empty<ShortcutCreationRequest>();
            }
            
            return getOldReleases(newCurrentVersion)
                     .OrderBy(x => x.Name)
                     .Select(d => d.FullName)
                     .SelectMany(runAppCleanup);
        }

        IEnumerable<ShortcutCreationRequest> runAppCleanup(string path)
        {
            var installerHooks = new InstallerHookOperations(fileSystem, applicationName);

            var ret = AppDomainHelper.ExecuteInNewAppDomain(path, installerHooks.RunAppSetupCleanups);

            try {
                Utility.DeleteDirectoryAtNextReboot(path);
            }
            catch (Exception ex) {
                var message = String.Format("Couldn't delete old app directory on next reboot {0}", path);
                log.WarnException(message, ex);
            }
            return ret;
        }

        IEnumerable<ShortcutCreationRequest> runAppUninstall(string path)
        {
            var installerHooks = new InstallerHookOperations(fileSystem, applicationName);

            var ret = AppDomainHelper.ExecuteInNewAppDomain(path, installerHooks.RunAppUninstall);

            try {
                Utility.DeleteDirectoryAtNextReboot(path);
            } catch (Exception ex) {
                var message = String.Format("Couldn't delete old app directory on next reboot {0}", path);
                log.WarnException(message, ex);
            }
            return ret;
        }


        void fixPinnedExecutables(Version newCurrentVersion) 
        {
            if (Environment.OSVersion.Version < new Version(6, 1)) {
                log.Warn("fixPinnedExecutables: Found OS Version '{0}', exiting...", Environment.OSVersion.VersionString);
                return;
            }

            var newCurrentFolder = "app-" + newCurrentVersion;
            var oldAppDirectories = fileSystem.GetDirectoryInfo(rootAppDirectory).GetDirectories()
                .Where(x => x.Name.StartsWith("app-", StringComparison.InvariantCultureIgnoreCase))
                .Where(x => x.Name != newCurrentFolder)
                .Select(x => x.FullName)
                .ToArray();

            if (!oldAppDirectories.Any()) {
                log.Info("fixPinnedExecutables: oldAppDirectories is empty, this is pointless");
                return;
            }

            var newAppPath = Path.Combine(rootAppDirectory, newCurrentFolder);

            var taskbarPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");

            Func<FileInfoBase, ShellLink> resolveLink = file => {
                try {
                    return new ShellLink(file.FullName);
                } catch (Exception ex) {
                    var message = String.Format("File '{0}' could not be converted into a valid ShellLink", file.FullName);
                    log.WarnException(message, ex);
                    return null;
                }
            };

            var shellLinks = fileSystem.GetDirectoryInfo(taskbarPath)
                                       .GetFiles("*.lnk")
                                       .Select(resolveLink)
                                       .Where(x => x != null)
                                       .ToArray();

            foreach (var shortcut in shellLinks) {
                try {
                    updateLink(shortcut, oldAppDirectories, newAppPath);
                } catch (Exception ex) {
                    var message = String.Format("fixPinnedExecutables: shortcut failed: {0}", shortcut.Target);
                    log.ErrorException(message, ex);
                }
            }
        }

        void updateLink(ShellLink shortcut, string[] oldAppDirectories, string newAppPath)
        {
            log.Info("Processing shortcut '{0}'", shortcut.Target);
            foreach (var oldAppDirectory in oldAppDirectories) {
                if (!shortcut.Target.StartsWith(oldAppDirectory, StringComparison.OrdinalIgnoreCase)) {
                    log.Info("Does not match '{0}', continuing to next directory", oldAppDirectory);
                    continue;
                }

                // replace old app path with new app path and check, if executable still exists
                var newTarget = Path.Combine(newAppPath, shortcut.Target.Substring(oldAppDirectory.Length + 1));

                if (fileSystem.GetFileInfo(newTarget).Exists) {
                    shortcut.Target = newTarget;

                    // replace working directory too if appropriate
                    if (shortcut.WorkingDirectory.StartsWith(oldAppDirectory, StringComparison.OrdinalIgnoreCase)) {
                        log.Info("Changing new directory to '{0}'", newAppPath);
                        shortcut.WorkingDirectory = Path.Combine(newAppPath,
                            shortcut.WorkingDirectory.Substring(oldAppDirectory.Length + 1));
                    }

                    shortcut.Save();
                }
                else {
                    log.Info("Unpinning {0} from taskbar", shortcut.Target);
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
        IObservable<Unit> cleanDeadVersions(Version currentVersion)
        {
            if (currentVersion == null) return Observable.Return(Unit.Default);

            var di = fileSystem.GetDirectoryInfo(rootAppDirectory);
            if (!di.Exists) return Observable.Return(Unit.Default);

            log.Info("cleanDeadVersions: for version {0}", currentVersion);

            string currentVersionFolder = null;
            if (currentVersion != null) {
                currentVersionFolder = getDirectoryForRelease(currentVersion).Name;
                log.Info("cleanDeadVersions: exclude folder {0}", currentVersionFolder);
            }

            // NB: If we try to access a directory that has already been 
            // scheduled for deletion by MoveFileEx it throws what seems like
            // NT's only error code, ERROR_ACCESS_DENIED. Squelch errors that
            // come from here.
            return di.GetDirectories().ToObservable()
                .Where(x => x.Name.ToLowerInvariant().Contains("app-"))
                .Where(x => x.Name != currentVersionFolder)
                .SelectMany(x => Utility.DeleteDirectory(x.FullName, RxApp.TaskpoolScheduler))
                    .LoggedCatch<Unit, UpdateManager, UnauthorizedAccessException>(this, _ => Observable.Return(Unit.Default))
                .Aggregate(Unit.Default, (acc, x) => acc);
        }
    }
}
