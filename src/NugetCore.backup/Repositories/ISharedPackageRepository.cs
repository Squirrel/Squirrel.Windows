using System.Collections.Generic;

namespace NuGet
{
    /// <summary>
    /// Represents the package repository in a solution. It contains:
    /// - A directory, called packages folder, where the packages are stored.
    /// - The repositories.config file in the packages folder, which contains the 
    ///   list of packages.config files of the projects in the solution.
    /// - The file packages.config in directory $(SolutionDir)/.nuget, which contains
    ///   the list of installed solution level packages.
    /// </summary>
    public interface ISharedPackageRepository : IPackageRepository
    {
        /// <summary>
        /// Determines whether a package is referenced by a project.
        /// </summary>
        /// <param name="packageId">The id of the package to check.</param>
        /// <param name="version">The version of the package to check. Can be null.</param>
        /// <returns>True if the package is referenced by a project; else false.</returns>
        bool IsReferenced(string packageId, SemanticVersion version);

        /// <summary>
        /// Gets whether the repository contains a solution-level package with the specified id and version.
        /// </summary>
        bool IsSolutionReferenced(string packageId, SemanticVersion version);
        
        /// <summary>
        /// Registers a new repository for the shared repository
        /// </summary>
        void RegisterRepository(PackageReferenceFile packageReferenceFile);

        /// <summary>
        /// Removes a registered repository
        /// </summary>
        void UnregisterRepository(PackageReferenceFile packageReferenceFile);

        /// <summary>
        /// Returns the list of project repositories.
        /// </summary>
        /// <returns>The list of project repositories.</returns>
        IEnumerable<IPackageRepository> LoadProjectRepositories();
    }
}
