using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace NuGet
{
    public class PackageSorter : PackageWalker
    {
        private IPackageRepository _repository;
        private IList<IPackage> _sortedPackages;

        // this ctor is used for unit tests
        internal PackageSorter()
        {
        }

        public PackageSorter(FrameworkName targetFramework) 
            : base(targetFramework)
        {
        }

        protected override bool RaiseErrorOnCycle
        {
            get
            {
                return false;
            }
        }

        protected override bool IgnoreWalkInfo
        {
            get
            {
                return true;
            }
        }

        protected override bool SkipDependencyResolveError
        {
            get
            {
                return true;
            }
        }

        protected override void OnAfterPackageWalk(IPackage package)
        {
            base.OnAfterPackageWalk(package);

            _sortedPackages.Add(package);
        }

        protected override IPackage ResolveDependency(PackageDependency dependency)
        {
            return DependencyResolveUtility.ResolveDependency(_repository, dependency, allowPrereleaseVersions: true, preferListedPackages: false);
        }

        protected override void OnDependencyResolveError(PackageDependency dependency)
        {
            // ignore dependency error
        }

        /// <summary>
        /// Get all packages from the specified repository in the dependency order, 
        /// e.g. if A -> B, then B will come before A.
        /// </summary>
        public IEnumerable<IPackage> GetPackagesByDependencyOrder(IPackageRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException("repository");
            }
            Marker.Clear();

            _repository = repository;
            _sortedPackages = new List<IPackage>();

            foreach (var package in _repository.GetPackages())
            {
                Walk(package);
            }

            return _sortedPackages;
        }
    }
}
