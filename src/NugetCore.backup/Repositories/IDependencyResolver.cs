using System.Collections.Generic;
using System.Linq;

namespace NuGet
{
    public interface IDependencyResolver
    {
        IPackage ResolveDependency(PackageDependency dependency, IPackageConstraintProvider constraintProvider, bool allowPrereleaseVersions, bool preferListedPackages, DependencyVersion dependencyVersion);
    }

    public interface IDependencyResolver2
    {
        IPackage ResolveDependency(PackageDependency dependency, IPackageConstraintProvider constraintProvider, bool allowPrereleaseVersions, bool preferListedPackages, DependencyVersion dependencyVersion);

        IEnumerable<IPackage> FindPackages(IEnumerable<string> packageIds);
    }

    public class DependencyResolverFromRepo : IDependencyResolver2
    {
        IPackageRepository _repo;

        public DependencyResolverFromRepo(IPackageRepository repo)
        {
            _repo = repo;
        }

        public IPackage ResolveDependency(PackageDependency dependency, IPackageConstraintProvider constraintProvider, bool allowPrereleaseVersions, bool preferListedPackages, DependencyVersion dependencyVersion)
        {
            IDependencyResolver dependencyResolver = _repo as IDependencyResolver;
            if (dependencyResolver != null)
            {
                return dependencyResolver.ResolveDependency(dependency, constraintProvider, allowPrereleaseVersions, preferListedPackages, dependencyVersion);
            }

            return DependencyResolveUtility.ResolveDependencyCore(
                _repo,
                dependency,
                constraintProvider,
                allowPrereleaseVersions,
                preferListedPackages,
                dependencyVersion);
        }

        public IEnumerable<IPackage> FindPackages(IEnumerable<string> packageIds)
        {
            return packageIds.SelectMany(id => _repo.FindPackagesById(id));
        }
    }
}
