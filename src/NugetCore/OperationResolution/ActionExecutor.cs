using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Resolver
{
    public class ActionExecutor
    {
        public ILogger Logger { get; set; }

        // !!! It seems this property should be deleted. Instead, the classes that use this property, such 
        // as SolutionUpdatesProvider, should just call
        //   RegisterPackageOperationEvents(packageManager, projectManager)
        // before userOperationExecutor.Execute(), then call 
        //   UnregisterPackageOperationEvents when everything's done.
        // Also, it seems this whole thing should be replaced with using projectManager's 
        // events themselves.
        public IPackageOperationEventListener PackageOperationEventListener { get; set; }

        // true means the exception generated when execute a project operation is caught.
        // false means such an exception is rethrown.
        public bool CatchProjectOperationException { get; set; }

        public ActionExecutor()
        {
            Logger = NullLogger.Instance;
        }

        public void Execute(IEnumerable<PackageAction> actions)
        {
            var executedActions = new List<PackageAction>();
            try
            {
                foreach (var action in actions)
                {
                    executedActions.Add(action);

                    PackageProjectAction projectAction = action as PackageProjectAction;
                    if (projectAction != null)
                    {
                        ExecuteProjectOperation(projectAction);

                        // Binding redirect
                        if (projectAction.ActionType == PackageActionType.Install &&
                            projectAction.ProjectManager.PackageManager != null &&
                            projectAction.ProjectManager.PackageManager.BindingRedirectEnabled &&
                            projectAction.ProjectManager.Project.IsBindingRedirectSupported)
                        {
                            projectAction.ProjectManager.PackageManager.AddBindingRedirects(projectAction.ProjectManager);
                        }
                    }
                    else
                    {
                        PackageSolutionAction solutionAction = (PackageSolutionAction)action;
                        solutionAction.PackageManager.Logger = Logger;
                        if (solutionAction.ActionType == PackageActionType.AddToPackagesFolder)
                        {
                            solutionAction.PackageManager.Execute(new PackageOperation(
                                action.Package,
                                NuGet.PackageAction.Install));
                        }
                        else if (solutionAction.ActionType == PackageActionType.DeleteFromPackagesFolder)
                        {
                            solutionAction.PackageManager.Execute(new PackageOperation(
                                action.Package,
                                NuGet.PackageAction.Uninstall));
                        }
                    }
                }
            }
            catch
            {
                Rollback(executedActions);
                throw;
            }
        }

        private void ExecuteProjectOperation(PackageProjectAction action)
        {
            try
            {
                if (PackageOperationEventListener != null)
                {
                    PackageOperationEventListener.OnBeforeAddPackageReference(action.ProjectManager);
                }

                action.ProjectManager.Execute(new PackageOperation(
                    action.Package,
                    action.ActionType == PackageActionType.Install ? 
                    NuGet.PackageAction.Install :
                    NuGet.PackageAction.Uninstall));
            }
            catch (Exception e)
            {
                if (CatchProjectOperationException)
                {
                    Logger.Log(MessageLevel.Error, ExceptionUtility.Unwrap(e).Message);

                    if (PackageOperationEventListener != null)
                    {
                        PackageOperationEventListener.OnAddPackageReferenceError(action.ProjectManager, e);
                    }
                }
                else
                {
                    throw;
                }
            }
            finally
            {   
                if (PackageOperationEventListener != null)
                {
                    PackageOperationEventListener.OnAfterAddPackageReference(action.ProjectManager);
                }
            }
        }

        private static PackageActionType GetReverseActionType(PackageActionType actionType)
        {
            switch (actionType)
            {
                case PackageActionType.AddToPackagesFolder:
                    return PackageActionType.DeleteFromPackagesFolder;
                case PackageActionType.DeleteFromPackagesFolder:
                    return PackageActionType.AddToPackagesFolder;
                case PackageActionType.Install:
                    return PackageActionType.Uninstall;
                case PackageActionType.Uninstall:
                    return PackageActionType.Install;
                default:
                    throw new InvalidOperationException();
            }
        }

        private static PackageAction CreateReverseAction(PackageAction action)
        {
            PackageProjectAction projectAction = action as PackageProjectAction;
            if (projectAction != null)
            {
                return new PackageProjectAction(
                    GetReverseActionType(projectAction.ActionType),
                    projectAction.Package,
                    projectAction.ProjectManager);
            }

            PackageSolutionAction solutionAction = (PackageSolutionAction)action;
            return new PackageSolutionAction(
                GetReverseActionType(solutionAction.ActionType),
                solutionAction.Package,
                solutionAction.PackageManager);
        }

        private void Rollback(List<PackageAction> executedOperations)
        {
            if (executedOperations.Count > 0)
            {
                // Only print the rollback warning if we have something to rollback
                Logger.Log(MessageLevel.Warning, "Rolling back");
            }

            executedOperations.Reverse();
            foreach (var operation in executedOperations)
            {
                var reverseOperation = CreateReverseAction(operation);
                PackageProjectAction projectAction = reverseOperation as PackageProjectAction;
                if (projectAction != null)
                {
                    // Don't log anything during the rollback
                    projectAction.ProjectManager.Logger = NullLogger.Instance;
                    projectAction.ProjectManager.Execute(new PackageOperation(
                        projectAction.Package,
                        projectAction.ActionType == PackageActionType.Install ?
                        NuGet.PackageAction.Install :
                        NuGet.PackageAction.Uninstall));
                }
                else
                {
                    PackageSolutionAction solutionAction = (PackageSolutionAction)reverseOperation;

                    // Don't log anything during the rollback
                    solutionAction.PackageManager.Logger = NullLogger.Instance;
                    if (solutionAction.ActionType == PackageActionType.AddToPackagesFolder)
                    {
                        solutionAction.PackageManager.Execute(new PackageOperation(
                            solutionAction.Package,
                            NuGet.PackageAction.Install));
                    }
                    else if (solutionAction.ActionType == PackageActionType.DeleteFromPackagesFolder)
                    {
                        solutionAction.PackageManager.Execute(new PackageOperation(
                            solutionAction.Package,
                            NuGet.PackageAction.Uninstall)); 
                    }
                }
            }
        }
    }
}
