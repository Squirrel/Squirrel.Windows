using System;
using System.IO;
using System.Linq;
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

            Log.Info($"Found {latestReleaseEntries.Length} assets in RELEASES file for GitHub version {source.Release.Name}.");

            var releasesToDownload = latestReleaseEntries
                .Where(x => !x.IsDelta)
                .OrderByDescending(x => x.Version)
                .Take(1)
                .Select(x => new {
                    Obj = x,
                    LocalPath = Path.Combine(releaseDirectoryInfo.FullName, x.Filename),
                    Filename = x.Filename,
                });

            foreach (var entry in releasesToDownload) {
                if (File.Exists(entry.LocalPath)) {
                    Log.Warn($"File '{entry.Filename}' exists on disk, skipping download.");
                    continue;
                }

                Log.Info($"Downloading {entry.Filename}...");
                await source.DownloadReleaseEntry(entry.Obj, entry.LocalPath, (p) => { });
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
