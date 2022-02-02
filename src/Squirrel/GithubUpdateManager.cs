using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Squirrel.Json;

namespace Squirrel
{
    /// <summary>
    /// An implementation of UpdateManager which supports checking updates and 
    /// downloading releases directly from GitHub releases
    /// </summary>
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public class GithubUpdateManager : UpdateManager
    {
        private readonly string _repoUrl;
        private readonly string _accessToken;
        private readonly bool _prerelease;

        /// <inheritdoc cref="UpdateManager(string, string, string, IFileDownloader)"/>
        /// <param name="repoUrl">
        /// The URL of the GitHub repository to download releases from 
        /// (e.g. https://github.com/myuser/myrepo)
        /// </param>
        /// <param name="applicationIdOverride">
        /// The Id of your application should correspond with the 
        /// appdata directory name, and the Id used with Squirrel releasify/pack.
        /// If left null/empty, will attempt to determine the current application Id  
        /// from the installed app location.
        /// </param>
        /// <param name="urlDownloader">
        /// A custom file downloader, for using non-standard package sources or adding 
        /// proxy configurations. 
        /// </param>
        /// <param name="localAppDataDirectoryOverride">
        /// Provide a custom location for the system LocalAppData, it will be used 
        /// instead of <see cref="Environment.SpecialFolder.LocalApplicationData"/>.
        /// </param>
        /// <param name="prerelease">
        /// If true, the latest pre-release will be downloaded. If false, the latest 
        /// stable release will be downloaded.
        /// </param>
        /// <param name="accessToken">
        /// The GitHub access token to use with the request to download releases. 
        /// If left empty, the GitHub rate limit for unauthenticated requests allows 
        /// for up to 60 requests per hour, limited by IP address.
        /// </param>
        public GithubUpdateManager(
            string repoUrl,
            bool prerelease = false,
            string accessToken = null,
            string applicationIdOverride = null,
            string localAppDataDirectoryOverride = null,
            IFileDownloader urlDownloader = null)
            : base(null, applicationIdOverride, localAppDataDirectoryOverride, urlDownloader)
        {
            _repoUrl = repoUrl;
            _accessToken = accessToken;
            _prerelease = prerelease;
        }

        /// <inheritdoc />
        public override async Task<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates = false, Action<int> progress = null, UpdaterIntention intention = UpdaterIntention.Update)
        {
            await EnsureReleaseUrl().ConfigureAwait(false);
            return await base.CheckForUpdate(ignoreDeltaUpdates, progress, intention).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null)
        {
            await EnsureReleaseUrl().ConfigureAwait(false);
            await base.DownloadReleases(releasesToDownload, progress).ConfigureAwait(false);
        }

        private async Task EnsureReleaseUrl()
        {
            if (this._updateUrlOrPath == null) {
                this._updateUrlOrPath = await GetLatestGithubRelease().ConfigureAwait(false);
            }
        }

        private async Task<string> GetLatestGithubRelease()
        {
            var repoUri = new Uri(_repoUrl);
            var userAgent = new ProductInfoHeaderValue("Squirrel", AssemblyRuntimeInfo.ExecutingAssemblyName.Version.ToString());

            if (repoUri.Segments.Length != 3) {
                throw new Exception("Repo URL must be to the root URL of the repo e.g. https://github.com/myuser/myrepo");
            }

            var releasesApiBuilder = new StringBuilder("repos")
                .Append(repoUri.AbsolutePath)
                .Append("/releases");

            if (!string.IsNullOrWhiteSpace(_accessToken))
                releasesApiBuilder.Append("?access_token=").Append(_accessToken);

            Uri baseAddress;

            if (repoUri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase)) {
                baseAddress = new Uri("https://api.github.com/");
            } else {
                // if it's not github.com, it's probably an Enterprise server
                // now the problem with Enterprise is that the API doesn't come prefixed
                // it comes suffixed
                // so the API path of http://internal.github.server.local API location is
                // http://interal.github.server.local/api/v3. 
                baseAddress = new Uri(string.Format("{0}{1}{2}/api/v3/", repoUri.Scheme, Uri.SchemeDelimiter, repoUri.Host));
            }

            // above ^^ notice the end slashes for the baseAddress, explained here: http://stackoverflow.com/a/23438417/162694

            using var client = new HttpClient() { BaseAddress = baseAddress };
            client.DefaultRequestHeaders.UserAgent.Add(userAgent);
            var response = await client.GetAsync(releasesApiBuilder.ToString()).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var releases = SimpleJson.DeserializeObject<List<Release>>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var latestRelease = releases
                .Where(x => _prerelease || !x.Prerelease)
                .OrderByDescending(x => x.PublishedAt)
                .First();

            var latestReleaseUrl = latestRelease.HtmlUrl.Replace("/tag/", "/download/");
            return latestReleaseUrl;
        }

        [DataContract]
        private class Release
        {
            [DataMember(Name = "prerelease")]
            public bool Prerelease { get; set; }

            [DataMember(Name = "published_at")]
            public DateTime PublishedAt { get; set; }

            [DataMember(Name = "html_url")]
            public string HtmlUrl { get; set; }
        }
    }
}
