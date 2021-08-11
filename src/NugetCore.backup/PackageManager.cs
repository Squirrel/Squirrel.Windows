using System;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.Resources;

namespace NuGet
{
    public class PackageManager : IPackageManager
    {
        private ILogger _logger;

        public event EventHandler<PackageOperationEventArgs> PackageInstalling;
        public event EventHandler<PackageOperationEventArgs> PackageInstalled;
        public event EventHandler<PackageOperationEventArgs> PackageUninstalling;
        public event EventHandler<PackageOperationEventArgs> PackageUninstalled;

        public PackageManager(IPackageRepository sourceRepository, string path)
            : this(sourceRepository, new DefaultPackagePathResolver(path), new PhysicalFileSystem(path))
        {
        }

        public PackageManager(IPackageRepository sourceRepository, IPackagePathResolver pathResolver, IFileSystem fileSystem) :
            this(sourceRepository, pathResolver, fileSystem, new SharedPackageRepository(pathResolver, fileSystem, fileSystem))
        {
        }

        public PackageManager(
            IPackageRepository sourceRepository, 
            IPackagePathResolver pathResolver, 
            IFileSystem fileSystem, 
            ISharedPackageRepository localRepository)
        {
            if (sourceRepository == null)
            {
                throw new ArgumentNullException("sourceRepository");
            }
            if (pathResolver == null)
            {
                throw new ArgumentNullException("pathResolver");
            }
            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }
            if (localRepository == null)
            {
                throw new ArgumentNullException("localRepository");
            }

            SourceRepository = sourceRepository;
            DependencyResolver = new DependencyResolverFromRepo(sourceRepository);
            PathResolver = pathResolver;
            FileSystem = fileSystem;
            LocalRepository = localRepository;
            DependencyVersion = DependencyVersion.Lowest;
            CheckDowngrade = true;
        }

        public IFileSystem FileSystem
        {
            get;
            set;
        }

        public IPackageRepository SourceRepository
        {
            get;
            private set;
        }

        public IDependencyResolver2 DependencyResolver
        {
            get;
            private set;
        }

        public ISharedPackageRepository LocalRepository
        {
            get;
            private set;
        }

        public IPackagePathResolver PathResolver
        {
            get;
            private set;
        }

        public ILogger Logger
        {
            get
            {
                return _logger ?? NullLogger.Instance;
            }
            set
            {
                _logger = value;
            }
        }

        public DependencyVersion DependencyVersion
        {
            get;
            set;
        }

        public void Execute(PackageOperation operation)
        {
            bool packageExists = LocalRepository.Exists(operation.Package);

            if (operation.Action == PackageAction.Install)
            {
                // If the package is already installed, then skip it
                if (packageExists)
                {
                    Logger.Log(MessageLevel.Info, NuGetResources.Log_PackageAlreadyInstalled, operation.Package.GetFullName());
                }
                else
                {
                    ExecuteInstall(operation.Package);
                }
            }
            else
            {
                if (packageExists)
                {
                    ExecuteUninstall(operation.Package);
                }
            }
        }

        protected void ExecuteInstall(IPackage package)
        {
            string packageFullName = package.GetFullName();
            Logger.Log(MessageLevel.Info, NuGetResources.Log_BeginInstallPackage, packageFullName);

            PackageOperationEventArgs args = CreateOperation(package);
            OnInstalling(args);

            if (args.Cancel)
            {
                return;
            }

            OnExpandFiles(args);

            LocalRepository.AddPackage(package);

            Logger.Log(MessageLevel.Info, NuGetResources.Log_PackageInstalledSuccessfully, packageFullName);

            OnInstalled(args);
        }

        private void ExpandFiles(IPackage package)
        {
            var batchProcessor = FileSystem as IBatchProcessor<string>;
            try
            {
                var files = package.GetFiles().ToList();
                if (batchProcessor != null)
                {
                    // Notify the batch processor that the files are being added. This is to allow source controlled file systems 
                    // to manage previously uninstalled files.
                    batchProcessor.BeginProcessing(files.Select(p => p.Path), PackageAction.Install);
                }

                string packageDirectory = PathResolver.GetPackageDirectory(package);

                // Add files
                FileSystem.AddFiles(files, packageDirectory);

                // If this is a Satellite Package, then copy the satellite files into the related runtime package folder too
                IPackage runtimePackage;
                if (PackageHelper.IsSatellitePackage(package, LocalRepository, targetFramework: null, runtimePackage: out runtimePackage))
                {
                    var satelliteFiles = package.GetSatelliteFiles();
                    var runtimePath = PathResolver.GetPackageDirectory(runtimePackage);
                    FileSystem.AddFiles(satelliteFiles, runtimePath);
                }
            }
            finally
            {
                if (batchProcessor != null)
                {
                    batchProcessor.EndProcessing();
                }
            }
        }

        protected virtual void ExecuteUninstall(IPackage package)
        {
            string packageFullName = package.GetFullName();
            Logger.Log(MessageLevel.Info, NuGetResources.Log_BeginUninstallPackage, packageFullName);

            PackageOperationEventArgs args = CreateOperation(package);
            OnUninstalling(args);

            if (args.Cancel)
            {
                return;
            }

            OnRemoveFiles(args);

            LocalRepository.RemovePackage(package);

            Logger.Log(MessageLevel.Info, NuGetResources.Log_SuccessfullyUninstalledPackage, packageFullName);

            OnUninstalled(args);
        }

        private void RemoveFiles(IPackage package)
        {
            string packageDirectory = PathResolver.GetPackageDirectory(package);

            // If this is a Satellite Package, then remove the files from the related runtime package folder too
            IPackage runtimePackage;
            if (PackageHelper.IsSatellitePackage(package, LocalRepository, targetFramework: null, runtimePackage: out runtimePackage))
            {
                var satelliteFiles = package.GetSatelliteFiles();
                var runtimePath = PathResolver.GetPackageDirectory(runtimePackage);
                FileSystem.DeleteFiles(satelliteFiles, runtimePath);
            }

            // Remove package files
            // IMPORTANT: This has to be done AFTER removing satellite files from runtime package,
            // because starting from 2.1, we read satellite files directly from package files, instead of .nupkg
            FileSystem.DeleteFiles(package.GetFiles(), packageDirectory);
        }

        protected virtual void OnInstalling(PackageOperationEventArgs e)
        {
            if (PackageInstalling != null)
            {
                PackageInstalling(this, e);
            }
        }

        protected virtual void OnExpandFiles(PackageOperationEventArgs e)
        {
            ExpandFiles(e.Package);
        }

        protected virtual void OnInstalled(PackageOperationEventArgs e)
        {
            if (PackageInstalled != null)
            {
                PackageInstalled(this, e);
            }
        }

        protected virtual void OnUninstalling(PackageOperationEventArgs e)
        {
            if (PackageUninstalling != null)
            {
                PackageUninstalling(this, e);
            }
        }

        protected virtual void OnRemoveFiles(PackageOperationEventArgs e)
        {
            RemoveFiles(e.Package);
        }

        protected virtual void OnUninstalled(PackageOperationEventArgs e)
        {
            if (PackageUninstalled != null)
            {
                PackageUninstalled(this, e);
            }
        }

        public PackageOperationEventArgs CreateOperation(IPackage package)
        {
            return new PackageOperationEventArgs(package, FileSystem, PathResolver.GetInstallPath(package));
        }

        public bool CheckDowngrade { get; set; }

        /// <summary>
        /// Check to see if this package applies to a project based on 2 criteria:
        /// 1. The package has project content (i.e. content that can be applied to a project lib or content files)
        /// 2. The package is referenced by any other project
        /// 3. The package has at least one dependecy
        /// 
        /// This logic will probably fail in one edge case. If there is a meta package that applies to a project
        /// that ended up not being installed in any of the projects and it only exists at solution level.
        /// If this happens, then we think that the following operation applies to the solution instead of showing an error.
        /// To solve that edge case we'd have to walk the graph to find out what the package applies to.
        /// 
        /// Technically, the third condition is not totally accurate because a solution-level package can depend on another 
        /// solution-level package. However, doing that check here is expensive and we haven't seen such a package. 
        /// This condition here is more geared towards guarding against metadata packages, i.e. we shouldn't treat metadata packages 
        /// as solution-level ones.
        /// </summary>
        public bool IsProjectLevel(IPackage package)
        {
            return package.HasProjectContent() ||
                 package.DependencySets.SelectMany(p => p.Dependencies).Any() ||
                LocalRepository.IsReferenced(package.Id, package.Version);
        }

        private bool _bindingRedirectEnabled = true;        

        public bool BindingRedirectEnabled
        {
            get { return _bindingRedirectEnabled; }
            set { _bindingRedirectEnabled = value; }
        }

        public virtual void AddBindingRedirects(IProjectManager projectManager)
        {
            // no-op
        }

        public virtual IPackage LocatePackageToUninstall(IProjectManager projectManager, string id, SemanticVersion version)
        {
            var package = LocalRepository.FindPackagesById(id).SingleOrDefault();
            if (package == null)
            {
                throw new InvalidOperationException();
            }

            return package;
        }
    }
}