using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Resolver
{
    public enum PackageActionType
    {
        // installs a package into a project/solution
        Install, 

        // uninstalls a package from a project/solution
        Uninstall,

        // downloads the package if needed and adds it to the packages folder
        AddToPackagesFolder,

        // deletes the package from the packages folder
        DeleteFromPackagesFolder
    }

    public abstract class PackageAction
    {
        public PackageActionType ActionType { get; private set; }
        public IPackage Package { get; private set; }

        protected PackageAction(
            PackageActionType actionType,
            IPackage package)
        {
            ActionType = actionType;
            Package = package;
        }
    }

    public class PackageProjectAction : PackageAction
    {
        public IProjectManager ProjectManager { get; private set; }

        // The name of the project if projectManager is not null. The reason we store 
        // the value in this variable instead of calling ProjectManager.Project.ProjectName
        // is that the latter does not work when the porgram is paused in debugger, the error
        // being "the function evaluation requires all threads to run".
        private string _projectName;

        public PackageProjectAction(PackageActionType actionType, IPackage package, IProjectManager projectManager) :
            base(actionType, package)
        {
            ProjectManager = projectManager;
            _projectName = ProjectManager.Project.ProjectName;
        }

        public override string ToString()
        {
            if (ActionType == PackageActionType.Install)
            {
                return string.Format(CultureInfo.InvariantCulture, "Install {0} into project '{1}'",
                    Package.ToString(),
                    _projectName);
            }
            else
            {
                return string.Format(CultureInfo.InvariantCulture, "Uninstall {0} from project '{1}'",
                    Package.ToString(),
                    _projectName);
            }
        }
    }

    public class PackageSolutionAction : PackageAction
    {
        public IPackageManager PackageManager { get; private set; }

        public PackageSolutionAction(PackageActionType actionType, IPackage package, IPackageManager packageManager) :
            base(actionType, package)
        {
            PackageManager = packageManager;
        }

        public override string ToString()
        {
            switch (ActionType)
            {
                case PackageActionType.Install:
                    return string.Format(CultureInfo.InvariantCulture, "Install {0} into solution",
                        Package.ToString());
                case PackageActionType.Uninstall:
                    return string.Format(CultureInfo.InvariantCulture, "Uninstall {0} from solution",
                        Package.ToString());
                case PackageActionType.AddToPackagesFolder:
                    return string.Format(CultureInfo.InvariantCulture, "Add {0} into packages folder",
                        Package.ToString());
                default: // PackageActionType.DeleteFromPackagesFolder
                    return string.Format(CultureInfo.InvariantCulture, "Delete {0} from packages folder",
                        Package.ToString());
            }
        }
    }
}
