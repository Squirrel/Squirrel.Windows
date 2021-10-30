using System.IO;
using System.Threading.Tasks;

namespace SquirrelCli.Sources
{
    internal interface IPackageRepository
    {
        public Task DownloadRecentPackages();
        public Task UploadMissingPackages();
    }
}
