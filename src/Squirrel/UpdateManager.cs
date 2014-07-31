using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NuGet;
using Splat;

namespace Squirrel
{
    public sealed class UpdateManager : IUpdateManager, IEnableLogger
    {
        readonly string rootAppDirectory;
        readonly string applicationName;
        readonly IFileDownloader urlDownloader;
        readonly string updateUrlOrPath;
        readonly FrameworkVersion appFrameworkVersion;

        IDisposable updateLock;

        public UpdateManager(string urlOrPath, 
            string applicationName,
            FrameworkVersion appFrameworkVersion,
            string rootDirectory = null,
            IFileDownloader urlDownloader = null)
        {
            Contract.Requires(!String.IsNullOrEmpty(urlOrPath));
            Contract.Requires(!String.IsNullOrEmpty(applicationName));

            updateUrlOrPath = urlOrPath;
            this.applicationName = applicationName;
            this.appFrameworkVersion = appFrameworkVersion;

            this.rootAppDirectory = Path.Combine(rootDirectory ?? getLocalAppDataDirectory(), applicationName);

            this.urlDownloader = urlDownloader ?? new FileDownloader();
        }

        public string PackageDirectory {
            get { return Path.Combine(rootAppDirectory, "packages"); }
        }

        public string LocalReleaseFile {
            get { return Path.Combine(PackageDirectory, "RELEASES"); }
        }

        public async Task<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates = false, Action<int> progress = null)
        {
            await acquireUpdateLock();

            var localReleases = Enumerable.Empty<ReleaseEntry>();

            bool shouldInitialize = false;
            try {
                var file = File.OpenRead(LocalReleaseFile);

                // NB: sr disposes file
                using (var sr = new StreamReader(file, Encoding.UTF8)) {
                    localReleases = ReleaseEntry.ParseReleaseFile(sr.ReadToEnd());
                }
            } catch (Exception ex) {
                // Something has gone wrong, we'll start from scratch.
                this.Log().WarnException("Failed to load local release list", ex);
                shouldInitialize = true;
            }

            if (shouldInitialize) await initializeClientAppDirectory();

            string releaseFile;

            // Fetch the remote RELEASES file, whether it's a local dir or an 
            // HTTP URL
            if (isHttpUrl(updateUrlOrPath)) {
                this.Log().Info("Downloading RELEASES file from {0}", updateUrlOrPath);

                try {
                    var data = await urlDownloader.DownloadUrl(String.Format("{0}/{1}", updateUrlOrPath, "RELEASES"));
                    releaseFile = Encoding.UTF8.GetString(data);
                } catch (WebException ex) {
                    this.Log().InfoException("Download resulted in WebException (returning blank release list)", ex);
                    releaseFile = String.Empty;
                }

                progress(33);
            } else {
                this.Log().Info("Reading RELEASES file from {0}", updateUrlOrPath);

                if (!Directory.Exists(updateUrlOrPath)) {
                    var message = String.Format(
                        "The directory {0} does not exist, something is probably broken with your application", 
                        updateUrlOrPath);

                    throw new Exception(message);
                }

                var fi = new FileInfo(Path.Combine(updateUrlOrPath, "RELEASES"));
                if (!fi.Exists) {
                    var message = String.Format(
                        "The file {0} does not exist, something is probably broken with your application", 
                        fi.FullName);

                    this.Log().Warn(message);

                    var packages = (new DirectoryInfo(updateUrlOrPath)).GetFiles("*.nupkg");
                    if (packages.Length == 0) {
                        throw new Exception(message);
                    }

                    // NB: Create a new RELEASES file since we've got a directory of packages
                    ReleaseEntry.WriteReleaseFile(
                        packages.Select(x => ReleaseEntry.GenerateFromFile(x.FullName)), fi.FullName);
                }

                releaseFile = File.ReadAllText(fi.FullName, Encoding.UTF8);
                progress(33);
            }

            var ret = default(UpdateInfo);
            var remoteReleases = ReleaseEntry.ParseReleaseFile(releaseFile);
            progress(66);

            if (!remoteReleases.IsEmpty()) {
                ret = determineUpdateInfo(localReleases, remoteReleases, ignoreDeltaUpdates);
            }

            progress(100);
            return ret;
        }

        public async Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null)
        {
            progress = progress ?? (_ => { });
            int current = 0;
            int toIncrement = (int)(100.0 / releasesToDownload.Count());

            await acquireUpdateLock();

            if (isHttpUrl(updateUrlOrPath)) {
                await releasesToDownload.ForEachAsync(async x => {
                    await urlDownloader.DownloadFile(
                        String.Format("{0}/{1}", updateUrlOrPath, x.Filename),
                        Path.Combine(rootAppDirectory, "packages", x.Filename));
                    lock (progress) progress(current += toIncrement);
                });
            } else {
                await releasesToDownload.ForEachAsync(x => {
                    File.Copy(
                        Path.Combine(updateUrlOrPath, x.Filename),
                        Path.Combine(rootAppDirectory, "packages", x.Filename));
                    lock (progress) progress(current += toIncrement);
                });
            }
        }

        public async Task ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null)
        {
            progress = progress ?? (_ => { });

            await acquireUpdateLock();

            await cleanDeadVersions(updateInfo.CurrentlyInstalledVersion != null ? updateInfo.CurrentlyInstalledVersion.Version : null);
            progress(10);

            var release = await createFullPackagesFromDeltas(updateInfo.ReleasesToApply, updateInfo.CurrentlyInstalledVersion);
            progress(50);

            await installPackageToAppDir(updateInfo, release);
            progress(95);

            await UpdateLocalReleasesFile();
            progress(100);
        }

        public async Task UpdateLocalReleasesFile()
        {
            await acquireUpdateLock();
            await Task.Run(() => ReleaseEntry.BuildReleasesFile(PackageDirectory));
        }

        public async Task FullUninstall(Version version = null)
        {
            version = version ?? new Version(255, 255, 255, 255);
            this.Log().Info("Uninstalling version '{0}'", version);

            await acquireUpdateLock();
            await fullUninstall(version);
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

        async Task fullUninstall(Version version)
        {
            // find all the old releases (and this one)
            var directoriesToDelete = getOldReleases(version)
                .Concat(new [] { getDirectoryForRelease(version) })
                .Where(d => d.Exists)
                .Select(d => d.FullName);

            await directoriesToDelete.ForEachAsync(x => deleteDirectoryWithFallbackToNextReboot(x));

            if (!getReleases().Any()) {
                await deleteDirectoryWithFallbackToNextReboot(rootAppDirectory);
            }
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

        Task<IDisposable> acquireUpdateLock()
        {
            if (updateLock != null) return Task.FromResult(updateLock);

            return Task.Run(() => {
                // TODO: We'll bring this back later
                var key = Utility.CalculateStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(rootAppDirectory)));
                var theLock = Disposable.Create(() => { });

                /*
                IDisposable theLock;
                try {
                    theLock = RxApp.InUnitTestRunner() ?
                        Disposable.Empty : new SingleGlobalInstance(key, 2000);
                } catch (TimeoutException) {
                    throw new TimeoutException("Couldn't acquire update lock, another instance may be running updates");
                }
                */

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

        DirectoryInfo getDirectoryForRelease(Version releaseVersion)
        {
            return new DirectoryInfo(Path.Combine(rootAppDirectory, "app-" + releaseVersion));
        }

        async Task deleteDirectoryWithFallbackToNextReboot(string dir)
        {
            try {
                await Utility.DeleteDirectory(dir);
            } catch (UnauthorizedAccessException ex) {
                var message = String.Format("Uninstall failed to delete dir '{0}', punting to next reboot", dir);
                this.Log().WarnException(message, ex);

                Utility.DeleteDirectoryAtNextReboot(dir);
            }
        }


        //
        // CheckForUpdate methods
        //

        async Task initializeClientAppDirectory()
        {
            // On bootstrap, we won't have any of our directories, create them
            var pkgDir = Path.Combine(rootAppDirectory, "packages");
            if (Directory.Exists(pkgDir)) {
                await Utility.DeleteDirectory(pkgDir);
            }

            Directory.CreateDirectory(pkgDir);
        }

        UpdateInfo determineUpdateInfo(IEnumerable<ReleaseEntry> localReleases, IEnumerable<ReleaseEntry> remoteReleases, bool ignoreDeltaUpdates)
        {
            localReleases = localReleases ?? Enumerable.Empty<ReleaseEntry>();

            if (remoteReleases == null) {
                this.Log().Warn("Release information couldn't be determined due to remote corrupt RELEASES file");
                throw new Exception("Corrupt remote RELEASES file");
            }

            if (localReleases.Count() == remoteReleases.Count()) {
                this.Log().Info("No updates, remote and local are the same");

                var latestFullRelease = findCurrentVersion(remoteReleases);
                var currentRelease = findCurrentVersion(localReleases);

                var info = UpdateInfo.Create(currentRelease, new[] {latestFullRelease}, PackageDirectory,appFrameworkVersion);
                return info;
            }

            if (ignoreDeltaUpdates) {
                remoteReleases = remoteReleases.Where(x => !x.IsDelta);
            }

            if (!localReleases.Any()) {
                this.Log().Warn("First run or local directory is corrupt, starting from scratch");

                var latestFullRelease = findCurrentVersion(remoteReleases);
                return UpdateInfo.Create(findCurrentVersion(localReleases), new[] {latestFullRelease}, PackageDirectory, appFrameworkVersion);
            }

            if (localReleases.Max(x => x.Version) > remoteReleases.Max(x => x.Version)) {
                this.Log().Warn("hwhat, local version is greater than remote version");

                var latestFullRelease = findCurrentVersion(remoteReleases);
                return UpdateInfo.Create(findCurrentVersion(localReleases), new[] {latestFullRelease}, PackageDirectory, appFrameworkVersion);
            }

            return UpdateInfo.Create(findCurrentVersion(localReleases), remoteReleases, PackageDirectory, appFrameworkVersion);
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

        Task checksumAllPackages(IEnumerable<ReleaseEntry> releasesDownloaded)
        {
            return releasesDownloaded.ForEachAsync(x => checksumPackage(x));
        }

        void checksumPackage(ReleaseEntry downloadedRelease)
        {
            var targetPackage = new FileInfo(
                Path.Combine(rootAppDirectory, "packages", downloadedRelease.Filename));

            if (!targetPackage.Exists) {
                this.Log().Error("File {0} should exist but doesn't", targetPackage.FullName);

                throw new Exception("Checksummed file doesn't exist: " + targetPackage.FullName);
            }

            if (targetPackage.Length != downloadedRelease.Filesize) {
                this.Log().Error("File Length should be {0}, is {1}", downloadedRelease.Filesize, targetPackage.Length);
                targetPackage.Delete();

                throw new Exception("Checksummed file size doesn't match: " + targetPackage.FullName);
            }

            using (var file = targetPackage.OpenRead()) {
                var hash = Utility.CalculateStreamSHA1(file);

                if (!hash.Equals(downloadedRelease.SHA1,StringComparison.OrdinalIgnoreCase)) {
                    this.Log().Error("File SHA1 should be {0}, is {1}", downloadedRelease.SHA1, hash);
                    targetPackage.Delete();
                    throw new Exception("Checksum doesn't match: " + targetPackage.FullName);
                }
            }
        }

        //
        // ApplyReleases methods
        //

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

            await pkg.GetLibFiles().Where(x => pathIsInFrameworkProfile(x, appFrameworkVersion))
                .OrderBy(x => x.Path)
                .ForEachAsync(x => CopyFileToLocation(target, x));

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
    }
}
