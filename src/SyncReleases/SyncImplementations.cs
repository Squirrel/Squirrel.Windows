using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Squirrel;
using Octokit;
using System.Reflection;
using System.Net;

namespace SyncReleases
{
    internal class SyncImplementations
    {
        public static async Task SyncRemoteReleases(Uri targetUri, DirectoryInfo releasesDir)
        {
            var releasesUri = Utility.AppendPathToUri(targetUri, "RELEASES");
            var releasesIndex = await retryAsync(3, () => downloadReleasesIndex(releasesUri));

            File.WriteAllText(Path.Combine(releasesDir.FullName, "RELEASES"), releasesIndex);

            var releasesToDownload = ReleaseEntry.ParseReleaseFile(releasesIndex)
                .Where(x => !x.IsDelta)
                .OrderByDescending(x => x.Version)
                .Take(1)
                .Select(x => new {
                    LocalPath = Path.Combine(releasesDir.FullName, x.Filename),
                    RemoteUrl = new Uri(Utility.EnsureTrailingSlash(targetUri), x.BaseUrl + x.Filename + x.Query)
                 });

            foreach (var releaseToDownload in releasesToDownload) {
                await retryAsync(3, () => downloadRelease(releaseToDownload.LocalPath, releaseToDownload.RemoteUrl));
            }
        }

        public static async Task SyncFromGitHub(string repoUrl, string token, DirectoryInfo releaseDirectoryInfo)
        {
            var repoUri = new Uri(repoUrl);
            var userAgent = new ProductHeaderValue("SyncReleases", Assembly.GetExecutingAssembly().GetName().Version.ToString());

            var client = new GitHubClient(userAgent, repoUri);

            if (token != null) {
                client.Credentials = new Credentials(token);
            }

            var nwo = nwoFromRepoUrl(repoUrl);
            var releases = (await client.Release.GetAll(nwo.Item1, nwo.Item2))
                .OrderByDescending(x => x.PublishedAt)
                .Take(5);

            await releases.ForEachAsync(async release => {
                // NB: Why do I have to double-fetch the release assets? It's already in GetAll
                var assets = await client.Release.GetAllAssets(nwo.Item1, nwo.Item2, release.Id);

                await assets
                    .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    .Where(x => {
                        var fi = new FileInfo(Path.Combine(releaseDirectoryInfo.FullName, x.Name));
                        return !(fi.Exists && fi.Length == x.Size);
                    })
                    .ForEachAsync(async x => {
                        var target = new FileInfo(Path.Combine(releaseDirectoryInfo.FullName, x.Name));
                        if (target.Exists) target.Delete();

                        await retryAsync(3, async () => {
                            var hc = new HttpClient();
                            var rq = new HttpRequestMessage(HttpMethod.Get, x.Url);
                            rq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/octet-stream"));
                            rq.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(userAgent.Name, userAgent.Version));
                            if (token != null) {
                                rq.Headers.Add("Authorization", "Bearer " + token);
                            }

                            var resp = await hc.SendAsync(rq);
                            resp.EnsureSuccessStatusCode();

                            using (var from = await resp.Content.ReadAsStreamAsync())
                            using (var to = File.OpenWrite(target.FullName)) {
                                await from.CopyToAsync(to);
                            }
                        });
                    });
            });

            var entries = releaseDirectoryInfo.GetFiles("*.nupkg")
                .AsParallel()
                .Select(x => ReleaseEntry.GenerateFromFile(x.FullName));

            ReleaseEntry.WriteReleaseFile(entries, Path.Combine(releaseDirectoryInfo.FullName, "RELEASES"));
        }

        static async Task<string> downloadReleasesIndex(Uri uri)
        {
            Console.WriteLine("Trying to download RELEASES index from {0}", uri);

            using (HttpClient client = new HttpClient()) {
                return await client.GetStringAsync(uri);
            }
        }

        static async Task downloadRelease(string localPath, Uri remoteUrl)
        {
            if (File.Exists(localPath)) {
                File.Delete(localPath);
            }

            Console.WriteLine("Downloading release from {0}", remoteUrl);
            var wc = new NotBrokenWebClient();
            await wc.DownloadFileTaskAsync(remoteUrl, localPath);
        }

        static Tuple<string, string> nwoFromRepoUrl(string repoUrl)
        {
            var uri = new Uri(repoUrl);

            var segments = uri.AbsolutePath.Split('/');
            if (segments.Count() != 3) {
                throw new Exception("Repo URL must be to the root URL of the repo e.g. https://github.com/myuser/myrepo");
            }

            return Tuple.Create(segments[1], segments[2]);
        }

        static async Task<T> retryAsync<T>(int count, Func<Task<T>> block)
        {
            int retryCount = count;

        retry:
            try {
                return await block();
            } catch (Exception) {
                retryCount--;
                if (retryCount >= 0) goto retry;

                throw;
            }
        }

        static async Task retryAsync(int count, Func<Task> block)
        {
            await retryAsync(count, async () => { await block(); return false; });
        }
    }

    class NotBrokenWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            var wr = base.GetWebRequest(address);
            var hwr = wr as HttpWebRequest;
            if (hwr == null) return wr;

            hwr.AllowAutoRedirect = true;
            hwr.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return hwr;
        }
    }
}
