using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace NuGet
{
    public interface IServiceBasedRepository : IPackageRepository
    {
        IQueryable<IPackage> Search(string searchTerm, IEnumerable<string> targetFrameworks, bool allowPrereleaseVersions, bool includeDelisted);

        IEnumerable<IPackage> GetUpdates(
            IEnumerable<IPackageName> packages, 
            bool includePrerelease, 
            bool includeAllVersions, 
            IEnumerable<FrameworkName> targetFrameworks,
            IEnumerable<IVersionSpec> versionConstraints);
    }
}
