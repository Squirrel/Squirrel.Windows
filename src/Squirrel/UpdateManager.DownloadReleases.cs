using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
    public sealed partial class UpdateManager
    {
        internal class DownloadReleasesImpl : IEnableLogger
        {
            readonly string rootAppDirectory;

            public DownloadReleasesImpl(string rootAppDirectory)
            {
                this.rootAppDirectory = rootAppDirectory;
            }

            public async Task DownloadReleases(string updateUrlOrPath, IEnumerable<ReleaseEntry> releasesToDownload1, Action<int> progress = null, IFileDownloader urlDownloader = null)
            {
                progress = progress ?? (_ => { });
                urlDownloader = urlDownloader ?? new FileDownloader();
                var packagesDirectory = Path.Combine(rootAppDirectory, "packages");
                var releasesToDownload = new List<ReleaseEntry>(releasesToDownload1);

                double current = 0;
                double toIncrement = 100.0 / releasesToDownload.Count();

                if (Utility.IsHttpUrl(updateUrlOrPath)) {
                    // From Internet
                    Exception lastException = null;
                    for (int attempts = 0; ; attempts++) {
                        // Filter out ones we downloaded successfully (perhaps on an earlier run of the program).
                        // We don't want to waste time and bandwidth downloading anything we already have.
                        releasesToDownload = releasesToDownload.Where(x => !isPackageOk(x)).ToList();
                        if (releasesToDownload.Count == 0 || attempts >= 4) // we got them all (or exceeded max attempt limit)
                            break;
                        try
                        {
                            await releasesToDownload.ForEachAsync(async x => {
                                var targetFile = Path.Combine(packagesDirectory, x.Filename);
                                double component = 0;
                                await downloadRelease(updateUrlOrPath, x, urlDownloader, targetFile, p => {
                                    lock (progress) {
                                        current -= component;
                                        component = toIncrement / 100.0 * p;
                                        progress((int)Math.Round(current += component));
                                    }
                                });
                                // With a lot of small updates, and since notifications are only sent every half second
                                // for each one, we can easily miss half or more of the progress on each download.
                                // To make sure we eventually get to 100% of the whole process, we need to update
                                // progress (and especially the total in current) to indicate that this one is complete.
                                lock (progress) {
                                    current -= component;
                                    component = toIncrement;
                                    progress((int)Math.Round(current += component));
                                }
                            });
                        }
                        catch (WebException ex)
                        {
                            lastException = ex;
                        }
                    }
                    // If we failed to get all the files, throw the last exception we got; it may provide some clue what went wrong.
                    if (releasesToDownload.Count > 0)
                        throw lastException ?? new ApplicationException("Download somehow failed to get a full set of valid deltas though no exception was thrown");
                }
                else
                {
                    // From Disk
                    await releasesToDownload.ForEachAsync(x => {
                        var targetFile = Path.Combine(packagesDirectory, x.Filename);

                        File.Copy(
                            Path.Combine(updateUrlOrPath, x.Filename),
                            targetFile,
                            true);

                        lock (progress) progress((int)Math.Round(current += toIncrement));
                    });
                }
            }

            bool isReleaseExplicitlyHttp(ReleaseEntry x)
            {
                return x.BaseUrl != null && 
                    Uri.IsWellFormedUriString(x.BaseUrl, UriKind.Absolute);
            }

            Task downloadRelease(string updateBaseUrl, ReleaseEntry releaseEntry, IFileDownloader urlDownloader, string targetFile, Action<int> progress)
            {
                if (!updateBaseUrl.EndsWith("/")) {
                    updateBaseUrl += '/';
                }

                var sourceFileUrl = new Uri(new Uri(updateBaseUrl), releaseEntry.BaseUrl + releaseEntry.Filename).AbsoluteUri;
                File.Delete(targetFile);

                return urlDownloader.DownloadFile(sourceFileUrl, targetFile, progress);
            }

            Task checksumAllPackages(IEnumerable<ReleaseEntry> releasesDownloaded)
            {
                return releasesDownloaded.ForEachAsync(x => checksumPackage(x));
            }

            bool isPackageOk(ReleaseEntry downloadedRelease)
            {
                var targetPackage = new FileInfo(
                    Path.Combine(rootAppDirectory, "packages", downloadedRelease.Filename));

                if (!targetPackage.Exists)
                {
                    return false;
                }

                if (targetPackage.Length != downloadedRelease.Filesize)
                {
                    return false;
                }

                using (var file = targetPackage.OpenRead())
                {
                    var hash = Utility.CalculateStreamSHA1(file);
                    return hash.Equals(downloadedRelease.SHA1, StringComparison.OrdinalIgnoreCase);
                }
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
