using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Splat;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Squirrel
{
    public sealed partial class UpdateManager
    {
        internal class DownloadReleasesImpl : IEnableLogger
        {
            readonly string rootAppDirectory;
            readonly X509Certificate originalCertificate;

            public DownloadReleasesImpl(string rootAppDirectory)
            {
                this.rootAppDirectory = rootAppDirectory;
                this.originalCertificate = GetCurrentExeCertificate();
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

            public async Task VerifyReleases(IEnumerable<ReleaseEntry> releasesToDownload, bool verifySignature)
            {
                await checksumAllPackages(releasesToDownload);
                if(verifySignature)
                    await verifySignatureAllPackages(releasesToDownload);
            }

            Task downloadRelease(string updateBaseUrl, ReleaseEntry releaseEntry, IFileDownloader urlDownloader, string targetFile, Action<int> progress)
            {
                var baseUri = Utility.EnsureTrailingSlash(new Uri(updateBaseUrl));

                var releaseEntryUrl = releaseEntry.BaseUrl + releaseEntry.Filename;
                if (!String.IsNullOrEmpty(releaseEntry.Query)) {
                    releaseEntryUrl += releaseEntry.Query;
                }
                var sourceFileUrl = new Uri(baseUri, releaseEntryUrl).AbsoluteUri;
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

            Task verifySignatureAllPackages(IEnumerable<ReleaseEntry> releasesDownloaded)
            {
                return releasesDownloaded.ForEachAsync(x => verifySignature(x));
            }

            void verifySignature(ReleaseEntry downloadedRelease)
            {
                var filePath = Path.Combine(rootAppDirectory, "packages", downloadedRelease.Filename);
                var targetPackage = new FileInfo(filePath);
                
                if(originalCertificate == null)
                {
                    this.Log().Info("Current exe doesn't have signature, skip verification for update package");
                    return;
                }
                if (!targetPackage.Exists)
                {
                    this.Log().Error("File {0} should exist but doesn't", targetPackage.FullName);
                    throw new Exception("Verifysignature file doesn't exist: " + targetPackage.FullName);
                }
                    
                try
                {
                    var updatePackageCertificate = X509Certificate.CreateFromSignedFile(filePath);
                    if (updatePackageCertificate.Subject != originalCertificate.Subject)
                    {
                        targetPackage.Delete();
                        throw new Exception(String.Format("Certificate subjects do not match, current:{0}, update: {1} ",
                            originalCertificate.Subject, updatePackageCertificate.Subject));
                    }
                }
                catch (Exception e)
                {
                    targetPackage.Delete();
                    throw e;
                }
            }

            X509Certificate GetCurrentExeCertificate()
            {
                var filePath = Assembly.GetExecutingAssembly().Location;
                try
                {
                    return X509Certificate.CreateFromSignedFile(filePath);
                }
                catch (Exception e)
                {
                    this.Log().Error("Current exe doesn't have signature, will not enforce verification for update", e);
                    return null;
                }
            }
        }
    }
}
