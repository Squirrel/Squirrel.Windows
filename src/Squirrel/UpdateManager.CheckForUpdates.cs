using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Splat;
using System.Security.Cryptography.X509Certificates;
using Squirrel.Update;

namespace Squirrel
{
    public sealed partial class UpdateManager
    {
        internal class CheckForUpdateImpl : IEnableLogger
        {
            readonly string rootAppDirectory;

            public CheckForUpdateImpl(string rootAppDirectory)
            {
                this.rootAppDirectory = rootAppDirectory;
            }

            public async Task<UpdateInfo> CheckForUpdate(
                string localReleaseFile,
                string updateUrlOrPath,
                bool ignoreDeltaUpdates = false,
                bool verifySignature = false,
                Action<int> progress = null,
                IFileDownloader urlDownloader = null)
            {
                progress = progress ?? (_ => { });

                var localReleases = Enumerable.Empty<ReleaseEntry>();
                var stagingId = getOrCreateStagedUserId();

                bool shouldInitialize = false;
                try {
                    localReleases = Utility.LoadLocalReleases(localReleaseFile);
                } catch (Exception ex) {
                    // Something has gone pear-shaped, let's start from scratch
                    this.Log().WarnException("Failed to load local releases, starting from scratch", ex);
                    shouldInitialize = true;
                }

                if (shouldInitialize) await initializeClientAppDirectory();

                string releaseFile;
                byte[] releaseDataBytes = null;
                byte[] releaseFileSignature = null;

                var latestLocalRelease = localReleases.Count() > 0 ?
                    localReleases.MaxBy(x => x.Version).First() :
                    default(ReleaseEntry);

                // Fetch the remote RELEASES file, whether it's a local dir or an
                // HTTP URL
                if (Utility.IsHttpUrl(updateUrlOrPath)) {
                    if (updateUrlOrPath.EndsWith("/")) {
                        updateUrlOrPath = updateUrlOrPath.Substring(0, updateUrlOrPath.Length - 1);
                    }

                    this.Log().Info("Downloading RELEASES file from {0}", updateUrlOrPath);

                    int retries = 3;

                retry:

                    try {
                        var uri = Utility.AppendPathToUri(new Uri(updateUrlOrPath), "RELEASES");

                        if (latestLocalRelease != null) {
                            uri = Utility.AddQueryParamsToUri(uri, new Dictionary<string, string> {
                                { "id", latestLocalRelease.PackageName },
                                { "localVersion", latestLocalRelease.Version.ToString() },
                                { "arch", Environment.Is64BitOperatingSystem ? "amd64" : "x86" }
                            });
                        }

                        releaseDataBytes = await urlDownloader.DownloadUrl(uri.ToString());
                        releaseFile = Encoding.UTF8.GetString(releaseDataBytes);
                        if(verifySignature)
                        {
                            this.Log().Info("####Downloading signature file");
                            var signatureFileUri = String.Concat(uri.ToString(), ".cat");
                            releaseFileSignature = await urlDownloader.DownloadUrl(signatureFileUri);
                        }
                    } catch (WebException ex) {
                        this.Log().InfoException("Download resulted in WebException (returning blank release list)", ex);

                        if (retries <= 0) throw;
                        retries--;
                        goto retry;
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
                    var fiSignature = new FileInfo(Path.Combine(updateUrlOrPath, "RELEASES.cat"));
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

                    releaseDataBytes = File.ReadAllBytes(fi.FullName);
                    releaseFile = File.ReadAllText(fi.FullName, Encoding.UTF8);
                    if(verifySignature)
                    {
                        this.Log().Info("####Downloading signature file");
                        if (!fiSignature.Exists)
                        {
                            var message = String.Format(
                                "The file {0} does not exist, something is probably broken with your application",
                                fi.FullName);
                            throw new Exception(message);

                        }
                        releaseFileSignature = File.ReadAllBytes(fiSignature.FullName);
                    }
                    progress(33);
                }

                if (verifySignature && !verifyReleaseFile(releaseDataBytes, releaseFileSignature))
                {
                    throw new Exception("RELEASE file verification failed");
                }
                var ret = default(UpdateInfo);
                var remoteReleases = ReleaseEntry.ParseReleaseFileAndApplyStaging(releaseFile, stagingId);
                progress(66);

                if (!remoteReleases.Any()) {
                    throw new Exception("Remote release File is empty or corrupted");
                }

                ret = determineUpdateInfo(localReleases, remoteReleases, ignoreDeltaUpdates);

                progress(100);
                return ret;
            }

            bool verifyReleaseFile(byte[] releaseData, byte[] signatureData)
            {
                if ((releaseData == null) || (signatureData == null))
                    return false;
                var catalogFilePath = Path.Combine(Utility.GetTempDirectory(rootAppDirectory).FullName, "RELEASES.cat");
                File.WriteAllBytes(catalogFilePath, signatureData);
                if(isSignatureValid(catalogFilePath) && isChecksumValid(releaseData, catalogFilePath))
                {
                    return true;
                }
                return false;
            }

            public bool isChecksumValid(byte[] releaseBytes, string catalogFilePath)
            {
                try
                {
                    List<string> checksums = CatalogTools.ReadCatalogFile(catalogFilePath);
                    if (checksums.Count == 0)
                    {
                        throw new Exception("Catalog file doesn't have RELEASE thumbprint");
                    }
                    string releaseChecksum = checksums[0];
                    var stream = new MemoryStream(releaseBytes);
                    string calculatedChecksum = Utility.CalculateStreamSHA1(stream);
                    if (releaseChecksum != calculatedChecksum)
                    {
                        throw new Exception("RELEASES file has invalid checksum");
                    }
                }
                catch (Exception e)
                {
                    throw new Exception("Failed to verify RELEASE checksum", e);
                }
                return true;
            }

            public bool isSignatureValid(string filePath)
            {
                try
                {
                    //Check for valid signature
                    var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
                    if (!cert.Verify())
                    {
                        throw new Exception("Failed to verify catalog file signature");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Catalog file has invalid signature", ex);
                }
                return true;
            }

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
                var packageDirectory = Utility.PackageDirectoryForAppDir(rootAppDirectory);
                localReleases = localReleases ?? Enumerable.Empty<ReleaseEntry>();

                if (remoteReleases == null) {
                    this.Log().Warn("Release information couldn't be determined due to remote corrupt RELEASES file");
                    throw new Exception("Corrupt remote RELEASES file");
                }

                var latestFullRelease = Utility.FindCurrentVersion(remoteReleases);
                var currentRelease = Utility.FindCurrentVersion(localReleases);

                if (latestFullRelease == currentRelease) {
                    this.Log().Info("No updates, remote and local are the same");

                    var info = UpdateInfo.Create(currentRelease, new[] {latestFullRelease}, packageDirectory);
                    return info;
                }

                if (ignoreDeltaUpdates) {
                    remoteReleases = remoteReleases.Where(x => !x.IsDelta);
                }

                if (!localReleases.Any()) {
                    this.Log().Warn("First run or local directory is corrupt, starting from scratch");
                    return UpdateInfo.Create(null, new[] {latestFullRelease}, packageDirectory);
                }

                if (localReleases.Max(x => x.Version) > remoteReleases.Max(x => x.Version)) {
                    this.Log().Warn("hwhat, local version is greater than remote version");
                    return UpdateInfo.Create(Utility.FindCurrentVersion(localReleases), new[] {latestFullRelease}, packageDirectory);
                }

                return UpdateInfo.Create(currentRelease, remoteReleases, packageDirectory);
            }

            internal Guid? getOrCreateStagedUserId()
            {
                var stagedUserIdFile = Path.Combine(rootAppDirectory, "packages", ".betaId");
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
}
