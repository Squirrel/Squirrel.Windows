using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using B2Net;
using B2Net.Models;
using Squirrel;

namespace Squirrel.SyncReleases.Sources
{
    internal class BackblazeRepository : IPackageRepository
    {
        private B2StorageProvider _b2;
        public BackblazeRepository(string keyId, string appKey, string bucketId)
        {
            _b2 = new B2StorageProvider(keyId, appKey, bucketId);
        }

        public async Task DownloadRecentPackages(DirectoryInfo releasesDir)
        {
            Console.WriteLine("Downloading RELEASES");
            var releasesBytes = await _b2.DownloadFile("RELEASES");
            if (releasesBytes == null) {
                Console.WriteLine("Can't find RELEASES on remote. Nothing to download.");
                return;
            }

            File.WriteAllBytes(Path.Combine(releasesDir.FullName, "RELEASES"), releasesBytes);

            var releasesToDownload = ReleaseEntry.ParseReleaseFile(Encoding.UTF8.GetString(releasesBytes))
               .Where(x => !x.IsDelta)
               .OrderByDescending(x => x.Version)
               .Take(1)
               .Select(x => new {
                   LocalPath = Path.Combine(releasesDir.FullName, x.Filename),
                   Filename = x.Filename,
               });

            foreach (var releaseToDownload in releasesToDownload) {
                Console.WriteLine("Downloading " + releaseToDownload.Filename);
                var bytes = await _b2.DownloadFile(releaseToDownload.Filename);
                File.WriteAllBytes(releaseToDownload.LocalPath, bytes);
            }
        }

        public async Task UploadMissingPackages(DirectoryInfo releasesDir)
        {
            foreach (var f in releasesDir.GetFiles()) {
                await _b2.UploadFile(File.ReadAllBytes(f.FullName), f.Name);
            }
        }

        private class B2StorageProvider
        {
            private readonly B2Client _client;
            private List<B2File> _metadataCache;

            public B2StorageProvider(string keyId, string appKey, string bucketId)
            {
                var options = new B2Options() {
                    KeyId = keyId,
                    ApplicationKey = appKey,
                    BucketId = bucketId,
                    PersistBucket = true
                };
                _client = new B2Client(options, authorizeOnInitialize: true);
            }

            private async Task EnsureMetadata()
            {
                if (_metadataCache != null)
                    return;

                Console.WriteLine("Downloading b2 file metadata (only needs to be done once)");

                _metadataCache = new List<B2File>();
                B2FileList list = null;
                do {
                    if (list == null)
                        list = await _client.Files.GetList();
                    else
                        list = await _client.Files.GetList(startFileName: list.NextFileName, maxFileCount: 100);

                    _metadataCache.AddRange(list.Files);
                }
                while (!String.IsNullOrEmpty(list.NextFileName));
            }

            public async Task<byte[]> DownloadFile(string fileName)
            {
                Console.WriteLine("Downloading b2 file: " + fileName);
                await EnsureMetadata();
                fileName = fileName.Replace('\\', '/').TrimEnd('/');

                var search = _metadataCache.FirstOrDefault(f => f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                if (search == null) return null;

                var dl = await _client.Files.DownloadById(search.FileId);
                return dl?.FileData;
            }

            public async Task UploadFile(byte[] bytes, string fileName)
            {
                Console.WriteLine("Uploading b2 file: " + fileName);
                await EnsureMetadata();
                fileName = fileName.Replace('\\', '/').TrimEnd('/');
                var sha1 = GetSHA1Checksum(bytes);

                // if there is an identical file in the remote
                var nameMatch = _metadataCache.FirstOrDefault(f => f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                if (nameMatch != null && nameMatch.ContentSHA1.Equals(sha1, StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine($"  File already exists in remote and sha1 hash matches (skipping)");
                    return;
                }

                // if a file has the same contents, but a different name, we can do a server-copy
                B2File uploaded;
                var sha1Match = _metadataCache.FirstOrDefault(f => f.ContentSHA1.Equals(sha1, StringComparison.OrdinalIgnoreCase));
                if (sha1Match != null) {
                    Console.WriteLine($"  File with different name but matching SHA1 found (copying server-side)");
                    uploaded = await _client.Files.Copy(sha1Match.FileId, fileName);
                } else {
                    // upload file, retry 3 times
                    // also, if the file already exists, we need to delete the old version afterwards
                    int retry = 3;
                    while (true) {
                        try {
                            uploaded = await _client.Files.Upload(bytes, fileName);
                            break;
                        } catch (Exception e) {
                            Console.WriteLine($"  File upload failed ({e.Message}). Will try {retry} more times.");
                            await Task.Delay(100);
                            if (--retry < 0) throw;
                        }
                    }
                }

                // if we uploaded a file, and _also_ there was a name match, we should delete the old file as the contents were wrong
                if (nameMatch != null) {
                    Console.WriteLine($"  Existing file was updated, old version is being deleted");
                    await _client.Files.Delete(nameMatch.FileId, nameMatch.FileName);
                }
            }

            private static string GetSHA1Checksum(byte[] bytes)
            {
                var sha = new System.Security.Cryptography.SHA1Managed();
                byte[] checksum = sha.ComputeHash(bytes);
                return BitConverter.ToString(checksum).Replace("-", String.Empty);
            }
        }
    }
}
