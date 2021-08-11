using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.Resources;

namespace NuGet
{
    public class InstallWalker : PackageWalker, IPackageOperationResolver
    {
        private readonly bool _ignoreDependencies;
        private bool _allowPrereleaseVersions;
        private readonly OperationLookup _operations;
        private bool _isDowngrade;

        // This acts as a "retainment" queue. It contains packages that are already installed but need to be kept during 
        // a package walk. This is to prevent those from being uninstalled in subsequent encounters.
        private readonly HashSet<IPackage> _packagesToKeep = new HashSet<IPackage>(PackageEqualityComparer.IdAndVersion);
        private IDictionary<string, IList<IPackage>> _packagesByDependencyOrder;

        // this ctor is used for unit tests
        internal InstallWalker(IPackageRepository localRepository,
                               IDependencyResolver2 dependencyResolver,
                               ILogger logger,
                               bool ignoreDependencies,
                               bool allowPrereleaseVersions,
                               DependencyVersion dependencyVersion)
            : this(localRepository, dependencyResolver, null, logger, ignoreDependencies, allowPrereleaseVersions, dependencyVersion)
        {
        }

        public InstallWalker(IPackageRepository localRepository,
                             IDependencyResolver2 dependencyResolver,
                             FrameworkName targetFramework,
                             ILogger logger,
                             bool ignoreDependencies,
                             bool allowPrereleaseVersions,
                             DependencyVersion dependencyVersion) :
            this(localRepository,
                 dependencyResolver,
                 constraintProvider: NullConstraintProvider.Instance,
                 targetFramework: targetFramework,
                 logger: logger,
                 ignoreDependencies: ignoreDependencies,
                 allowPrereleaseVersions: allowPrereleaseVersions,
                 dependencyVersion: dependencyVersion)
        {
        }
        
        public InstallWalker(IPackageRepository localRepository,
                             IDependencyResolver2 dependencyResolver,
                             IPackageConstraintProvider constraintProvider,
                             FrameworkName targetFramework,
                             ILogger logger,
                             bool ignoreDependencies,
                             bool allowPrereleaseVersions,
                             DependencyVersion dependencyVersion)
            : base(targetFramework)
        {

            if (dependencyResolver == null)
            {
                throw new ArgumentNullException("dependencyResolver");
            }
            if (localRepository == null)
            {
                throw new ArgumentNullException("localRepository");
            }
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            Repository = localRepository;
            Logger = logger;
            DependencyResolver = dependencyResolver;
            _ignoreDependencies = ignoreDependencies;
            ConstraintProvider = constraintProvider;
            _operations = new OperationLookup();
            _allowPrereleaseVersions = allowPrereleaseVersions;
            DependencyVersion = dependencyVersion;
            CheckDowngrade = true;
        }

        internal bool DisableWalkInfo
        { 
            get; 
            set; 
        }
        
        /// <summary>
        /// Indicates if this object checks the downgrade case. 
        /// </summary>
        /// <remarks>
        /// Currently there is a concurrent issue: if there are multiple "nuget.exe install" running
        /// concurrently, then checking local repository for existing packages to see
        /// if current install is downgrade can generate file in use exception.
        /// This property is a temporary workaround: it is set to false when 
        /// this object is called by "nuget.exe install/restore".
        /// </remarks>        
        internal bool CheckDowngrade
        {
            get;
            set;
        }

        protected override bool IgnoreWalkInfo
        {
            get
            {
                return DisableWalkInfo ? true : base.IgnoreWalkInfo;
            }
        }

        protected ILogger Logger
        {
            get;
            private set;
        }

        protected IPackageRepository Repository
        {
            get;
            private set;
        }

        protected override bool IgnoreDependencies
        {
            get
            {
                return _ignoreDependencies;
            }
        }

        protected override bool AllowPrereleaseVersions
        {
            get
            {
                return _allowPrereleaseVersions;
            }
        }

        protected IDependencyResolver2 DependencyResolver
        {
            get;
            private set;
        }

        private IPackageConstraintProvider ConstraintProvider { get; set; }

        protected IList<PackageOperation> Operations
        {
            get
            {
                return _operations.ToList();
            }
        }

        protected virtual ConflictResult GetConflict(IPackage package)
        {
            var conflictingPackage = Marker.FindPackage(package.Id);
            if (conflictingPackage != null)
            {
                return new ConflictResult(conflictingPackage, Marker, Marker);
            }
            return null;
        }

        protected override void OnBeforePackageWalk(IPackage package)
        {
            ConflictResult conflictResult = GetConflict(package);

            if (conflictResult == null)
            {
                return;
            }

            // If the conflicting package is the same as the package being installed
            // then no-op
            if (PackageEqualityComparer.IdAndVersion.Equals(package, conflictResult.Package))
            {
                return;
            }

            // First we get a list of dependents for the installed package.
            // Then we find the dependency in the foreach dependent that this installed package used to satisfy.
            // We then check if the resolved package also meets that dependency and if it doesn't it's added to the list
            // i.e. A1 -> C >= 1
            //      B1 -> C >= 1
            //      C2 -> []
            // Given the above graph, if we upgrade from C1 to C2, we need to see if A and B can work with the new C
            var incompatiblePackages = from dependentPackage in GetDependents(conflictResult)
                                       let dependency = dependentPackage.FindDependency(package.Id, TargetFramework)
                                       where dependency != null && !dependency.VersionSpec.Satisfies(package.Version)
                                       select dependentPackage;

            // If there were incompatible packages that we failed to update then we throw an exception
            if (incompatiblePackages.Any() && !TryUpdate(incompatiblePackages, conflictResult, package, out incompatiblePackages))
            {
                throw CreatePackageConflictException(package, conflictResult.Package, incompatiblePackages);
            }
            else 
            {
                if (!_isDowngrade && (package.Version < conflictResult.Package.Version))
                {
                    throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture,
                       NuGetResources.NewerVersionAlreadyReferenced, package.Id));
                }
                Uninstall(conflictResult.Package, conflictResult.DependentsResolver, conflictResult.Repository);
            }
        }

        private void Uninstall(IPackage package, IDependentsResolver dependentsResolver, IPackageRepository repository)
        {
            // If we explicitly want to uninstall this package, then remove it from the retainment queue.
            _packagesToKeep.Remove(package);

            // If this package isn't part of the current graph (i.e. hasn't been visited yet) and
            // is marked for removal, then do nothing. This is so we don't get unnecessary duplicates.
            if (!Marker.Contains(package) && _operations.Contains(package, PackageAction.Uninstall))
            {
                return;
            }

            // Uninstall the conflicting package. We set throw on conflicts to false since we've
            // already decided that there were no conflicts based on the above code.
            var resolver = new UninstallWalker(
                repository,
                dependentsResolver,
                TargetFramework,
                NullLogger.Instance,
                removeDependencies: !IgnoreDependencies,
                forceRemove: false) 
                {
                    DisableWalkInfo = this.DisableWalkInfo,
                    ThrowOnConflicts = false 
                };

            foreach (var operation in resolver.ResolveOperations(package))
            {
                // If the operation is Uninstall, we don't want to uninstall the package if it is in the "retainment" queue.
                if (operation.Action == PackageAction.Install || !_packagesToKeep.Contains(operation.Package))
                {
                    _operations.AddOperation(operation);
                }
            }
        }

        private IPackage SelectDependency(IEnumerable<IPackage> dependencies)
        {
            return dependencies.SelectDependency(DependencyVersion);
        }

        private static IEnumerable<IPackage> FindCompatiblePackages(
            IDependencyResolver2 dependencyResolver,
            IPackageConstraintProvider constraintProvider,
            IEnumerable<string> packageIds,
            IPackage package,
            FrameworkName targetFramework,
            bool allowPrereleaseVersions)
        {
            return (from p in dependencyResolver.FindPackages(packageIds)
                    where allowPrereleaseVersions || p.IsReleaseVersion()
                    let dependency = p.FindDependency(package.Id, targetFramework)
                    let otherConstaint = constraintProvider.GetConstraint(p.Id)
                    where dependency != null &&
                          dependency.VersionSpec.Satisfies(package.Version) &&
                          (otherConstaint == null || otherConstaint.Satisfies(package.Version))
                    select p);
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We re-throw a more specific exception later on")]
        private bool TryUpdate(IEnumerable<IPackage> dependents, ConflictResult conflictResult, IPackage package, out IEnumerable<IPackage> incompatiblePackages)
        {
            // Key dependents by id so we can look up the old package later
            var dependentsLookup = dependents.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
            var compatiblePackages = new Dictionary<IPackage, IPackage>();

            // Initialize each compatible package to null
            foreach (var dependent in dependents)
            {
                compatiblePackages[dependent] = null;
            }

            // Get compatible packages in one batch so we don't have to make requests for each one
            var packages = from p in FindCompatiblePackages(
                           DependencyResolver,
                           ConstraintProvider, dependentsLookup.Keys, package, TargetFramework, AllowPrereleaseVersions)
                           group p by p.Id into g
                           let oldPackage = dependentsLookup[g.Key]
                           select new
                           {
                               OldPackage = oldPackage,
                               NewPackage = SelectDependency(g.Where(p => p.Version > oldPackage.Version)
                                   .OrderBy(p => p.Version))
                           };

            foreach (var p in packages)
            {
                compatiblePackages[p.OldPackage] = p.NewPackage;
            }

            // Get all packages that have an incompatibility with the specified package i.e.
            // We couldn't find a version in the repository that works with the specified package.
            incompatiblePackages = compatiblePackages.Where(p => p.Value == null)
                                                     .Select(p => p.Key);

            if (incompatiblePackages.Any())
            {
                return false;
            }

            IPackageConstraintProvider currentConstraintProvider = ConstraintProvider;

            try
            {
                // Add a constraint for the incoming package so we don't try to update it by mistake.
                // Scenario:
                // A 1.0 -> B [1.0]
                // B 1.0.1, B 1.5, B 2.0
                // A 2.0 -> B (any version)
                // We have A 1.0 and B 1.0 installed. When trying to update to B 1.0.1, we'll end up trying
                // to find a version of A that works with B 1.0.1. The version in the above case is A 2.0.
                // When we go to install A 2.0 we need to make sure that when we resolve it's dependencies that we stay within bounds
                // i.e. when we resolve B for A 2.0 we want to keep the B 1.0.1 we've already chosen instead of trying to grab
                // B 1.5 or B 2.0. In order to achieve this, we add a constraint for version of B 1.0.1 so we stay within those bounds for B.

                // Respect all existing constraints plus an additional one that we specify based on the incoming package
                var constraintProvider = new DefaultConstraintProvider();
                constraintProvider.AddConstraint(package.Id, new VersionSpec(package.Version));
                ConstraintProvider = new AggregateConstraintProvider(ConstraintProvider, constraintProvider);

                // Mark the incoming package as visited so that we don't try walking the graph again
                Marker.MarkVisited(package);

                var failedPackages = new List<IPackage>();
                // Update each of the existing packages to more compatible one
                foreach (var pair in compatiblePackages)
                {
                    try
                    {
                        //
                        // BUGBUG: What if the new package required license acceptance but the new dependency package did not?!
                        //         When a dependency package that does not require license acceptance, like jQuery 1.7.1.1,
                        //         is being updated to a version incompatible with its dependents, like jQuery 2.0.3 and its dependent Microsoft.jQuery.Unobtrusive.Ajax 2.0.20710.0
                        //         Then, the dependent package is updated such that the new dependent package is compatible with the new dependency package
                        //         In the example above, Microsoft.jQuery.Unobtrusive.Ajax 2.0.20710.0 will be updated to 2.0.30506.0. 2.0.30506.0 requires license acceptance
                        //         But, the update happens anyways, just because, the user chose to update jQuery
                        //
                        // Remove the old package
                        Uninstall(pair.Key, conflictResult.DependentsResolver, conflictResult.Repository);

                        // Install the new package
                        Walk(pair.Value);
                    }
                    catch
                    {
                        // If we failed to update this package (most likely because of a conflict further up the dependency chain)
                        // we keep track of it so we can report an error about the top level package.
                        failedPackages.Add(pair.Key);
                    }
                }

                incompatiblePackages = failedPackages;

                return !incompatiblePackages.Any();
            }
            finally
            {
                // Restore the current constraint provider
                ConstraintProvider = currentConstraintProvider;

                // Mark the package as processing again
                Marker.MarkProcessing(package);
            }
        }

        protected override void OnAfterPackageWalk(IPackage package)
        {   
            if (!Repository.Exists(package))
            {
                // Don't add the package for installation if it already exists in the repository
                var operation = new PackageOperation(package, PackageAction.Install);
                var packageTarget = GetPackageTarget(package);
                if (packageTarget == PackageTargets.External)
                {
                    operation.Target = PackageOperationTarget.PackagesFolder;
                };
                _operations.AddOperation(operation);
            }
            else
            {
                // If we already added an entry for removing this package then remove it 
                // (it's equivalent for doing +P since we're removing a -P from the list)
                _operations.RemoveOperation(package, PackageAction.Uninstall);

                // and mark the package as being "retained".
                _packagesToKeep.Add(package);
            }

            if (_packagesByDependencyOrder != null)
            {
                IList<IPackage> packages;
                if (!_packagesByDependencyOrder.TryGetValue(package.Id, out packages))
                {
                    _packagesByDependencyOrder[package.Id] = packages = new List<IPackage>();
                }

                packages.Add(package);
            }
        }

        protected override IPackage ResolveDependency(PackageDependency dependency)
        {
            Logger.Log(MessageLevel.Info, NuGetResources.Log_AttemptingToRetrievePackageFromSource, dependency);

            // First try to get a local copy of the package
            // Bug1638: Include prereleases when resolving locally installed dependencies.
            //Prerelease is included when we try to look at the local repository. 
            //In case of downgrade, we are going to look only at source repo and not local. 
            //That way we will downgrade dependencies when parent package is downgraded.
            if (!_isDowngrade)
            {
                IPackage package = DependencyResolveUtility.ResolveDependency(Repository, dependency, ConstraintProvider, allowPrereleaseVersions: true, preferListedPackages: false, dependencyVersion: DependencyVersion);
                if (package != null)
                {
                    return package;
                } 
            }

            // Next, query the source repo for the same dependency
            IPackage sourcePackage = DependencyResolver.ResolveDependency(dependency, ConstraintProvider, AllowPrereleaseVersions, preferListedPackages: true, dependencyVersion: DependencyVersion);
            return sourcePackage;
        }

        protected override void OnDependencyResolveError(PackageDependency dependency)
        {
            IVersionSpec spec = ConstraintProvider.GetConstraint(dependency.Id);

            string message = String.Empty;
            if (spec != null)
            {
                message = String.Format(CultureInfo.CurrentCulture, NuGetResources.AdditonalConstraintsDefined, dependency.Id, VersionUtility.PrettyPrint(spec), ConstraintProvider.Source);
            }

            throw new InvalidOperationException(
                String.Format(CultureInfo.CurrentCulture,
                NuGetResources.UnableToResolveDependency + message, dependency));
        }

        public IEnumerable<PackageOperation> ResolveOperations(IPackage package)
        {
            // The cases when we don't check downgrade is when this object is 
            // called to restore packages, e.g. by nuget.exe restore command.
            // Otherwise, check downgrade is true, e.g. when user installs a package
            // inside VS.
            if (CheckDowngrade)
            {
                //Check if the package is installed. This is necessary to know if this is a fresh-install or a downgrade operation
                IPackage packageUnderInstallation = Repository.FindPackage(package.Id);
                if (packageUnderInstallation != null && packageUnderInstallation.Version > package.Version)
                {
                    _isDowngrade = true;
                }
            }
            else
            {
                _isDowngrade = false;
            }

            _operations.Clear();
            Marker.Clear();
            _packagesToKeep.Clear();
            
            Walk(package);
            return Operations.Reduce();
        }

        /// <summary>
        /// Resolve operations for a list of packages clearing the package marker only once at the beginning. When the packages are interdependent, this method performs efficiently
        /// Also, sets the packagesByDependencyOrder to the input packages, but in dependency order
        /// NOTE: If package A 1.0 depends on package B 1.0 and A 2.0 does not depend on B 2.0; and, A 2.0 and B 2.0 are the input packages (likely from the updates tab in dialog)
        ///       then, the packagesbyDependencyOrder will have A followed by B. Since, A 2.0 does not depend on B 2.0. This is also true because GetConflict in this class
        ///       would only the PackageMarker and not the installed packages for information
        /// </summary>
        /// <param name="packages">The list of packages to resolve operations for. If from the dialog node, the list may be sorted, mostly, alphabetically</param>
        /// <param name="packagesByDependencyOrder">Same set of packages returned in the dependency order</param>
        /// <param name="allowPrereleaseVersionsBasedOnPackage">If true, allowPrereleaseVersion is determined based on package before walking that package. Otherwise, existing value is used</param>
        /// <returns>
        /// Returns a list of Package Operations to be performed for the installation of the packages passed
        /// Also, the out parameter packagesByDependencyOrder would returned the packages passed in the dependency order
        /// </returns>
        [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "1#", Justification = "In addition to operations, need to return packagesByDependencyOrder.")]
        public IList<PackageOperation> ResolveOperations(IEnumerable<IPackage> packages, out IList<IPackage> packagesByDependencyOrder, bool allowPrereleaseVersionsBasedOnPackage = false)
        {
            _packagesByDependencyOrder = new Dictionary<string, IList<IPackage>>();
            _operations.Clear();
            Marker.Clear();
            _packagesToKeep.Clear();

            Debug.Assert(Operations is List<PackageOperation>);
            foreach (var package in packages)
            {
                if (!_operations.Contains(package, PackageAction.Install))
                {
                    var allowPrereleaseVersions = _allowPrereleaseVersions;
                    try
                    {
                        if (allowPrereleaseVersionsBasedOnPackage)
                        {
                            // Update _allowPrereleaseVersions before walking a package if allowPrereleaseVersionsBasedOnPackage is set to true
                            // This is mainly used when bulk resolving operations for reinstalling packages
                            _allowPrereleaseVersions = _allowPrereleaseVersions || !package.IsReleaseVersion();
                        }
                        Walk(package);
                    }
                    finally
                    {
                        _allowPrereleaseVersions = allowPrereleaseVersions;
                    }
                }
            }

            // Flatten the dictionary to create a list of all the packages. Only this item the packages visited first during the walk will appear on the list. Also, only retain distinct elements
            IEnumerable<IPackage> allPackagesByDependencyOrder = _packagesByDependencyOrder.SelectMany(p => p.Value).Distinct();

            // Only retain the packages for which the operations are being resolved for
            packagesByDependencyOrder = allPackagesByDependencyOrder.Where(p => packages.Any(q => p.Id == q.Id && p.Version == q.Version)).ToList();
            Debug.Assert(packagesByDependencyOrder.Count == packages.Count());

            _packagesByDependencyOrder.Clear();
            _packagesByDependencyOrder = null;

            return Operations.Reduce();
        }

        private IEnumerable<IPackage> GetDependents(ConflictResult conflict)
        {
            // Skip all dependents that are marked for uninstall
            IEnumerable<IPackage> packages = _operations.GetPackages(PackageAction.Uninstall);

            return conflict.DependentsResolver.GetDependents(conflict.Package)
                                              .Except<IPackage>(packages, PackageEqualityComparer.IdAndVersion);
        }

        private static InvalidOperationException CreatePackageConflictException(IPackage resolvedPackage, IPackage package, IEnumerable<IPackage> dependents)
        {
            if (dependents.Count() == 1)
            {
                return new InvalidOperationException(String.Format(CultureInfo.CurrentCulture,
                       NuGetResources.ConflictErrorWithDependent, package.GetFullName(), resolvedPackage.GetFullName(), dependents.Single().Id));
            }

            return new InvalidOperationException(String.Format(CultureInfo.CurrentCulture,
                        NuGetResources.ConflictErrorWithDependents, package.GetFullName(), resolvedPackage.GetFullName(), String.Join(", ",
                        dependents.Select(d => d.Id))));
        }

        /// <summary>
        /// Operation lookup encapsulates an operation list and another efficient data structure for finding package operations
        /// by package id, version and PackageAction.
        /// </summary>
        private class OperationLookup
        {
            private readonly List<PackageOperation> _operations = new List<PackageOperation>();
            private readonly Dictionary<PackageAction, Dictionary<IPackage, PackageOperation>> _operationLookup = new Dictionary<PackageAction, Dictionary<IPackage, PackageOperation>>();

            internal void Clear()
            {
                _operations.Clear();
                _operationLookup.Clear();
            }

            internal IList<PackageOperation> ToList()
            {
                return _operations;
            }

            internal IEnumerable<IPackage> GetPackages(PackageAction action)
            {
                Dictionary<IPackage, PackageOperation> dictionary = GetPackageLookup(action);
                if (dictionary != null)
                {
                    return dictionary.Keys;
                }
                return Enumerable.Empty<IPackage>();
            }

            internal void AddOperation(PackageOperation operation)
            {
                Dictionary<IPackage, PackageOperation> dictionary = GetPackageLookup(operation.Action, createIfNotExists: true);
                if (!dictionary.ContainsKey(operation.Package))
                {
                    dictionary.Add(operation.Package, operation);
                    _operations.Add(operation);
                }
            }

            internal void RemoveOperation(IPackage package, PackageAction action)
            {
                Dictionary<IPackage, PackageOperation> dictionary = GetPackageLookup(action);
                PackageOperation operation;
                if (dictionary != null && dictionary.TryGetValue(package, out operation))
                {
                    dictionary.Remove(package);
                    _operations.Remove(operation);
                }
            }

            internal bool Contains(IPackage package, PackageAction action)
            {
                Dictionary<IPackage, PackageOperation> dictionary = GetPackageLookup(action);
                return dictionary != null && dictionary.ContainsKey(package);
            }

            private Dictionary<IPackage, PackageOperation> GetPackageLookup(PackageAction action, bool createIfNotExists = false)
            {
                Dictionary<IPackage, PackageOperation> packages;
                if (!_operationLookup.TryGetValue(action, out packages) && createIfNotExists)
                {
                    packages = new Dictionary<IPackage, PackageOperation>(PackageEqualityComparer.IdAndVersion);
                    _operationLookup.Add(action, packages);
                }
                return packages;
            }
        }
    }
}