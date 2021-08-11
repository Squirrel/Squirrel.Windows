using System;

namespace NuGet
{
    /// <summary>
    /// This class is used to manage packages in a project.
    /// </summary>
    public interface IProjectManager
    {
        IPackageRepository LocalRepository { get; }
        IPackageManager PackageManager { get; }

        ILogger Logger { get; set; }
        IProjectSystem Project { get; }

        IPackageConstraintProvider ConstraintProvider { get; set; }

        event EventHandler<PackageOperationEventArgs> PackageReferenceAdded;
        event EventHandler<PackageOperationEventArgs> PackageReferenceAdding;
        event EventHandler<PackageOperationEventArgs> PackageReferenceRemoved;
        event EventHandler<PackageOperationEventArgs> PackageReferenceRemoving;

        void Execute(PackageOperation operation);
    }
}