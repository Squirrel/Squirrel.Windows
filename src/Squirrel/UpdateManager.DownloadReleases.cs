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
        class DownloadReleases : IEnableLogger
        {
			public async Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null)
			{
				progress = progress ?? (_ => { });
				int current = 0;
				int toIncrement = (int)(100.0 / releasesToDownload.Count());

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
        }
    }
}
