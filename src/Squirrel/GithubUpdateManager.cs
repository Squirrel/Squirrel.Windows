using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Squirrel.Sources;

namespace Squirrel
{
    /// <summary>
    /// An implementation of UpdateManager which supports checking updates and 
    /// downloading releases directly from GitHub releases. This class is just a shorthand
    /// for initialising <see cref="UpdateManager"/> with a <see cref="GithubSource"/>
    /// as the first argument.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Use 'new UpdateManager(new GithubSource(...))' instead")]
    public class GithubUpdateManager : UpdateManager
    {
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
            : base(new GithubSource(repoUrl, accessToken, prerelease, urlDownloader), applicationIdOverride, localAppDataDirectoryOverride)
        {
        }
    }

    public partial class UpdateManager
    {
        /// <summary>
        /// This function is obsolete and will be removed in a future version,
        /// see the <see cref="GithubUpdateManager" /> class for a replacement.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use 'new UpdateManager(new GithubSource(...))' instead")]
        public static Task<UpdateManager> GitHubUpdateManager(
            string repoUrl,
            string applicationName = null,
            string rootDirectory = null,
            IFileDownloader urlDownloader = null,
            bool prerelease = false,
            string accessToken = null)
        {
            return Task.FromResult(new UpdateManager(new GithubSource(repoUrl, accessToken, prerelease, urlDownloader), applicationName, rootDirectory));
        }
    }
}
