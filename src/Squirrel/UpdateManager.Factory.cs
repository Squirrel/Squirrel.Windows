using Splat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Squirrel.Json;

namespace Squirrel
{
    public sealed partial class UpdateManager
    {
        const string gitHubUrl = "https://api.github.com";

        [DataContract]
        public class Release
        {
            [DataMember(Name = "prerelease")]
            public bool Prerelease { get; set; }

            [DataMember(Name = "published_at")]
            public DateTime PublishedAt { get; set; }

            [DataMember(Name = "html_url")]
            public string HtmlUrl { get; set; }
        }

        public static async Task<UpdateManager> GitHubUpdateManager(
            string repoUrl,
            string applicationName = null,
            string rootDirectory = null,
            IFileDownloader urlDownloader = null,
            bool prerelease = false)
        {
            var repoUri = new Uri(repoUrl);
            var userAgent = new ProductInfoHeaderValue("Squirrel", Assembly.GetExecutingAssembly().GetName().Version.ToString());

            if (repoUri.Segments.Count() != 3) {
                throw new Exception("Repo URL must be to the root URL of the repo e.g. https://github.com/myuser/myrepo");
            }

            using (var client = new HttpClient() { BaseAddress = new Uri(gitHubUrl) }) {
                client.DefaultRequestHeaders.UserAgent.Add(userAgent);
                var response = await client.GetAsync(String.Format("/repos{0}/releases", repoUri.PathAndQuery));
                response.EnsureSuccessStatusCode();

                var releases = SimpleJson.DeserializeObject<List<Release>>(await response.Content.ReadAsStringAsync());
                var latestRelease = releases
                    .Where(x => prerelease ? x.Prerelease : !x.Prerelease)
                    .OrderByDescending(x => x.PublishedAt)
                    .First();

                var latestReleaseUrl = latestRelease.HtmlUrl.Replace("/tag/", "/download/");

                return new UpdateManager(latestReleaseUrl, applicationName, rootDirectory, urlDownloader);
            }
        }
    }
}