﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            public async Task DownloadReleases(string updateUrlOrPath, IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null, IFileDownloader urlDownloader = null)
            {
                progress = progress ?? (_ => { });
                urlDownloader = urlDownloader ?? new FileDownloader();
                var packagesDirectory = Path.Combine(rootAppDirectory, "packages");

                double current = 0;
                double toIncrement = 100.0 / releasesToDownload.Count();

                if (Utility.IsHttpUrl(updateUrlOrPath)) {
                    // From Internet
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
                        lock (progress)
                        {
                            current -= component;
                            component = toIncrement;
                            progress((int)Math.Round(current += component));
                        }
                    });
                } else {
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
