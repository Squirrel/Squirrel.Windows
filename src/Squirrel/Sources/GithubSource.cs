using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Squirrel.Json;

namespace Squirrel.Sources
{
    /// <summary> Describes a GitHub release, including attached assets. </summary>
    [DataContract]
    public class GithubRelease
    {
        /// <summary> The name of this release. </summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary> True if this release is a prerelease. </summary>
        [DataMember(Name = "prerelease")]
        public bool Prerelease { get; set; }

        /// <summary> The date which this release was published publically. </summary>
        [DataMember(Name = "published_at")]
        public DateTime PublishedAt { get; set; }

        /// <summary> A list of assets (files) uploaded to this release. </summary>
        [DataMember(Name = "assets")]
        public GithubReleaseAsset[] Assets { get; set; }
    }

    /// <summary> Describes a asset (file) uploaded to a GitHub release. </summary>
    [DataContract]
    public class GithubReleaseAsset
    {
        /// <summary> 
        /// The asset URL for this release asset. Requests to this URL will use API
        /// quota and return JSON unless the 'Accept' header is "application/octet-stream". 
        /// </summary>
        [DataMember(Name = "url")]
        public string Url { get; set; }

        /// <summary>  
        /// The browser URL for this release asset. This does not use API quota,
        /// however this URL only works for public repositories. If downloading
        /// assets from a private repository, the <see cref="Url"/> property must
        /// be used with an appropriate access token.
        /// </summary>
        [DataMember(Name = "browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        /// <summary> The name of this release asset. </summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary> The mime type of this release asset (as detected by GitHub). </summary>
        [DataMember(Name = "content_type")]
        public string ContentType { get; set; }
    }

    /// <summary>
    /// Retrieves available releases from a GitHub repository. This class only
    /// downloads assets from the very latest GitHub release.
    /// </summary>
    public class GithubSource : IUpdateSource
    {
        /// <summary> 
        /// The URL of the GitHub repository to download releases from 
        /// (e.g. https://github.com/myuser/myrepo)
        /// </summary>
        public virtual Uri RepoUri { get; }

        /// <summary>  
        /// If true, the latest pre-release will be downloaded. If false, the latest 
        /// stable release will be downloaded.
        /// </summary>
        public virtual bool Prerelease { get; }

        /// <summary> 
        /// The file downloader used to perform HTTP requests. 
        /// </summary>
        public virtual IFileDownloader Downloader { get; }

        /// <summary>  
        /// The GitHub release which this class should download assets from when 
        /// executing <see cref="DownloadReleaseEntry"/>. This property can be set
        /// explicitly, otherwise it will also be set automatically when executing
        /// <see cref="GetReleaseFeed(Guid?, ReleaseEntry)"/>.
        /// </summary>
        public virtual GithubRelease Release { get; set; }

        /// <summary>
        /// The GitHub access token to use with the request to download releases. 
        /// If left empty, the GitHub rate limit for unauthenticated requests allows 
        /// for up to 60 requests per hour, limited by IP address.
        /// </summary>
        protected virtual string AccessToken { get; }

        /// <summary> The Bearer token used in the request. </summary>
        protected virtual string Authorization => String.IsNullOrWhiteSpace(AccessToken) ? null : "Bearer " + AccessToken;

        /// <inheritdoc cref="GithubSource" />
        /// <param name="repoUrl">
        /// The URL of the GitHub repository to download releases from 
        /// (e.g. https://github.com/myuser/myrepo)
        /// </param>
        /// <param name="accessToken">
        /// The GitHub access token to use with the request to download releases. 
        /// If left empty, the GitHub rate limit for unauthenticated requests allows 
        /// for up to 60 requests per hour, limited by IP address.
        /// </param>
        /// <param name="prerelease">
        /// If true, the latest pre-release will be downloaded. If false, the latest 
        /// stable release will be downloaded.
        /// </param>
        /// <param name="downloader">
        /// The file downloader used to perform HTTP requests. 
        /// </param>
        public GithubSource(string repoUrl, string accessToken, bool prerelease, IFileDownloader downloader = null)
        {
            RepoUri = new Uri(repoUrl);
            AccessToken = accessToken;
            Prerelease = prerelease;
            Downloader = downloader ?? Utility.CreateDefaultDownloader();
        }

        /// <inheritdoc />
        public virtual async Task<ReleaseEntry[]> GetReleaseFeed(Guid? stagingId = null, ReleaseEntry latestLocalRelease = null)
        {
            var releases = await GetReleases(Prerelease).ConfigureAwait(false);
            if (releases == null || releases.Count() == 0)
                throw new Exception($"No GitHub releases found at '{RepoUri}'.");

            // CS: we 'cache' the release here, so subsequent calls to DownloadReleaseEntry
            // will download assets from the same release in which we returned ReleaseEntry's
            // from. A better architecture would be to return an array of "GithubReleaseEntry"
            // containing a reference to the GithubReleaseAsset instead.
            Release = releases.First();

            // this might be a browser url or an api url (depending on whether we have a AccessToken or not)
            // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
            var assetUrl = GetAssetUrlFromName(Release, "RELEASES");
            var releaseBytes = await Downloader.DownloadBytes(assetUrl, Authorization, "application/octet-stream").ConfigureAwait(false);
            var txt = Utility.RemoveByteOrderMarkerIfPresent(releaseBytes);
            return ReleaseEntry.ParseReleaseFileAndApplyStaging(txt, stagingId).ToArray();
        }

        /// <inheritdoc />
        public virtual Task DownloadReleaseEntry(ReleaseEntry releaseEntry, string localFile, Action<int> progress)
        {
            if (Release == null) {
                throw new InvalidOperationException("No GitHub Release specified. Call GetReleaseFeed or set " +
                    "GithubSource.Release before calling this function.");
            }

            // this might be a browser url or an api url (depending on whether we have a AccessToken or not)
            // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
            var assetUrl = GetAssetUrlFromName(Release, releaseEntry.Filename);
            return Downloader.DownloadFile(assetUrl, localFile, progress, Authorization, "application/octet-stream");
        }

        /// <summary>
        /// Retrieves a list of <see cref="GithubRelease"/> from the current repository.
        /// </summary>
        public virtual async Task<GithubRelease[]> GetReleases(bool includePrereleases, int perPage = 30, int page = 1)
        {
            // https://docs.github.com/en/rest/reference/releases
            var releasesPath = $"repos{RepoUri.AbsolutePath}/releases?per_page={perPage}&page={page}";
            var baseUri = GetApiBaseUrl(RepoUri);
            var getReleasesUri = new Uri(baseUri, releasesPath);
            var response = await Downloader.DownloadString(getReleasesUri.ToString(), Authorization, "application/vnd.github.v3+json").ConfigureAwait(false);
            var releases = SimpleJson.DeserializeObject<List<GithubRelease>>(response);
            return releases.OrderByDescending(d => d.PublishedAt).Where(x => includePrereleases || !x.Prerelease).ToArray();
        }

        /// <summary>
        /// Given a <see cref="GithubRelease"/> and an asset filename (eg. 'RELEASES') this 
        /// function will return either <see cref="GithubReleaseAsset.BrowserDownloadUrl"/> or
        /// <see cref="GithubReleaseAsset.Url"/>, depending whether an access token is available
        /// or not. Throws if the specified release has no matching assets.
        /// </summary>
        protected virtual string GetAssetUrlFromName(GithubRelease release, string assetName)
        {
            if (release.Assets == null || release.Assets.Count() == 0) {
                throw new ArgumentException($"No assets found in Github Release '{release.Name}'.");
            }

            IEnumerable<GithubReleaseAsset> allReleasesFiles = release.Assets.Where(a => a.Name.Equals(assetName, StringComparison.InvariantCultureIgnoreCase));
            if (allReleasesFiles == null || allReleasesFiles.Count() == 0) {
                throw new ArgumentException($"Could not find asset called '{assetName}' in Github Release '{release.Name}'.");
            }

            var asset = allReleasesFiles.First();

            if (String.IsNullOrWhiteSpace(AccessToken)) {
                // if no AccessToken provided, we use the BrowserDownloadUrl which does not 
                // count towards the "unauthenticated api request" limit of 60 per hour per IP.
                return asset.BrowserDownloadUrl;
            } else {
                // otherwise, we use the regular asset url, which will allow us to retrieve
                // assets from private repositories
                // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
                return asset.Url;
            }
        }

        /// <summary>
        /// Given a repository URL (e.g. https://github.com/myuser/myrepo) this function
        /// returns the API base for performing requests. (eg. "https://api.github.com/" 
        /// or http://internal.github.server.local/api/v3)
        /// </summary>
        /// <param name="repoUrl"></param>
        /// <returns></returns>
        protected virtual Uri GetApiBaseUrl(Uri repoUrl)
        {
            Uri baseAddress;
            if (repoUrl.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase)) {
                baseAddress = new Uri("https://api.github.com/");
            } else {
                // if it's not github.com, it's probably an Enterprise server
                // now the problem with Enterprise is that the API doesn't come prefixed
                // it comes suffixed so the API path of http://internal.github.server.local
                // API location is http://internal.github.server.local/api/v3
                baseAddress = new Uri(string.Format("{0}{1}{2}/api/v3/", repoUrl.Scheme, Uri.SchemeDelimiter, repoUrl.Host));
            }
            // above ^^ notice the end slashes for the baseAddress, explained here: http://stackoverflow.com/a/23438417/162694
            return baseAddress;
        }
    }
}
