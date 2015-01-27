using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Squirrel;

namespace SyncGitHubReleases
{
    internal class StandardHttpSyncer
    {
        private readonly Uri m_Uri;

        public StandardHttpSyncer(Uri uri)
        {
            m_Uri = uri;
        }

        public async Task Sync(DirectoryInfo releasesDir)
        {
            
            var releasesIndex = await DownloadReleasesIndex(m_Uri);

            File.WriteAllText(Path.Combine(releasesDir.FullName, "RELEASES"), releasesIndex);

            var releasesToDownload = ReleaseEntry.ParseReleaseFile(releasesIndex)
                .Where(x => !x.IsDelta)
                .OrderByDescending(x => x.Version)
                .Take(2)
                .Select(x => new
                             {
                                 LocalPath = Path.Combine(releasesDir.FullName, x.Filename),
                                 RemoteUrl = new Uri(m_Uri, x.Filename)
                             }
                );

            foreach (var releaseToDownload in releasesToDownload)
                await DownloadRelease(releaseToDownload.LocalPath, releaseToDownload.RemoteUrl);
        }

        private async Task<string> DownloadReleasesIndex(Uri repoUrl)
        {
            var uri = new Uri(repoUrl, "RELEASES");

            Console.WriteLine("Trying to download RELEASES index from {0}", uri);

            using (HttpClient client = new HttpClient())
            {
                return await client.GetStringAsync(uri);
            }
        }

        private async Task DownloadRelease(string localPath, Uri remoteUrl)
        {
            if (File.Exists(localPath))
            {
                Console.WriteLine("Skipping this release as existing file found at: {0}", localPath);
                return;
            }

            Console.WriteLine("Downloading release from {0}", remoteUrl);

            using (HttpClient client = new HttpClient())
            {
                using (var localStream = File.Create(localPath))
                {
                    using (var remoteStream = await client.GetStreamAsync(remoteUrl))
                    {
                        await remoteStream.CopyToAsync(localStream);
                    }
                }
            }
        }
    }
}