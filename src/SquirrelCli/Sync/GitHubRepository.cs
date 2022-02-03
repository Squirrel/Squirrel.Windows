using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Squirrel;
using Squirrel.SimpleSplat;

namespace SquirrelCli.Sources
{
    internal class GitHubRepository : IPackageRepository
    {
        private SyncGithubOptions _options;

        internal readonly static IFullLogger Log = SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(GitHubRepository));

        public GitHubRepository(SyncGithubOptions options)
        {
            _options = options;
        }

        public async Task DownloadRecentPackages()
        {
            var dl = Utility.CreateDefaultDownloader();

            var releaseDirectoryInfo = new DirectoryInfo(_options.releaseDir);
            if (!releaseDirectoryInfo.Exists)
                releaseDirectoryInfo.Create();

            var releases = await GithubUpdateManager.GetGithubReleases(
                new Uri(_options.repoUrl), _options.token, false, dl);

            if (!releases.Any()) {
                Log.Warn("No github releases found.");
                return;
            }

            string bearer = null;
            if (!string.IsNullOrWhiteSpace(_options.token))
                bearer = "Bearer " + _options.token;

            var lastRelease = await GetLastReleaseUrl(releases, dl, bearer);
            if (lastRelease.Url == null) {
                Log.Warn("No github releases found with a valid release attached.");
                return;
            }

            Log.Info("Downloading package from " + lastRelease.Url);

            var localFile = Path.Combine(releaseDirectoryInfo.FullName, lastRelease.Filename);
            await dl.DownloadFile(lastRelease.Url, localFile, null, bearer);

            var rf = ReleaseEntry.GenerateFromFile(localFile);
            ReleaseEntry.WriteReleaseFile(new[] { rf }, Path.Combine(releaseDirectoryInfo.FullName, "RELEASES"));
        }

        private async Task<(string Url, string Filename)> GetLastReleaseUrl(IEnumerable<GithubUpdateManager.GithubRelease> releases, IFileDownloader dl, string bearer)
        {
            foreach (var r in releases) {
                var releasesUrl = Utility.AppendPathToUri(new Uri(r.DownloadUrl), "RELEASES");

                Log.Info("Downloading metadata from " + releasesUrl);

                var releasesText = await dl.DownloadString(releasesUrl.ToString(), bearer);

                var entries = ReleaseEntry.ParseReleaseFile(releasesText);
                var latestAsset = entries
                    .Where(p => p.Version != null)
                    .Where(p => !p.IsDelta)
                    .OrderByDescending(p => p.Version)
                    .FirstOrDefault();

                if (latestAsset != null) {
                    return (Utility.AppendPathToUri(new Uri(r.DownloadUrl), latestAsset.Filename).ToString(), latestAsset.Filename);
                }
            }

            return (null, null);
        }

        public Task UploadMissingPackages()
        {
            throw new NotImplementedException();
        }
    }
}
