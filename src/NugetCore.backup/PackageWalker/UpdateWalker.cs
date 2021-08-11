using System.Runtime.Versioning;

namespace NuGet
{
    public class UpdateWalker : InstallWalker
    {
        private readonly IDependentsResolver _dependentsResolver;

        // this ctor is used for unit tests
        internal UpdateWalker(IPackageRepository localRepository,
                            IDependencyResolver2 sourceRepository,
                            IDependentsResolver dependentsResolver,
                            IPackageConstraintProvider constraintProvider,
                            ILogger logger,
                            bool updateDependencies,
                            bool allowPrereleaseVersions)
            : this(localRepository, sourceRepository, dependentsResolver, constraintProvider, null, logger, updateDependencies, allowPrereleaseVersions)
        {
        }

        public UpdateWalker(IPackageRepository localRepository,
                            IDependencyResolver2 sourceRepository,
                            IDependentsResolver dependentsResolver,
                            IPackageConstraintProvider constraintProvider,
                            FrameworkName targetFramework, 
                            ILogger logger,
                            bool updateDependencies,
                            bool allowPrereleaseVersions)
            : base(localRepository, sourceRepository, constraintProvider, targetFramework, logger, !updateDependencies, allowPrereleaseVersions, DependencyVersion.Lowest)
        {
            _dependentsResolver = dependentsResolver;
            AcceptedTargets = PackageTargets.All;
        }

        public PackageTargets AcceptedTargets { get; set; }

        protected override ConflictResult GetConflict(IPackage package)
        {
            // For project installs we first try to base behavior (using the live graph)
            // then we look for conflicts for packages installed into the current project.
            ConflictResult result = base.GetConflict(package);

            if (result == null)
            {
                IPackage existingPackage = Repository.FindPackage(package.Id);

                if (existingPackage != null)
                {
                    result = new ConflictResult(existingPackage, Repository, _dependentsResolver);
                }
            }
            return result;
        }

        protected override void OnAfterPackageWalk(IPackage package)
        {
            if (DisableWalkInfo)
            {
                base.OnAfterPackageWalk(package);
            }
            else
            {
                PackageWalkInfo info = GetPackageInfo(package);

                if (AcceptedTargets.HasFlag(info.Target))
                {
                    base.OnAfterPackageWalk(package);
                }
            }
        }
    }
}
