using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGet
{
    public class PriorityPackageRepository : PackageRepositoryBase, IPackageLookup, IOperationAwareRepository
    {
        private readonly IPackageRepository _primaryRepository;
        private readonly IPackageRepository _secondaryRepository;

        public PriorityPackageRepository(IPackageRepository primaryRepository, IPackageRepository secondaryRepository)
        {
            if (primaryRepository == null)
            {
                throw new ArgumentNullException("primaryRepository");
            }

            if (secondaryRepository == null)
            {
                throw new ArgumentNullException("secondaryRepository");
            }

            _primaryRepository = primaryRepository;
            _secondaryRepository = secondaryRepository;
        }

        internal IPackageRepository PrimaryRepository
        {
            get { return _primaryRepository; }
        }

        internal IPackageRepository SecondaryRepository
        {
            get { return _secondaryRepository; }
        }

        public override string Source
        {
            get { return _primaryRepository.Source; }
        }

        public override IQueryable<IPackage> GetPackages()
        {
            return _primaryRepository.GetPackages();
        }

        public override bool SupportsPrereleasePackages
        {
            get { return _primaryRepository.SupportsPrereleasePackages; }
        }

        public bool Exists(string packageId, SemanticVersion version)
        {
            bool packageExists = _primaryRepository.Exists(packageId, version);
            if (!packageExists)
            {
                packageExists = _secondaryRepository.Exists(packageId, version);
            }

            return packageExists;
        }

        public IPackage FindPackage(string packageId, SemanticVersion version)
        {
            return _primaryRepository.FindPackage(packageId, version) ?? _secondaryRepository.FindPackage(packageId, version);
        }

        public IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            IEnumerable<IPackage> packages = _primaryRepository.FindPackagesById(packageId);
            if (packages.IsEmpty())
            {
                packages = _secondaryRepository.FindPackagesById(packageId);
            }

            return packages.Distinct();
        }

        public IDisposable StartOperation(string operation, string mainPackageId, string mainPackageVersion)
        {
            return DisposableAction.All(_primaryRepository.StartOperation(operation, mainPackageId, mainPackageVersion),
                _secondaryRepository.StartOperation(operation, mainPackageId, mainPackageVersion));
        }
    }
}
