using System;
using System.IO;
using System.Threading.Tasks;

namespace SyncReleases.Sources
{
    internal class GitHubRepository : IPackageRepository
    {
        public string RepoUrl { get; }
        public string Token { get; }

        public GitHubRepository(string repoUrl, string token)
        {
            RepoUrl = repoUrl;
            Token = token;
        }

        public Task DownloadRecentPackages(DirectoryInfo releasesDir)
        {
            return SyncImplementations.SyncFromGitHub(RepoUrl, Token, releasesDir);
        }

        public Task UploadMissingPackages(DirectoryInfo releasesDir)
        {
            throw new NotImplementedException();
        }
    }
}
