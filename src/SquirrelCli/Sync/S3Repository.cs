using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Squirrel;
using Squirrel.SimpleSplat;

namespace SquirrelCli.Sources
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
            var releasesDir = new DirectoryInfo(_options.releaseDir);
            if (!releasesDir.Exists)
                releasesDir.Create();

            var releasesPath = Path.Combine(releasesDir.FullName, "RELEASES");

            Log.Info($"Downloading latest release to '{_options.releaseDir}' from S3 bucket '{_options.bucket}'"
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
            Log.Info($"Uploading releases from '{_options.releaseDir}' to S3 bucket '{_options.bucket}'"
                + (String.IsNullOrWhiteSpace(_prefix) ? "" : " with prefix '" + _prefix + "'"));

            var releasesDir = new DirectoryInfo(_options.releaseDir);

            // locate files to upload
            var files = releasesDir.GetFiles("*", SearchOption.TopDirectoryOnly);
            var msiFile = files.Where(f => f.FullName.EndsWith(".msi", StringComparison.InvariantCultureIgnoreCase)).SingleOrDefault();
            var setupFile = files.Where(f => f.FullName.EndsWith("Setup.exe", StringComparison.InvariantCultureIgnoreCase))
                .ContextualSingle("release directory", "Setup.exe file");
            var releasesFile = files.Where(f => f.Name.Equals("RELEASES", StringComparison.InvariantCultureIgnoreCase))
                .ContextualSingle("release directory", "RELEASES file");
            var nupkgFiles = files.Where(f => f.FullName.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase)).ToArray();

            // apply retention policy. count '-full' versions only, then also remove corresponding delta packages
            var releaseEntries = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesFile.FullName))
                .OrderBy(k => k.Version)
                .ThenBy(k => !k.IsDelta)
                .ToArray();

            var fullCount = releaseEntries.Where(r => !r.IsDelta).Count();
            if (_options.keepMaxReleases > 0 && fullCount > _options.keepMaxReleases) {
                Log.Info($"Retention Policy: {fullCount - _options.keepMaxReleases} releases will be removed from RELEASES file.");

                var fullReleases = releaseEntries
                    .OrderByDescending(k => k.Version)
                    .Where(k => !k.IsDelta)
                    .Take(_options.keepMaxReleases)
                    .ToArray();

                var deltaReleases = releaseEntries
                    .OrderByDescending(k => k.Version)
                    .Where(k => k.IsDelta)
                    .Where(k => fullReleases.Any(f => f.Version == k.Version))
                    .Where(k => k.Version != fullReleases.Last().Version) // ignore delta packages for the oldest full package
                    .ToArray();

                Log.Info($"Total number of packages in remote after retention: {fullReleases.Length} full, {deltaReleases.Length} delta.");
                fullCount = fullReleases.Length;

                releaseEntries = fullReleases
                    .Concat(deltaReleases)
                    .OrderBy(k => k.Version)
                    .ThenBy(k => !k.IsDelta)
                    .ToArray();
                ReleaseEntry.WriteReleaseFile(releaseEntries, releasesFile.FullName);
            } else {
                Log.Info($"There are currently {fullCount} full releases in RELEASES file.");
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

                var objects = await ListBucketContentsAsync(_client, _options.bucket).ToArrayAsync();
                var deadObjectKeys = objects
                    .Select(o => o.Key)
                    .Where(o => o.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase))
                    .Where(o => o.StartsWith(_prefix, StringComparison.InvariantCultureIgnoreCase))
                    .Select(o => o.Substring(_prefix.Length))
                    .Where(o => !o.Contains('/')) // filters out objects in folders if _prefix is empty
                    .Where(o => !releaseEntries.Any(r => r.Filename.Equals(o, StringComparison.InvariantCultureIgnoreCase)))
                    .ToArray();

                Log.Info($"Found {deadObjectKeys.Length} dead packages.");
                foreach (var objKey in deadObjectKeys) {
                    await RetryAsync(() => _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _options.bucket, Key = objKey }),
                        "Deleting dead package: " + objKey);
                }
            }

            Log.Info("Done");

            var endpoint = new Uri(_options.endpoint ?? RegionEndpoint.GetBySystemName(_options.region).GetEndpointForService("s3").Hostname);
            var baseurl = $"https://{_options.bucket}.{endpoint.Host}/{_prefix}";
            Log.Info($"Bucket URL:  {baseurl}");
            Log.Info($"Setup URL:   {baseurl}{setupFile.Name}");
        }

        private static async IAsyncEnumerable<S3Object> ListBucketContentsAsync(IAmazonS3 client, string bucketName)
        {
            var request = new ListObjectsV2Request {
                BucketName = bucketName,
                MaxKeys = 100,
            };

            ListObjectsV2Response response;
            do {
                response = await client.ListObjectsV2Async(request);
                foreach (var obj in response.S3Objects) {
                    yield return obj;
                }

                // If the response is truncated, set the request ContinuationToken
                // from the NextContinuationToken property of the response.
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated);
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
                        if (throwIfFail) throw;
                        else return;
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
