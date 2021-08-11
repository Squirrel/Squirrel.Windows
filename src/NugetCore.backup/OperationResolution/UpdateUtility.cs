using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Resolver;
using NuGet.Resources;

namespace NuGet
{
    public class UpdateUtility
    {
        public ActionResolver Resolver { get; private set; }
        public bool Safe { get; set; }
        public ILogger Logger { get; set; }
        public bool AllowPrereleaseVersions { get; set; }

        public UpdateUtility(ActionResolver resolver)
        {
            Resolver = resolver;
            Logger = NullLogger.Instance;
        }

        public IEnumerable<Resolver.PackageAction> ResolveActionsForUpdate(
            string id, 
            SemanticVersion version,
            IEnumerable<IProjectManager> projectManagers, 
            bool projectNameSpecified)
        {
            if (string.IsNullOrEmpty(id))
            {
                return ResolveActionsToUpdateAllPackages(projectManagers);
            }
            else
            {
                return ResolveActionsToUpdateOnePackage(id, version, projectManagers, projectNameSpecified);
            }
        }

        IEnumerable<Resolver.PackageAction> ResolveActionsToUpdateAllPackages(IEnumerable<IProjectManager> projectManagers)
        {
            // BUGBUG: TargetFramework should be passed for more efficient package walking
            var packageSorter = new PackageSorter(targetFramework: null);
            // Get the packages in reverse dependency order then run update on each one i.e. if A -> B run Update(A) then Update(B)
            var packages = packageSorter.GetPackagesByDependencyOrder(
                projectManagers.First().PackageManager.LocalRepository).Reverse();

            foreach (var projectManager in projectManagers)
            {
                foreach (var package in packages)
                {
                    AddUpdateOperations(
                        package.Id,
                        null,
                        new[] { projectManager });
                }
            }

            var actions = Resolver.ResolveActions();
            return actions;
        }

        // Add update operations to the resolver
        private void AddUpdateOperations(
            string id,
            SemanticVersion version,
            IEnumerable<IProjectManager> projectManagers)
        {
            if (!Safe)
            {
                // Update to latest version                
                foreach (var projectManager in projectManagers)
                {
                    AddUnsafeUpdateOperation(
                        id,
                        version,
                        version != null,
                        projectManager);
                }
            }
            else
            {
                // safe update
                foreach (var projectManager in projectManagers)
                {
                    IPackage installedPackage = projectManager.LocalRepository.FindPackage(id);
                    if (installedPackage == null)
                    {
                        continue;
                    }

                    var safeRange = VersionUtility.GetSafeRange(installedPackage.Version);
                    var package = projectManager.PackageManager.SourceRepository.FindPackage(
                        id,
                        safeRange,
                        projectManager.ConstraintProvider,
                        AllowPrereleaseVersions,
                        allowUnlisted: false);

                    Resolver.AddOperation(PackageAction.Install, package, projectManager);
                }
            }
        }

        void AddUnsafeUpdateOperation(
            string id,
            SemanticVersion version,
            bool targetVersionSetExplicitly,
            IProjectManager projectManager)
        {
            var oldPackage = projectManager.LocalRepository.FindPackage(id);
            if (oldPackage == null)
            {
                return;
            }

            Logger.Log(MessageLevel.Debug, NuGetResources.Debug_LookingForUpdates, id);

            var package = projectManager.PackageManager.SourceRepository.FindPackage(
                id, version,
                projectManager.ConstraintProvider,
                AllowPrereleaseVersions,
                allowUnlisted: false);

            // the condition (allowPrereleaseVersions || targetVersionSetExplicitly || oldPackage.IsReleaseVersion() || !package.IsReleaseVersion() || oldPackage.Version < package.Version)
            // is to fix bug 1574. We want to do nothing if, let's say, you have package 2.0alpha installed, and you do:
            //      update-package
            // without specifying a version explicitly, and the feed only has version 1.0 as the latest stable version.
            if (package != null &&
                oldPackage.Version != package.Version &&
                (AllowPrereleaseVersions || targetVersionSetExplicitly || oldPackage.IsReleaseVersion() || !package.IsReleaseVersion() ||
                oldPackage.Version < package.Version))
            {
                Logger.Log(MessageLevel.Info, NuGetResources.Log_UpdatingPackages,
                    package.Id,
                    oldPackage.Version,
                    package.Version,
                    projectManager.Project.ProjectName);

                Resolver.AddOperation(PackageAction.Install, package, projectManager);
            }

            // Display message that no updates are available.
            IVersionSpec constraint = projectManager.ConstraintProvider.GetConstraint(package.Id);
            if (constraint != null)
            {
                Logger.Log(MessageLevel.Info, NuGetResources.Log_ApplyingConstraints, package.Id, VersionUtility.PrettyPrint(constraint),
                    projectManager.ConstraintProvider.Source);
            }

            Logger.Log(
                MessageLevel.Info,
                NuGetResources.Log_NoUpdatesAvailableForProject,
                package.Id,
                projectManager.Project.ProjectName);
        }

        // Updates the specified package in projects        
        private  IEnumerable<Resolver.PackageAction> ResolveActionsToUpdateOnePackage(string id, SemanticVersion version, IEnumerable<IProjectManager> projectManagers, 
            bool projectNameSpecified)
        {
            var packageManager = projectManagers.First().PackageManager;
            var oldPackage = projectNameSpecified ?
                FindPackageToUpdate(id, version, packageManager, projectManagers.First()) :
                FindPackageToUpdate(id, version, packageManager, projectManagers, Logger);
            if (oldPackage.Item2 == null)
            {
                // we're updating a solution level package
                var package = packageManager.SourceRepository.FindPackage(
                    id, version, AllowPrereleaseVersions, allowUnlisted: false);
                if (package == null)
                {
                    Logger.Log(MessageLevel.Info, "No updates available for {0}", id);
                    return Enumerable.Empty<Resolver.PackageAction>();
                }

                Resolver.AddOperation(PackageAction.Update, package, new NullProjectManager(packageManager));
            }
            else
            {
                AddUpdateOperations(
                    id,
                    version,
                    projectManagers);
            }

            return Resolver.ResolveActions();
        }

        // Find the package that is to be updated when user specifies the project
        public static Tuple<IPackage, IProjectManager> FindPackageToUpdate(
            string id, SemanticVersion version, 
            IPackageManager packageManager,
            IProjectManager projectManager)
        {
            IPackage package = null;

            // Check if the package is installed in the project
            package = projectManager.LocalRepository.FindPackage(id, version: null);
            if (package != null)
            {
                return Tuple.Create(package, projectManager);
            }

            // The package could be a solution level pacakge.
            if (version != null)
            {
                package = packageManager.LocalRepository.FindPackage(id, version);
            }
            else
            {
                // Get all packages by this name to see if we find an ambiguous match
                var packages = packageManager.LocalRepository.FindPackagesById(id).ToList();
                if (packages.Count > 1)
                {
                    if (packages.Any(p => packageManager.IsProjectLevel(p)))
                    {
                        throw new InvalidOperationException(
                            String.Format(CultureInfo.CurrentCulture,
                                "Unknown package in Project {0}: {1}",
                                packages[0].Id,
                                projectManager.Project.ProjectName));
                    }

                    throw new InvalidOperationException(
                            String.Format(CultureInfo.CurrentCulture,
                                "Ambiguous update: {0}",
                                packages[0].Id));
                }

                // Pick the only one of default if none match
                package = packages.SingleOrDefault();
            }

            // Can't find the package in the solution or in the project then fail
            if (package == null)
            {
                throw new InvalidOperationException(
                    String.Format(CultureInfo.CurrentCulture,
                    "Unknown Package: {0}", id));
            }

            bool isProjectLevel = packageManager.IsProjectLevel(package);
            if (isProjectLevel)
            {
                // The package is project level, but it is not referenced by the specified 
                // project. This is an error.
                if (version == null)
                {
                    throw new InvalidOperationException(
                        String.Format(CultureInfo.CurrentCulture,
                        "Unknown package {0} in project {1}",
                        package.Id,
                        projectManager.Project.ProjectName));
                }

                throw new InvalidOperationException(
                    String.Format(CultureInfo.CurrentCulture,
                    "Unknown package {0} in project {1}",
                    package.GetFullName(),
                    projectManager.Project.ProjectName));
            }

            return Tuple.Create<IPackage, IProjectManager>(package, null);
        }

        // Find the package that is to be updated.
        public static Tuple<IPackage, IProjectManager> FindPackageToUpdate(
            string id, SemanticVersion version, 
            IPackageManager packageManager,        
            IEnumerable<IProjectManager> projectManagers,
            ILogger logger)
        {
            IPackage package = null;

            // Check if the package is installed in a project
            foreach (var projectManager in projectManagers)
            {
                package = projectManager.LocalRepository.FindPackage(id, version: null);
                if (package != null)
                {
                    return Tuple.Create(package, projectManager);
                }
            }

            // Check if the package is a solution level package
            if (version != null)
            {
                package = packageManager.LocalRepository.FindPackage(id, version);
            }
            else
            {
                // Get all packages by this name to see if we find an ambiguous match
                var packages = packageManager.LocalRepository.FindPackagesById(id).ToList();
                foreach (var p in packages)
                {
                    bool isProjectLevel = packageManager.IsProjectLevel(p);

                    if (!isProjectLevel)
                    {
                        if (packages.Count > 1)
                        {
                            throw new InvalidOperationException(
                                String.Format(CultureInfo.CurrentCulture,
                                    "Ambiguous update: {0}",
                                    id));
                        }

                        package = p;
                        break;
                    }
                    else
                    {
                        if (!packageManager.LocalRepository.IsReferenced(p.Id, p.Version))
                        {
                            logger.Log(MessageLevel.Warning, String.Format(CultureInfo.CurrentCulture,
                                "Package not referenced by any project {0}, {1}", p.Id, p.Version));

                            // Try next package
                            continue;
                        }

                        // Found a package with package Id as 'id' which is installed in at least 1 project
                        package = p;
                        break;
                    }
                }

                if (package == null)
                {
                    // There are one or more packages with package Id as 'id'
                    // BUT, none of them is installed in a project
                    // it's probably a borked install.
                    throw new PackageNotInstalledException(
                        String.Format(CultureInfo.CurrentCulture,
                        "Package not installed in any project: {0}", id));
                }
            }

            if (package == null)
            {
                throw new InvalidOperationException(
                    String.Format(CultureInfo.CurrentCulture,
                    "Unknown Package: {0}", id));
            }

            return Tuple.Create<IPackage, IProjectManager>(package, null);
        }
    }
}
