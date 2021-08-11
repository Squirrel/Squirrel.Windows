using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;

namespace NuGet
{
    /// <summary>
    /// This repository implementation keeps track of packages that are referenced in a project but
    /// it also has a reference to the repository that actually contains the packages. It keeps track
    /// of packages in an xml file at the project root (packages.xml).
    /// </summary>
    public class PackageReferenceRepository : IPackageReferenceRepository, IPackageLookup, IPackageConstraintProvider, ILatestPackageLookup, IPackageReferenceRepository2
    {
        private readonly PackageReferenceFile _packageReferenceFile;

        public PackageReferenceRepository(
            IFileSystem fileSystem,
            string projectName,
            ISharedPackageRepository sourceRepository)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }
            if (sourceRepository == null)
            {
                throw new ArgumentNullException("sourceRepository");
            }

            _packageReferenceFile = new PackageReferenceFile(
                fileSystem, Constants.PackageReferenceFile, projectName);

            SourceRepository = sourceRepository;
        }

        public PackageReferenceRepository(
            string configFilePath,
            ISharedPackageRepository sourceRepository)
        {
            if (String.IsNullOrEmpty(configFilePath))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "configFilePath");
            }

            if (sourceRepository == null)
            {
                throw new ArgumentNullException("sourceRepository");
            }

            _packageReferenceFile = new PackageReferenceFile(configFilePath);
            SourceRepository = sourceRepository;
        }

        public string Source
        {
            get
            {
                return Constants.PackageReferenceFile;
            }
        }

        public PackageSaveModes PackageSaveMode
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public bool SupportsPrereleasePackages
        {
            get { return true; }
        }

        private ISharedPackageRepository SourceRepository
        {
            get;
            set;
        }

        public PackageReferenceFile ReferenceFile
        {
            get
            {
                return _packageReferenceFile;
            }
        }

        public IQueryable<IPackage> GetPackages()
        {
            return GetPackagesCore().AsQueryable();
        }

        private IEnumerable<IPackage> GetPackagesCore()
        {
            return _packageReferenceFile.GetPackageReferences()
                                        .Select(GetPackage)
                                        .Where(p => p != null);
        }

        public void AddPackage(IPackage package)
        {
            AddPackage(package.Id, package.Version, package.DevelopmentDependency, targetFramework: null);
        }

        public void RemovePackage(IPackage package)
        {
            if (_packageReferenceFile.DeleteEntry(package.Id, package.Version))
            {
                // Remove the repository from the source
                SourceRepository.UnregisterRepository(_packageReferenceFile);
            }
        }

        public IPackage FindPackage(string packageId, SemanticVersion version)
        {
            if (!_packageReferenceFile.EntryExists(packageId, version))
            {
                return null;
            }

            return SourceRepository.FindPackage(packageId, version);
        }

        public IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            return GetPackageReferences(packageId).Select(GetPackage)
                                                  .Where(p => p != null);
        }

        public bool Exists(string packageId, SemanticVersion version)
        {
            return _packageReferenceFile.EntryExists(packageId, version);
        }

        public void RegisterIfNecessary()
        {
            if (GetPackages().Any())
            {
                SourceRepository.RegisterRepository(_packageReferenceFile);
            }
        }

        public IVersionSpec GetConstraint(string packageId)
        {
            // Find the reference entry for this package
            var reference = GetPackageReference(packageId);
            if (reference != null)
            {
                return reference.VersionConstraint;
            }
            return null;
        }

        public bool TryFindLatestPackageById(string id, out SemanticVersion latestVersion)
        {
            PackageReference reference = GetPackageReferences(id).OrderByDescending(r => r.Version)
                                                                 .FirstOrDefault();
            if (reference == null)
            {
                latestVersion = null;
                return false;
            }
            else
            {
                latestVersion = reference.Version;
                Debug.Assert(latestVersion != null);
                return true;
            }
        }

        public bool TryFindLatestPackageById(string id, bool includePrerelease, out IPackage package)
        {
            IEnumerable<PackageReference> references = GetPackageReferences(id);
            if (!includePrerelease)
            {
                references = references.Where(r => String.IsNullOrEmpty(r.Version.SpecialVersion));
            }

            PackageReference reference = references.OrderByDescending(r => r.Version).FirstOrDefault();
            if (reference != null)
            {
                package = GetPackage(reference);
                return true;
            }
            else
            {
                package = null;
                return false;
            }
        }

        public void AddPackage(string packageId, SemanticVersion version, bool developmentDependency, FrameworkName targetFramework)
        {
            _packageReferenceFile.AddEntry(packageId, version, developmentDependency, targetFramework);

            // Notify the source repository every time we add a new package to the repository.
            // This doesn't really need to happen on every package add, but this is over agressive
            // to combat scenarios where the 2 repositories get out of sync. If this repository is already
            // registered in the source then this will be ignored
            SourceRepository.RegisterRepository(_packageReferenceFile);
        }

        public FrameworkName GetPackageTargetFramework(string packageId)
        {
            var reference = GetPackageReference(packageId);
            if (reference != null)
            {
                return reference.TargetFramework;
            }
            return null;
        }

        public PackageReference GetPackageReference(string packageId)
        {
            return GetPackageReferences(packageId).FirstOrDefault();
        }

        public IEnumerable<PackageReference> GetPackageReferences()
        {
            return _packageReferenceFile.GetPackageReferences();
        }

        /// <summary>
        /// Gets all references to a specific package id that are valid.
        /// </summary>
        /// <param name="packageId"></param>
        /// <returns></returns>
        public IEnumerable<PackageReference> GetPackageReferences(string packageId)
        {
            return _packageReferenceFile.GetPackageReferences()
                                        .Where(reference => IsValidReference(reference) &&
                                                            reference.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        }

        private IPackage GetPackage(PackageReference reference)
        {
            if (IsValidReference(reference))
            {
                return SourceRepository.FindPackage(reference.Id, reference.Version);
            }
            return null;
        }

        private static bool IsValidReference(PackageReference reference)
        {
            return !String.IsNullOrEmpty(reference.Id) && reference.Version != null;
        }
    }

    // TODO: This is a temporary interface that should be deleted.
    public interface IPackageReferenceRepository2 : IPackageRepository
    {
        PackageReference GetPackageReference(string packageId);

        IEnumerable<PackageReference> GetPackageReferences(string packageId);
    }
}