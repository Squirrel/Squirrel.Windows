using System;

namespace NuGet
{
    public interface IPackageManager
    {
        /// <summary>
        /// File system that represents the packages folder.
        /// </summary>
        IFileSystem FileSystem { get; set; }

        /// <summary>
        /// Local repository to install and reference packages.
        /// </summary>
        ISharedPackageRepository LocalRepository { get; }

        ILogger Logger { get; set; }

        // !!! This property should be deleted. It's applicable to OperationResolver, 
        // not to this interface.
        DependencyVersion DependencyVersion { get; set; }

        /// <summary>
        /// Remote repository to install packages from.
        /// </summary>
        IPackageRepository SourceRepository { get; }

        IDependencyResolver2 DependencyResolver { get; }

        /// <summary>
        /// PathResolver used to determine paths for installed packages.
        /// </summary>
        IPackagePathResolver PathResolver { get; }

        event EventHandler<PackageOperationEventArgs> PackageInstalled;
        event EventHandler<PackageOperationEventArgs> PackageInstalling;
        event EventHandler<PackageOperationEventArgs> PackageUninstalled;
        event EventHandler<PackageOperationEventArgs> PackageUninstalling;

        void Execute(PackageOperation operation);

        bool IsProjectLevel(IPackage package);
        bool BindingRedirectEnabled { get; set; }        
        void AddBindingRedirects(IProjectManager projectManager);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "3#")]
        IPackage LocatePackageToUninstall(
            IProjectManager projectManager, 
            string id, 
            SemanticVersion version);
    }
}
