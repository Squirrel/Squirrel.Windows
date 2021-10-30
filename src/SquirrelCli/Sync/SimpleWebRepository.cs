using System;
using System.IO;
using System.Threading.Tasks;

namespace SquirrelCli.Sources
{

    internal class SimpleWebRepository : IPackageRepository
    {
        private readonly SyncHttpOptions options;

        public SimpleWebRepository(SyncHttpOptions options)
        {
            this.options = options;
        }

        public Task DownloadRecentPackages()
        {
            return SyncImplementations.SyncRemoteReleases(new Uri(options.url), new DirectoryInfo(options.releaseDir));
        }

        public Task UploadMissingPackages()
        {
            throw new NotImplementedException();
        }
    }
}
