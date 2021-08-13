using System;
using System.IO;
using System.Threading.Tasks;

namespace SyncReleases.Sources
{

    internal class SimpleWebRepository : IPackageRepository
    {
        public Uri TargetUri { get; }

        public SimpleWebRepository(Uri targetUri)
        {
            TargetUri = targetUri;
        }

        public Task DownloadRecentPackages(DirectoryInfo releasesDir)
        {
            return SyncImplementations.SyncRemoteReleases(TargetUri, releasesDir);
        }

        public Task UploadMissingPackages(DirectoryInfo releasesDir)
        {
            throw new NotImplementedException();
        }
    }
}
