using System.Collections.Generic;

namespace NuGet
{
    public interface IPackageLookup : IPackageRepository
    {
        /// <summary>
        /// Determines if a package exists in a repository.
        /// </summary>
        bool Exists(string packageId, SemanticVersion version);

        /// <summary>
        /// Finds packages that match the exact Id and version.
        /// </summary>
        /// <returns>The package if found, null otherwise.</returns>
        IPackage FindPackage(string packageId, SemanticVersion version);

        /// <summary>
        /// Returns a sequence of packages with the specified id.
        /// </summary>
        IEnumerable<IPackage> FindPackagesById(string packageId);
    }
}
