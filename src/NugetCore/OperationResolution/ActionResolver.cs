using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if VS14
using Microsoft.VisualStudio.ProjectSystem.Interop;
#endif

namespace NuGet.Resolver
{
    public class ActionResolver
    {
        public DependencyVersion DependencyVersion { get; set; }

        // used by install & update 
        public bool IgnoreDependencies { get; set; }

        public bool AllowPrereleaseVersions { get; set; }

        public bool ForceRemove { get; set; }

        // used by uninstall
        public bool RemoveDependencies { get; set; }

        public ILogger Logger { get; set; }

        public ActionResolver()
        {   
            Logger = NullLogger.Instance;
            DependencyVersion = DependencyVersion.Lowest;
            _operations = new List<Operation>();
        }

        private class Operation
        {
            public NuGet.PackageAction OperationType { get; set; }
            public IPackage Package { get; set; }
            public IProjectManager ProjectManager { get; set; }
        }

        private List<Operation> _operations;
        private Dictionary<IProjectManager, VirtualRepository> _virtualProjectRepos;
        private Dictionary<IPackageManager, VirtualRepository> _virtualPackageRepos;
        private Dictionary<IPackageManager, Dictionary<IPackage, int>> _packageRefCounts;

        public void AddOperation(NuGet.PackageAction operationType, IPackage package, IProjectManager projectManager)
        {
            _operations.Add(new Operation()
            {
                OperationType = operationType,
                Package = package,
                ProjectManager = projectManager
            });
        }

        public IEnumerable<PackageAction> ResolveActions()
        {
            InitilizeVirtualRepos();
            InitializeRefCount();

            List<PackageAction> actions = new List<PackageAction>();
            foreach (var operation in _operations)
            {
                actions.AddRange(ResolveActionsForOperation(operation));                
            }

            return actions;
        }

        // create virtual repos
        private void InitilizeVirtualRepos()
        {   
            _virtualProjectRepos = new Dictionary<IProjectManager, VirtualRepository>();
            _virtualPackageRepos = new Dictionary<IPackageManager, VirtualRepository>();
            foreach (var operation in _operations)
            {
                if (!_virtualProjectRepos.ContainsKey(operation.ProjectManager))
                {
                    _virtualProjectRepos.Add(
                        operation.ProjectManager,
                        new VirtualRepository(operation.ProjectManager.LocalRepository));
                }

                var packageManager = operation.ProjectManager.PackageManager;
                if (!_virtualPackageRepos.ContainsKey(packageManager))
                {
                    _virtualPackageRepos.Add(
                        packageManager,
                        new VirtualRepository(packageManager.LocalRepository));
                }
            }
        }

        // Initialize _packageRefCounts
        private void InitializeRefCount()
        {
            // calculate package ref count   
            _packageRefCounts = new Dictionary<IPackageManager, Dictionary<IPackage, int>>();
            foreach (var packageManager in _operations.Select(op => op.ProjectManager.PackageManager))
            {
                var refCount = new Dictionary<IPackage, int>(PackageEqualityComparer.IdAndVersion);
                foreach (var package in packageManager.LocalRepository.GetPackages())
                {
                    refCount[package] = 0;
                }

                // calculate ref count of project level packages
                foreach (var repo in packageManager.LocalRepository.LoadProjectRepositories())
                {
                    foreach (var p in repo.GetPackages())
                    {
                        if (refCount.ContainsKey(p))
                        {
                            refCount[p]++;
                        }
                        else
                        {
                            refCount[p] = 1;
                        }
                    }
                }

                // any packages that are not referenced by projects are considered to be 
                // solution level packages, with ref count 1.
                foreach (var package in packageManager.LocalRepository.GetPackages())
                {
                    if (refCount[package] == 0)
                    {
                        refCount[package] = 1;
                    }
                }

                _packageRefCounts[packageManager] = refCount;
            }
        }

        private IEnumerable<PackageOperation> ResolveOperationsToInstallProjectLevelPackage(Operation operation)
        {
            var projectRepo = _virtualProjectRepos[operation.ProjectManager];
            var dependentsWalker = new DependentsWalker(
                operation.ProjectManager.PackageManager.LocalRepository,
                operation.ProjectManager.GetTargetFrameworkForPackage(operation.Package.Id))
            {
                DependencyVersion = DependencyVersion
            };
            var updateWalker = new UpdateWalker(
                projectRepo,
                operation.ProjectManager.PackageManager.DependencyResolver,
                dependentsWalker,
                operation.ProjectManager.ConstraintProvider,
                operation.ProjectManager.Project.TargetFramework,
                Logger ?? NullLogger.Instance,
                !IgnoreDependencies,
                AllowPrereleaseVersions)
            {
                AcceptedTargets = PackageTargets.All,
                DependencyVersion = DependencyVersion
            };

            var operations = updateWalker.ResolveOperations(operation.Package);
            return operations;
        }

        private IEnumerable<PackageOperation> ResolveOperationsToUninstallProjectLevelPackage(Operation operation)
        {
            var projectRepo = _virtualProjectRepos[operation.ProjectManager];
            var targetFramework = operation.ProjectManager.GetTargetFrameworkForPackage(operation.Package.Id);
            var resolver = new UninstallWalker(
                projectRepo,
                new DependentsWalker(projectRepo, targetFramework),
                targetFramework,
                NullLogger.Instance,
                RemoveDependencies,
                ForceRemove);
            var operations = resolver.ResolveOperations(operation.Package);
            return operations;
        }

        private IEnumerable<PackageOperation> ResolveOperationsToUninstallSolutionLevelPackage(Operation operation)
        {
            var repo = _virtualPackageRepos[operation.ProjectManager.PackageManager];
            var resolver = new UninstallWalker(
                repo,
                new DependentsWalker(
                    operation.ProjectManager.PackageManager.LocalRepository,
                    targetFramework: null),
                targetFramework: null,
                logger: NullLogger.Instance,
                removeDependencies: RemoveDependencies,
                forceRemove: ForceRemove);
            var operations = resolver.ResolveOperations(operation.Package);

            // we're uninstalling solution level packages, so all target should be
            // set to PackagesFolder.
            foreach (var op in operations)
            {
                op.Target = PackageOperationTarget.PackagesFolder;
            }

            return operations;
        }

        private IEnumerable<PackageOperation> ResolveOperationsToInstallSolutionLevelPackage(Operation operation)
        {
            var repo = _virtualPackageRepos[operation.ProjectManager.PackageManager];
            var installWalker = new InstallWalker(
                repo,
                operation.ProjectManager.PackageManager.DependencyResolver,
                targetFramework: null,
                logger: Logger,
                ignoreDependencies: IgnoreDependencies,
                allowPrereleaseVersions: AllowPrereleaseVersions,
                dependencyVersion: DependencyVersion);
            var operations = installWalker.ResolveOperations(operation.Package);

            // we're installing solution level packages, so all target should be
            // set to PackagesFolder.
            foreach (var op in operations)
            {
                op.Target = PackageOperationTarget.PackagesFolder;
            }

            return operations;
        }

        private IEnumerable<PackageOperation> ResolveOperationsToUpdateSolutionLevelPackage(Operation operation)
        {
            var repo = _virtualPackageRepos[operation.ProjectManager.PackageManager];            
            var updateWalker = new UpdateWalker(
                repo,
                operation.ProjectManager.PackageManager.DependencyResolver,
                new DependentsWalker(repo, targetFramework: null)
                {
                    DependencyVersion = DependencyVersion
                },
                constraintProvider: NullConstraintProvider.Instance,
                targetFramework: null,
                logger: Logger ?? NullLogger.Instance,
                updateDependencies: !IgnoreDependencies,
                allowPrereleaseVersions: AllowPrereleaseVersions)
                {
                    AcceptedTargets = PackageTargets.All,
                    DependencyVersion = DependencyVersion
                };

            var operations = updateWalker.ResolveOperations(operation.Package);

            // we're updating solution level packages, so all target should be
            // set to PackagesFolder.
            foreach (var op in operations)
            {
                op.Target = PackageOperationTarget.PackagesFolder;
            }

            return operations;
        }

        private IEnumerable<PackageAction> ResolveActionsForOperation(Operation operation)
        {
            IEnumerable<PackageOperation> projectOperations = Enumerable.Empty<PackageOperation>();
#if VS14
            if (operation.ProjectManager.Project is INuGetPackageManager)
            {
                var action = operation.OperationType == NuGet.PackageAction.Install ?
                    PackageActionType.Install :
                    PackageActionType.Uninstall;

                return new[] {
                    new PackageProjectAction(
                        action,
                        operation.Package,
                        operation.ProjectManager)
                };
            }
#endif
            bool isProjectLevel = operation.ProjectManager.PackageManager.IsProjectLevel(operation.Package);
            if (operation.OperationType == NuGet.PackageAction.Install)
            {
                if (isProjectLevel)
                {
                    projectOperations = ResolveOperationsToInstallProjectLevelPackage(operation);
                }
                else
                {
                    projectOperations = ResolveOperationsToInstallSolutionLevelPackage(operation);
                }
            }
            else if (operation.OperationType == NuGet.PackageAction.Update)
            {
                if (isProjectLevel)
                {
                    // For project level packages, Update is the same as Install.
                    projectOperations = ResolveOperationsToInstallProjectLevelPackage(operation);
                }
                else
                {
                    projectOperations = ResolveOperationsToUpdateSolutionLevelPackage(operation);
                }
            }
            else 
            {
                if (isProjectLevel)
                {
                    projectOperations = ResolveOperationsToUninstallProjectLevelPackage(operation);
                }
                else
                {
                    projectOperations = ResolveOperationsToUninstallSolutionLevelPackage(operation);
                }
            }

            var actions = new List<PackageAction>();
            foreach (var op in projectOperations)
            {
                var actionType = op.Action == 
                    NuGet.PackageAction.Install ? 
                    PackageActionType.Install :
                    PackageActionType.Uninstall;

                if (op.Target == PackageOperationTarget.Project)
                {   
                    actions.Add(new PackageProjectAction(
                        actionType,
                        op.Package,
                        operation.ProjectManager));
                }
                else 
                {
                    actions.Add(new PackageSolutionAction(
                        actionType,
                        op.Package,
                        operation.ProjectManager.PackageManager));
                }
            }
            
            var finalActions = ResolveFinalActions(operation.ProjectManager.PackageManager, actions);
            UpdateVirtualRepos(finalActions);
            return finalActions;
        }

        private void UpdateVirtualRepos(IList<PackageAction> actions)
        {
            foreach (var action in actions)
            {
                PackageProjectAction projectAction = action as PackageProjectAction;
                if (projectAction == null)
                {
                    // update the virtual packages folder repo
                    PackageSolutionAction solutionAction = (PackageSolutionAction)action;
                    var packageRepo = _virtualPackageRepos[solutionAction.PackageManager];
                    if (solutionAction.ActionType == PackageActionType.AddToPackagesFolder)
                    {
                        packageRepo.AddPackage(solutionAction.Package);
                    }
                    else if (solutionAction.ActionType == PackageActionType.DeleteFromPackagesFolder)
                    {
                        packageRepo.RemovePackage(solutionAction.Package);
                    }
                }
                else
                {
                    // update the virtual project repo
                    var projectRepo = _virtualProjectRepos[projectAction.ProjectManager];
                    if (projectAction.ActionType == PackageActionType.Install)
                    {
                        projectRepo.AddPackage(action.Package);
                    }
                    else
                    {
                        projectRepo.RemovePackage(action.Package);
                    }
                }
            }
        }

        private IList<PackageAction> ResolveFinalActions(
            IPackageManager packageManager,
            IEnumerable<PackageAction> projectActions)
        {
            var packageRefCount = _packageRefCounts[packageManager];

            // generate operations
            var packagesFolderAddActions = new List<PackageSolutionAction>();
            var packagesFolderDeleteActions = new List<PackageSolutionAction>();

            foreach (var action in projectActions)
            {
                if (action.ActionType == PackageActionType.Uninstall)
                {
                    // update the package's ref count
                    if (packageRefCount.ContainsKey(action.Package))
                    {
                        packageRefCount[action.Package]--;
                        if (packageRefCount[action.Package] <= 0)
                        {
                            packagesFolderDeleteActions.Add(
                                new PackageSolutionAction(
                                    PackageActionType.DeleteFromPackagesFolder,
                                    action.Package,
                                    packageManager));
                        }
                    }
                }
                else
                {
                    bool packageExists = false;
                    int refCount;
                    if (packageRefCount.TryGetValue(action.Package, out refCount))
                    {
                        if (refCount > 0)
                        {
                            packageExists = true;
                        }
                        packageRefCount[action.Package] = refCount + 1;
                    }
                    else
                    {                        
                        packageRefCount.Add(action.Package, 1);
                    }

                    if (!packageExists)
                    {
                        // package does not exist in packages folder. We need to add
                        // an add into packages folder action.
                        packagesFolderAddActions.Add(
                            new PackageSolutionAction(
                                PackageActionType.AddToPackagesFolder,
                                action.Package,
                                packageManager));
                    }
                }
            }

            var operations = new List<PackageAction>();
            operations.AddRange(packagesFolderAddActions);
            operations.AddRange(projectActions);
            operations.AddRange(packagesFolderDeleteActions);

            return operations;
        }
    }
}
