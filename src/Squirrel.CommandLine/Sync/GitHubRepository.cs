using System;
using System.IO;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;
using Squirrel.Sources;

namespace Squirrel.CommandLine.Sync
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
            var releaseDirectoryInfo = _options.GetReleaseDirectory();

            if (String.IsNullOrWhiteSpace(_options.token))
                Log.Warn("No GitHub access token provided. Unauthenticated requests will be limited to 60 per hour.");

            Log.Info("Fetching RELEASES...");
            var source = new GithubSource(_options.repoUrl, _options.token, _options.pre);
            var latestReleaseEntries = await source.GetReleaseFeed();

            if (latestReleaseEntries == null || latestReleaseEntries.Length == 0) {
                Log.Warn("No github release or assets found.");
                return;
            }

            Log.Info($"Found {latestReleaseEntries.Length} assets in latest release ({source.Release.Name}).");

            foreach (var entry in latestReleaseEntries) {
                var localFile = Path.Combine(releaseDirectoryInfo.FullName, entry.Filename);
                if (File.Exists(localFile)) {
                    Log.Info($"File '{entry.Filename}' exists on disk, skipping download.");
                    continue;
                }
                Log.Info($"Downloading {entry.Filename}...");
                await source.DownloadReleaseEntry(entry, localFile, (p) => { });
            }

            ReleaseEntry.BuildReleasesFile(releaseDirectoryInfo.FullName);
            Log.Info("Done.");
        }

        public Task UploadMissingPackages()
        {
            throw new NotImplementedException();
        }
    }
}
