using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    public partial class UpdateManager
    {
        /// <inheritdoc />
        public virtual async Task<UpdateInfo> CheckForUpdate(
            bool ignoreDeltaUpdates = false,
            Action<int> progress = null,
            UpdaterIntention intention = UpdaterIntention.Update)
        {
            // lock will be held until this class is disposed
            await acquireUpdateLock().ConfigureAwait(false);

            if (_source == null)
                throw new InvalidOperationException("Cannot check for updates if no update source / url was provided in the construction of UpdateManager.");

            progress ??= (_ => { });
            ReleaseEntry[] localReleases = new ReleaseEntry[0];
            bool shouldInitialize = intention == UpdaterIntention.Install;

            if (intention != UpdaterIntention.Install) {
                try {
                    localReleases = Utility.LoadLocalReleases(_config.ReleasesFilePath).ToArray();
                } catch (Exception ex) {
                    // Something has gone pear-shaped, let's start from scratch
                    this.Log().WarnException("Failed to load local releases, starting from scratch", ex);
                    shouldInitialize = true;
                }
            }

            if (shouldInitialize) initializeClientAppDirectory();
            
            var stagingId = intention == UpdaterIntention.Install ? null : getOrCreateStagedUserId();

            var latestLocalRelease = localReleases.Count() > 0
                ? localReleases.MaxBy(v => v.Version).First() 
                : null;

            progress(33);

            var remoteReleases = await Utility.RetryAsync(() => _source.GetReleaseFeed(stagingId, latestLocalRelease)).ConfigureAwait(false);

            progress(66);

            var updateInfo = determineUpdateInfo(intention, localReleases, remoteReleases, ignoreDeltaUpdates);

            progress(100);
            return updateInfo;
        }

        void initializeClientAppDirectory()
        {
            // On bootstrap, we won't have any of our directories, create them
            var pkgDir = _config.PackagesDir;
            if (Directory.Exists(pkgDir)) {
                Utility.DeleteFileOrDirectoryHard(pkgDir, throwOnFailure: false);
            }
            Directory.CreateDirectory(pkgDir);
        }

        UpdateInfo determineUpdateInfo(UpdaterIntention intention, IEnumerable<ReleaseEntry> localReleases, IEnumerable<ReleaseEntry> remoteReleases, bool ignoreDeltaUpdates)
        {
            var packageDirectory = _config.PackagesDir;
            localReleases = localReleases ?? Enumerable.Empty<ReleaseEntry>();

            if (remoteReleases == null) {
                this.Log().Warn("Release information couldn't be determined due to remote corrupt RELEASES file");
                throw new Exception("Corrupt remote RELEASES file");
            }

            if (!remoteReleases.Any()) {
                throw new Exception("Remote release File is empty or corrupted");
            }

            var latestFullRelease = Utility.FindCurrentVersion(remoteReleases);
            var currentRelease = Utility.FindCurrentVersion(localReleases);

            if (latestFullRelease == currentRelease) {
                this.Log().Info("No updates, remote and local are the same");

                var info = UpdateInfo.Create(currentRelease, new[] { latestFullRelease }, packageDirectory);
                return info;
            }

            if (ignoreDeltaUpdates) {
                remoteReleases = remoteReleases.Where(x => !x.IsDelta);
            }

            if (!localReleases.Any()) {
                if (intention == UpdaterIntention.Install) {
                    this.Log().Info("First run, starting from scratch");
                } else {
                    this.Log().Warn("No local releases found, starting from scratch");
                }

                return UpdateInfo.Create(null, new[] { latestFullRelease }, packageDirectory);
            }

            if (localReleases.Max(x => x.Version) > remoteReleases.Max(x => x.Version)) {
                this.Log().Warn("hwhat, local version is greater than remote version");
                return UpdateInfo.Create(Utility.FindCurrentVersion(localReleases), new[] { latestFullRelease }, packageDirectory);
            }

            return UpdateInfo.Create(currentRelease, remoteReleases, packageDirectory);
        }

        internal Guid? getOrCreateStagedUserId()
        {
            var stagedUserIdFile = _config.BetaIdFilePath;
            var ret = default(Guid);

            try {
                if (!Guid.TryParse(File.ReadAllText(stagedUserIdFile, Encoding.UTF8), out ret)) {
                    throw new Exception("File was read but contents were invalid");
                }

                this.Log().Info("Using existing staging user ID: {0}", ret.ToString());
                return ret;
            } catch (Exception ex) {
                this.Log().DebugException("Couldn't read staging user ID, creating a blank one", ex);
            }

            var prng = new Random();
            var buf = new byte[4096];
            prng.NextBytes(buf);

            ret = Utility.CreateGuidFromHash(buf);
            try {
                File.WriteAllText(stagedUserIdFile, ret.ToString(), Encoding.UTF8);
                this.Log().Info("Generated new staging user ID: {0}", ret.ToString());
                return ret;
            } catch (Exception ex) {
                this.Log().WarnException("Couldn't write out staging user ID, this user probably shouldn't get beta anything", ex);
                return null;
            }
        }
    }
}
