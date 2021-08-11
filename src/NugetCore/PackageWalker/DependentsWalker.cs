using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;

namespace NuGet
{
    public class DependentsWalker : PackageWalker, IDependentsResolver
    {
        // this constructor is used for unit tests
        internal DependentsWalker(IPackageRepository repository) 
            : this(repository, targetFramework: null)
        {
        }

        public DependentsWalker(IPackageRepository repository, FrameworkName targetFramework) 
            : base(targetFramework)
        {
            if (repository == null)
            {
                throw new ArgumentNullException("repository");
            }
            Repository = repository;
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

        protected IPackageRepository Repository
        {
            get;
            private set;
        }

        private IDictionary<IPackage, HashSet<IPackage>> DependentsLookup
        {
            get;
            set;
        }

        protected override IPackage ResolveDependency(PackageDependency dependency)
        {
            return DependencyResolveUtility.ResolveDependency(Repository, dependency, allowPrereleaseVersions: true, preferListedPackages: false);
        }

        protected override bool OnAfterResolveDependency(IPackage package, IPackage dependency)
        {
            Debug.Assert(DependentsLookup != null);

            HashSet<IPackage> values;
            if (!DependentsLookup.TryGetValue(dependency, out values))
            {
                values = new HashSet<IPackage>(PackageEqualityComparer.IdAndVersion);
                DependentsLookup[dependency] = values;
            }

            // Add the current package to the list of dependents
            values.Add(package);
            return base.OnAfterResolveDependency(package, dependency);
        }

        public IEnumerable<IPackage> GetDependents(IPackage package)
        {
            if (DependentsLookup == null)
            {
                DependentsLookup = new Dictionary<IPackage, HashSet<IPackage>>(PackageEqualityComparer.IdAndVersion);
                foreach (IPackage p in Repository.GetPackages())
                {
                    Walk(p);
                }
            }

            HashSet<IPackage> dependents;
            if (DependentsLookup.TryGetValue(package, out dependents))
            {
                return dependents;
            }
            return Enumerable.Empty<IPackage>();
        }
    }
}
