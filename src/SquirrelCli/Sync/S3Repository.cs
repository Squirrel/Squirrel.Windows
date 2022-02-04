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
                _client = new AmazonS3Client(_options.key, _options.secret, r);
            } else if (options.endpointUrl != null) {
                var config = new AmazonS3Config() { ServiceURL = _options.endpointUrl };
                _client = new AmazonS3Client(_options.key, _options.secret, config);
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
            Log.Info("Downloading RELEASES");
            using (var obj = await _client.GetObjectAsync(_options.bucket, _prefix + "RELEASES"))
                await obj.WriteResponseStreamToFileAsync(releasesPath, false, CancellationToken.None);

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
            var releasesDir = new DirectoryInfo(_options.releaseDir);

            var files = releasesDir.GetFiles();
            var setupFile = files.Where(f => f.FullName.EndsWith("Setup.exe")).SingleOrDefault();
            var releasesFile = files.Where(f => f.Name == "RELEASES").SingleOrDefault();
            var filesWithoutSpecial = files.Except(new[] { setupFile, releasesFile });

            foreach (var f in filesWithoutSpecial) {
                string key = _prefix + f.Name;
                string deleteOldVersionId = null;

                try {
                    var metadata = await _client.GetObjectMetadataAsync(_options.bucket, key);
                    var md5 = GetFileMD5Checksum(f.FullName);
                    var stored = metadata?.ETag?.Trim().Trim('"');

                    if (stored != null) {
                        if (stored.Equals(md5, StringComparison.InvariantCultureIgnoreCase)) {
                            Log.Info($"Skipping '{f.FullName}', matching file exists in remote.");
                            continue;
                        } else if (_options.overwrite) {
                            Log.Info($"File '{f.FullName}' exists in remote, replacing...");
                            deleteOldVersionId = metadata.VersionId;
                        } else {
                            Log.Warn($"File '{f.FullName}' exists in remote and checksum does not match. Use 'overwrite' argument to replace remote file.");
                            continue;
                        }
                    }
                } catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
                    // we don't care if the file does not exist, we're uploading!
                }

                var req = new PutObjectRequest {
                    BucketName = _options.bucket,
                    FilePath = f.FullName,
                    Key = key,
                };

                Log.Info("Uploading " + f.Name);
                var resp = await _client.PutObjectAsync(req);
                if ((int) resp.HttpStatusCode >= 300 || (int) resp.HttpStatusCode < 200)
                    throw new Exception("Failed to upload with status code " + resp.HttpStatusCode);

                if (deleteOldVersionId != null) {
                    Log.Info("Deleting old version of " + f.Name);
                    await _client.DeleteObjectAsync(_options.bucket, key, deleteOldVersionId);
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
