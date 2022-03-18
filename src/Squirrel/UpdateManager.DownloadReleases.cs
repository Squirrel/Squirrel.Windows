using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    public partial class UpdateManager
    {
        /// <inheritdoc />
        public virtual async Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null)
        {
            // lock will be held until this class is disposed
            await acquireUpdateLock().ConfigureAwait(false);

            if (_updateSource == null)
                throw new InvalidOperationException("Cannot download updates if no update source / url was provided in the construction of UpdateManager.");

            progress = progress ?? (_ => { });
            var packagesDirectory = PackagesDirectory;

            double current = 0;
            double toIncrement = 100.0 / releasesToDownload.Count();

            await releasesToDownload.ForEachAsync(async x => {
                var targetFile = Path.Combine(packagesDirectory, x.Filename);
                double component = 0;
                await _updateSource.DownloadReleaseEntry(x, targetFile, p => {
                    lock (progress) {
                        current -= component;
                        component = toIncrement / 100.0 * p;
                        progress((int) Math.Round(current += component));
                    }
                }).ConfigureAwait(false);

                checksumPackage(x);
            }).ConfigureAwait(false);
        }

        void checksumPackage(ReleaseEntry downloadedRelease)
        {
            var targetPackage = new FileInfo(Path.Combine(PackagesDirectory, downloadedRelease.Filename));

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

                if (!hash.Equals(downloadedRelease.SHA1, StringComparison.OrdinalIgnoreCase)) {
                    this.Log().Error("File SHA1 should be {0}, is {1}", downloadedRelease.SHA1, hash);
                    targetPackage.Delete();
                    throw new Exception("Checksum doesn't match: " + targetPackage.FullName);
                }
            }
        }
    }
}
