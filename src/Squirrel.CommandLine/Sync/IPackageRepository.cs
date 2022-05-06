using System.Threading.Tasks;

namespace Squirrel.CommandLine.Sync
{
    internal interface IPackageRepository
    {
        public Task DownloadRecentPackages();
        public Task UploadMissingPackages();
    }
}
