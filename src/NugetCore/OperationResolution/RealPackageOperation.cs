using System;
using System.Diagnostics;
using System.Globalization;

namespace NuGet.Resolver
{
    public class Operation : PackageOperation
    {
        // The project that the operation applies to if the operation's target is project.
        // Null if operation's target is packages folder.
        public IProjectManager ProjectManager { get; private set; }

        // The pacakge manager that the operation applies to if the operation's target
        // is pacakges folder.
        public IPackageManager PackageManager { get; private set; }

        // The name of the project if projectManager is not null. The reason we store 
        // the value in this variable instead of calling ProjectManager.Project.ProjectName
        // is that the latter does not work when the porgram is paused in debugger, the error
        // being "the function evaluation requires all threads to run".
        private string _projectName;

        public Operation(
            PackageOperation operation, 
            IProjectManager projectManager,
            IPackageManager packageManager)
            : base(operation.Package, operation.Action)
        {
            if (projectManager != null && packageManager != null)
            {
                throw new ArgumentException("Only one of packageManager and projectManager can be non-null");
            }

            if (operation.Target == PackageOperationTarget.PackagesFolder && packageManager == null)
            {
                throw new ArgumentNullException("packageManager");
            }

            if (operation.Target == PackageOperationTarget.Project && projectManager == null)
            {
                throw new ArgumentNullException("projectManager");
            }

            Target = operation.Target;
            PackageManager = packageManager;
            ProjectManager = projectManager;
            if (ProjectManager != null)
            {
                _projectName = ProjectManager.Project.ProjectName;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}",
                base.ToString(),
                String.IsNullOrEmpty(_projectName) ? "" : " -> " + _projectName);
        }
    }
}
