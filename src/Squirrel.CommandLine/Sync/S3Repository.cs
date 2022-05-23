using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine.Sync
{
    internal class S3Repository : IPackageRepository
    {
        private readonly SyncS3Options _options;
        private readonly AmazonS3Client _client;
        private readonly string _prefix;

        private readonly static IFullLogger Log = SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(S3Repository));

        public S3Repository(SyncS3Options options)
        {
            _options = options;
            if (options.region != null) {
                var r = RegionEndpoint.GetBySystemName(options.region);
                _client = new AmazonS3Client(_options.keyId, _options.secret, r);
            } else if (options.endpoint != null) {
                var config = new AmazonS3Config() { ServiceURL = _options.endpoint };
                _client = new AmazonS3Client(_options.keyId, _options.secret, config);
            } else {
                throw new InvalidOperationException("Missing endpoint");
            }

            var prefix = _options.pathPrefix?.Replace('\\', '/') ?? "";
            if (!String.IsNullOrWhiteSpace(prefix) && !prefix.EndsWith("/")) prefix += "/";
            _prefix = prefix;
        }

        public async Task DownloadRecentPackages()
        {
            var releasesDir = _options.GetReleaseDirectory();
            var releasesPath = Path.Combine(releasesDir.FullName, "RELEASES");

            Log.Info($"Downloading latest release to '{releasesDir.FullName}' from S3 bucket '{_options.bucket}'"
                     + (String.IsNullOrWhiteSpace(_prefix) ? "" : " with prefix '" + _prefix + "'"));

            try {
                Log.Info("Downloading RELEASES");
                using (var obj = await _client.GetObjectAsync(_options.bucket, _prefix + "RELEASES"))
                    await obj.WriteResponseStreamToFileAsync(releasesPath, false, CancellationToken.None);
            } catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
                Log.Warn("RELEASES file not found. No releases to download.");
                return;
            }

            var releasesToDownload = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesPath))
                .Where(x => !x.IsDelta)
                .OrderByDescending(x => x.Version)
                .Take(1)
                .Select(x => new {
                    LocalPath = Path.Combine(releasesDir.FullName, x.Filename),
                    Filename = x.Filename,
                });

            foreach (var releaseToDownload in releasesToDownload) {
                Log.Info("Downloading " + releaseToDownload.Filename);
                using (var pkgobj = await _client.GetObjectAsync(_options.bucket, _prefix + releaseToDownload.Filename))
                    await pkgobj.WriteResponseStreamToFileAsync(releaseToDownload.LocalPath, false, CancellationToken.None);
            }
        }

        public async Task UploadMissingPackages()
        {
            var releasesDir = _options.GetReleaseDirectory(createIfMissing: false);
            if (!releasesDir.Exists)
                throw new Exception($"Release directory '{releasesDir.FullName}' does not exist.");

            Log.Info($"Uploading releases from '{releasesDir.FullName}' to S3 bucket '{_options.bucket}'"
                     + (String.IsNullOrWhiteSpace(_prefix) ? "" : " with prefix '" + _prefix + "'"));

            // locate files to upload
            var files = releasesDir.GetFiles("*", SearchOption.TopDirectoryOnly);
            var msiFile = files.Where(f => f.FullName.EndsWith(".msi", StringComparison.InvariantCultureIgnoreCase)).SingleOrDefault();
            var setupFile = files.Where(f => f.FullName.EndsWith("Setup.exe", StringComparison.InvariantCultureIgnoreCase))
                .ContextualSingle("release directory", "Setup.exe file");
            var releasesFile = files.Where(f => f.Name.Equals("RELEASES", StringComparison.InvariantCultureIgnoreCase))
                .ContextualSingle("release directory", "RELEASES file");
            var nupkgFiles = files.Where(f => f.FullName.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase)).ToArray();

            // we will merge the remote RELEASES file with the local one
            string remoteReleasesContent = null;
            try {
                Log.Info("Downloading remote RELEASES file");
                using (var obj = await _client.GetObjectAsync(_options.bucket, _prefix + "RELEASES"))
                using (var sr = new StreamReader(obj.ResponseStream, Encoding.UTF8, true))
                    remoteReleasesContent = await sr.ReadToEndAsync();
                Log.Info("Merging remote and local RELEASES files");
            } catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
                Log.Warn("No remote RELEASES found.");
            }

            var localReleases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesFile.FullName));
            var remoteReleases = ReleaseEntry.ParseReleaseFile(remoteReleasesContent);

            // apply retention policy. count '-full' versions only, then also remove corresponding delta packages
            var releaseEntries = localReleases
                .Concat(remoteReleases)
                .DistinctBy(r => r.Filename) // will preserve the local entries because they appear first
                .OrderBy(k => k.Version)
                .ThenBy(k => !k.IsDelta)
                .ToArray();

            if (releaseEntries.Length == 0) {
                Log.Warn("No releases found.");
                return;
            }

            if (!releaseEntries.All(f => f.PackageName == releaseEntries.First().PackageName)) {
                throw new Exception("There are mix-matched package Id's in local/remote RELEASES file. " +
                                    "Please fix the release files manually so there is only one consistent package Id present.");
            }

            var fullCount = releaseEntries.Where(r => !r.IsDelta).Count();
            if (_options.keepMaxReleases > 0 && fullCount > _options.keepMaxReleases) {
                Log.Info($"Retention Policy: {fullCount - _options.keepMaxReleases} releases will be removed from RELEASES file.");

                var fullReleases = releaseEntries
                    .OrderByDescending(k => k.Version)
                    .Where(k => !k.IsDelta)
                    .Take(_options.keepMaxReleases)
                    .ToArray();

                var deltaReleases = releaseEntries
                    .Where(k => k.IsDelta)
                    .Where(k => fullReleases.Any(f => f.Version == k.Version))
                    .Where(k => k.Version != fullReleases.Last().Version) // ignore delta packages for the oldest full package
                    .ToArray();

                Log.Info($"Total number of packages in remote after retention: {fullReleases.Length} full, {deltaReleases.Length} delta.");
                fullCount = fullReleases.Length;
                ReleaseEntry.WriteReleaseFile(fullReleases.Concat(deltaReleases), releasesFile.FullName);
            } else {
                Log.Info($"There are currently {fullCount} full releases in RELEASES file.");
                ReleaseEntry.WriteReleaseFile(releaseEntries, releasesFile.FullName);
            }

            // we need to upload things in a certain order. If we upload 'RELEASES' first, for example, a client
            // might try to request a nupkg that does not yet exist.

            // upload nupkg's first
            foreach (var f in nupkgFiles) {
                if (!releaseEntries.Any(r => r.Filename.Equals(f.Name, StringComparison.InvariantCultureIgnoreCase))) {
                    Log.Warn($"Upload file '{f.Name}' skipped (not in RELEASES file)");
                    continue;
                }

                await UploadFile(f, _options.overwrite);
            }

            // next upload setup files
            await UploadFile(setupFile, true);
            if (msiFile != null) await UploadFile(msiFile, true);

            // upload RELEASES
            await UploadFile(releasesFile, true);

            // ignore dead package cleanup if there is no retention policy
            if (_options.keepMaxReleases > 0) {
                // remove any dead packages (not in RELEASES) as they are undiscoverable anyway
                Log.Info("Searching for remote dead packages (not in RELEASES file)");

                var objects = await ListBucketContentsAsync(_client, _options.bucket, _prefix).ToArrayAsync();

                var deadObjectQuery =
                    from o in objects
                    let key = o.Key
                    where key.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase)
                    where key.StartsWith(_prefix, StringComparison.InvariantCultureIgnoreCase)
                    let fileName = key.Substring(_prefix.Length)
                    where !fileName.Contains('/') // filters out objects in folders if _prefix is empty
                    where !releaseEntries.Any(r => r.Filename.Equals(fileName, StringComparison.InvariantCultureIgnoreCase))
                    orderby o.LastModified ascending
                    select new { key, fileName, versionId = o.VersionId };

                var deadObj = deadObjectQuery.ToArray();

                Log.Info($"Found {deadObj.Length} dead packages.");
                foreach (var s3obj in deadObj) {
                    var req = new DeleteObjectRequest { BucketName = _options.bucket, Key = s3obj.key, VersionId = s3obj.versionId };
                    await RetryAsync(() => _client.DeleteObjectAsync(req), "Deleting dead package: " + s3obj, throwIfFail: false);
                }
            }

            Log.Info("Done");

            var endpointHost = _options.endpoint ?? RegionEndpoint.GetBySystemName(_options.region).GetEndpointForService("s3").Hostname;

            if (Regex.IsMatch(endpointHost, @"^https?:\/\/", RegexOptions.IgnoreCase)) {
                endpointHost = new Uri(endpointHost, UriKind.Absolute).Host;
            }

            var baseurl = $"https://{_options.bucket}.{endpointHost}/{_prefix}";
            Log.Info($"Bucket URL:  {baseurl}");
            Log.Info($"Setup URL:   {baseurl}{setupFile.Name}");
        }

        private static async IAsyncEnumerable<S3ObjectVersion> ListBucketContentsAsync(IAmazonS3 client, string bucketName, string prefix)
        {
            var request = new ListVersionsRequest {
                BucketName = bucketName,
                MaxKeys = 100,
                Prefix = prefix,
            };

            ListVersionsResponse response;
            do {
                response = await client.ListVersionsAsync(request);
                foreach (var obj in response.Versions) {
                    yield return obj;
                }

                // If the response is truncated, set the request ContinuationToken
                // from the NextContinuationToken property of the response.
                request.KeyMarker = response.NextKeyMarker;
                request.VersionIdMarker = response.NextVersionIdMarker;
            } while (response.IsTruncated);
        }

        private async Task UploadFile(FileInfo f, bool overwriteRemote)
        {
            string key = _prefix + f.Name;
            string deleteOldVersionId = null;

            // try to detect an existing remote file of the same name
            try {
                var metadata = await _client.GetObjectMetadataAsync(_options.bucket, key);
                var md5 = GetFileMD5Checksum(f.FullName);
                var stored = metadata?.ETag?.Trim().Trim('"');

                if (stored != null) {
                    if (stored.Equals(md5, StringComparison.InvariantCultureIgnoreCase)) {
                        Log.Info($"Upload file '{f.Name}' skipped (already exists in remote)");
                        return;
                    } else if (overwriteRemote) {
                        Log.Info($"File '{f.Name}' exists in remote, replacing...");
                        deleteOldVersionId = metadata.VersionId;
                    } else {
                        Log.Warn($"File '{f.Name}' exists in remote and checksum does not match local file. Use 'overwrite' argument to replace remote file.");
                        return;
                    }
                }
            } catch {
                // don't care if this check fails. worst case, we end up re-uploading a file that
                // already exists. storage providers should prefer the newer file of the same name.
            }

            var req = new PutObjectRequest {
                BucketName = _options.bucket,
                FilePath = f.FullName,
                Key = key,
            };

            await RetryAsync(() => _client.PutObjectAsync(req), "Uploading " + f.Name);

            if (deleteOldVersionId != null) {
                await RetryAsync(() => _client.DeleteObjectAsync(_options.bucket, key, deleteOldVersionId),
                    "Removing old version of " + f.Name,
                    throwIfFail: false);
            }
        }

        private async Task RetryAsync(Func<Task> block, string message, bool throwIfFail = true, bool showMessageFirst = true)
        {
            int ctry = 0;
            while (true) {
                try {
                    if (showMessageFirst || ctry > 0)
                        Log.Info((ctry > 0 ? $"(retry {ctry}) " : "") + message);
                    await block().ConfigureAwait(false);
                    return;
                } catch (Exception ex) {
                    if (ctry++ > 2) {
                        if (throwIfFail) {
                            throw;
                        } else {
                            Log.Error("Error: " + ex.Message + ", will not try again.");
                            return;
                        }
                    }

                    Log.Error($"Error: {ex.Message}, retrying in 1 second.");
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
        }

        private static string GetFileMD5Checksum(string filePath)
        {
            var sha = System.Security.Cryptography.MD5.Create();
            byte[] checksum;
            using (var fs = File.OpenRead(filePath))
                checksum = sha.ComputeHash(fs);
            return BitConverter.ToString(checksum).Replace("-", String.Empty);
        }
    }
}