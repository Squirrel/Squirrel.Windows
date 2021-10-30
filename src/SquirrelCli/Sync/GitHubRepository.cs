using System;
using System.IO;
using System.Threading.Tasks;

namespace SquirrelCli.Sources
{
    internal class GitHubRepository : IPackageRepository
    {
        private SyncGithubOptions _options;

        public GitHubRepository(SyncGithubOptions options)
        {
            _options = options;
        }

        public Task DownloadRecentPackages()
        {
            return SyncImplementations.SyncFromGitHub(_options.repoUrl, _options.token, new DirectoryInfo(_options.releaseDir));
        }

        public Task UploadMissingPackages()
        {
            throw new NotImplementedException();
        }
    }
}
