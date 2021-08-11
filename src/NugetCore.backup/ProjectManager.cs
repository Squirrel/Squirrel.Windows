using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Xml.Linq;
using NuGet.Resources;

#if VS14
using Microsoft.VisualStudio.ProjectSystem.Interop;
using System.Threading;
#endif

namespace NuGet
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
    public class ProjectManager : IProjectManager
    {
        public event EventHandler<PackageOperationEventArgs> PackageReferenceAdding;
        public event EventHandler<PackageOperationEventArgs> PackageReferenceAdded;
        public event EventHandler<PackageOperationEventArgs> PackageReferenceRemoving;
        public event EventHandler<PackageOperationEventArgs> PackageReferenceRemoved;
        
        private ILogger _logger;
        private IPackageConstraintProvider _constraintProvider;
        private readonly IPackageReferenceRepository _packageReferenceRepository;        

        private readonly IDictionary<FileTransformExtensions, IPackageFileTransformer> _fileTransformers = 
            new Dictionary<FileTransformExtensions, IPackageFileTransformer>() 
        {
            { new FileTransformExtensions(".transform", ".transform"), new XmlTransformer(GetConfigMappings()) },
            { new FileTransformExtensions(".pp", ".pp"), new Preprocessor() },
            { new FileTransformExtensions(".install.xdt", ".uninstall.xdt"), new XdtTransformer() }
        };

        public ProjectManager(IPackageManager packageManager, IPackagePathResolver pathResolver, IProjectSystem project, IPackageRepository localRepository)
        {
            // !!! TODO: we should get rid of the parameter pathResolver. Use packageManager's path resolver
            // instead.
            if (pathResolver == null)
            {
                throw new ArgumentNullException("pathResolver");
            }
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }
            if (localRepository == null)
            {
                throw new ArgumentNullException("localRepository");
            }

            PackageManager = packageManager;
            Project = project;
            PathResolver = pathResolver;
            LocalRepository = localRepository;
            _packageReferenceRepository = LocalRepository as IPackageReferenceRepository;
        }

        public IPackageManager PackageManager
        {
            get;
            private set;
        }

        public IPackagePathResolver PathResolver
        {
            get;
            private set;
        }

        public IPackageRepository LocalRepository
        {
            get;
            private set;
        }

        public IPackageConstraintProvider ConstraintProvider
        {
            get
            {
                return _constraintProvider ?? NullConstraintProvider.Instance;
            }
            set
            {
                _constraintProvider = value;
            }
        }

        public IProjectSystem Project
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
       
        public virtual void Execute(PackageOperation operation)
        {
            bool packageExists = LocalRepository.Exists(operation.Package);

            if (operation.Action == PackageAction.Install)
            {
                // If the package is already installed, then skip it
                if (packageExists)
                {
                    Logger.Log(MessageLevel.Info, NuGetResources.Log_ProjectAlreadyReferencesPackage, Project.ProjectName, operation.Package.GetFullName());
                }
                else
                {
                    AddPackageReferenceToProject(operation.Package);
                }
            }
            else
            {
                if (packageExists)
                {
                    RemovePackageReferenceFromProject(operation.Package);
                }
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Error caused by conditional compilation")]
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "package", Justification = "Error caused by conditional compilation")]
        void AddPackageReferenceToNuGetAwareProject(IPackage package)
        {
#if VS14
            var nugetAwareProject = Project as INuGetPackageManager;
            var args = new Dictionary<string, object>();
            using (var cts = new CancellationTokenSource())
            {
                var packageSupportedFrameworks = package.GetSupportedFrameworks();
                var projectFrameworks = nugetAwareProject.GetSupportedFrameworksAsync(cts.Token).Result;
                args["Frameworks"] = projectFrameworks.Where(
                    projectFramework =>
                        NuGet.VersionUtility.IsCompatible(
                            projectFramework,
                            packageSupportedFrameworks)).ToArray();
                var task = nugetAwareProject.InstallPackageAsync(
                    new NuGetPackageMoniker
                    {
                        Id = package.Id,
                        Version = package.Version.ToString()
                    },
                    args,
                    logger: null,
                    progress: null,
                    cancellationToken: cts.Token);
                task.Wait();
                return;
            }
#else
            // no-op
#endif
        }

        // Returns a value indicating if the Project is a nuget aware project.
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Error caused by conditional compilation")]
        private bool IsNuGetAwareProject()
        {
#if VS14
            var nugetAwareProject = Project as INuGetPackageManager;
            return nugetAwareProject != null;

#else
            return false;
#endif
        }

        protected void AddPackageReferenceToProject(IPackage package)
        {
            string packageFullName = package.GetFullName();
            Logger.Log(MessageLevel.Info, NuGetResources.Log_BeginAddPackageReference, packageFullName, Project.ProjectName);

            if (IsNuGetAwareProject())
            {
                AddPackageReferenceToNuGetAwareProject(package);
                Logger.Log(MessageLevel.Info, NuGetResources.Log_SuccessfullyAddedPackageReference, packageFullName, Project.ProjectName);
            }
            else
            {
                PackageOperationEventArgs args = CreateOperation(package);
                OnPackageReferenceAdding(args);

                if (args.Cancel)
                {
                    return;
                }

                ExtractPackageFilesToProject(package);
                Logger.Log(MessageLevel.Info, NuGetResources.Log_SuccessfullyAddedPackageReference, packageFullName, Project.ProjectName);
                OnPackageReferenceAdded(args);
            }            
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        protected virtual void ExtractPackageFilesToProject(IPackage package)
        {
            // BUG 491: Installing a package with incompatible binaries still does a partial install.
            // Resolve assembly references and content files first so that if this fails we never do anything to the project
            List<IPackageAssemblyReference> assemblyReferences = Project.GetCompatibleItemsCore(package.AssemblyReferences).ToList();
            List<FrameworkAssemblyReference> frameworkReferences = Project.GetCompatibleItemsCore(package.FrameworkAssemblies).ToList();
            List<IPackageFile> contentFiles = Project.GetCompatibleItemsCore(package.GetContentFiles()).ToList();
            List<IPackageFile> buildFiles = Project.GetCompatibleItemsCore(package.GetBuildFiles()).ToList();

            // If the package doesn't have any compatible assembly references or content files,
            // throw, unless it's a meta package.
            if (assemblyReferences.Count == 0 && frameworkReferences.Count == 0 && contentFiles.Count == 0 && buildFiles.Count == 0 &&
                (package.FrameworkAssemblies.Any() || package.AssemblyReferences.Any() || package.GetContentFiles().Any() || package.GetBuildFiles().Any()))
            {
                // for portable framework, we want to show the friendly short form (e.g. portable-win8+net45+wp8) instead of ".NETPortable, Profile=Profile104".
                FrameworkName targetFramework = Project.TargetFramework;
                string targetFrameworkString = targetFramework.IsPortableFramework()
                                                    ? VersionUtility.GetShortFrameworkName(targetFramework)
                                                    : targetFramework != null ? targetFramework.ToString() : null;

                throw new InvalidOperationException(
                           String.Format(CultureInfo.CurrentCulture,
                           NuGetResources.UnableToFindCompatibleItems, package.GetFullName(), targetFrameworkString));
            }

            // IMPORTANT: this filtering has to be done AFTER the 'if' statement above,
            // so that we don't throw the exception in case the <References> filters out all assemblies.
            FilterAssemblyReferences(assemblyReferences, package.PackageAssemblyReferences);

            try
            {
                // Log target framework info for debugging
                LogTargetFrameworkInfo(package, assemblyReferences, contentFiles, buildFiles);

                // Add content files
                Project.AddFiles(contentFiles, _fileTransformers);

                // Add the references to the reference path
                foreach (IPackageAssemblyReference assemblyReference in assemblyReferences)
                {
                    if (assemblyReference.IsEmptyFolder())
                    {
                        continue;
                    }

                    // Get the physical path of the assembly reference
                    string referencePath = Path.Combine(PathResolver.GetInstallPath(package), assemblyReference.Path);
                    string relativeReferencePath = PathUtility.GetRelativePath(Project.Root, referencePath);

                    if (Project.ReferenceExists(assemblyReference.Name))
                    {
                        Project.RemoveReference(assemblyReference.Name);
                    }

                    Project.AddReference(relativeReferencePath);
                }

                // Add GAC/Framework references
                foreach (FrameworkAssemblyReference frameworkReference in frameworkReferences)
                {
                    if (!Project.ReferenceExists(frameworkReference.AssemblyName))
                    {
                        Project.AddFrameworkReference(frameworkReference.AssemblyName);
                    }
                }

                foreach (var importFile in buildFiles)
                {
                    string fullImportFilePath = Path.Combine(PathResolver.GetInstallPath(package), importFile.Path);
                    Project.AddImport(
                        fullImportFilePath, 
                        importFile.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ? ProjectImportLocation.Top : ProjectImportLocation.Bottom);
                }
            }
            finally
            {
                if (_packageReferenceRepository != null)
                {
                    // save the used project's framework if the repository supports it.
                    _packageReferenceRepository.AddPackage(package.Id, package.Version, package.DevelopmentDependency, Project.TargetFramework);
                }
                else
                {
                    // Add package to local repository in the finally so that the user can uninstall it
                    // if any exception occurs. This is easier than rolling back since the user can just
                    // manually uninstall things that may have failed.
                    // If this fails then the user is out of luck.
                    LocalRepository.AddPackage(package);
                }
            }
        }

        private void LogTargetFrameworkInfo(IPackage package, List<IPackageAssemblyReference> assemblyReferences, List<IPackageFile> contentFiles, List<IPackageFile> buildFiles)
        {
            if (assemblyReferences.Count > 0 || contentFiles.Count > 0 || buildFiles.Count > 0)
            {
                // targetFramework can be null for unknown project types
                string shortFramework = Project.TargetFramework == null ? string.Empty : VersionUtility.GetShortFrameworkName(Project.TargetFramework);

                Logger.Log(MessageLevel.Debug, NuGetResources.Debug_TargetFrameworkInfoPrefix, package.GetFullName(), Project.ProjectName, shortFramework);

                if (assemblyReferences.Count > 0)
                {
                    Logger.Log(MessageLevel.Debug, NuGetResources.Debug_TargetFrameworkInfo, NuGetResources.Debug_TargetFrameworkInfo_AssemblyReferences,
                        Path.GetDirectoryName(assemblyReferences[0].Path), VersionUtility.GetTargetFrameworkLogString(assemblyReferences[0].TargetFramework));
                }

                if (contentFiles.Count > 0)
                {
                    Logger.Log(MessageLevel.Debug, NuGetResources.Debug_TargetFrameworkInfo, NuGetResources.Debug_TargetFrameworkInfo_ContentFiles,
                        Path.GetDirectoryName(contentFiles[0].Path), VersionUtility.GetTargetFrameworkLogString(contentFiles[0].TargetFramework));
                }

                if (buildFiles.Count > 0)
                {
                    Logger.Log(MessageLevel.Debug, NuGetResources.Debug_TargetFrameworkInfo, NuGetResources.Debug_TargetFrameworkInfo_BuildFiles,
                        Path.GetDirectoryName(buildFiles[0].Path), VersionUtility.GetTargetFrameworkLogString(buildFiles[0].TargetFramework));
                }
            }
        }

        private void FilterAssemblyReferences(List<IPackageAssemblyReference> assemblyReferences, ICollection<PackageReferenceSet> packageAssemblyReferences)
        {
            if (packageAssemblyReferences != null && packageAssemblyReferences.Count > 0)
            {
                var packageReferences = Project.GetCompatibleItemsCore(packageAssemblyReferences).FirstOrDefault();
                if (packageReferences != null)
                {
                    // remove all assemblies of which names do not appear in the References list
                    assemblyReferences.RemoveAll(assembly => !packageReferences.References.Contains(assembly.Name, StringComparer.OrdinalIgnoreCase));
                }
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Error caused by conditional compilation")]
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "package", Justification = "Error caused by conditional compilation")]
        private void RemovePackageReferenceFromNuGetAwareProject(IPackage package)
        {
#if VS14
            var nugetAwareProject = Project as INuGetPackageManager;
            string packageFullName = package.GetFullName();
            Logger.Log(MessageLevel.Info, NuGetResources.Log_BeginRemovePackageReference, packageFullName, Project.ProjectName);

            var args = new Dictionary<string, object>();
            using (var cts = new CancellationTokenSource())
            {
                var task = nugetAwareProject.UninstallPackageAsync(
                    new NuGetPackageMoniker
                    {
                        Id = package.Id,
                        Version = package.Version.ToString()
                    },
                    args,
                    logger: null,
                    progress: null,
                    cancellationToken: cts.Token);
                task.Wait();
            }

            Logger.Log(MessageLevel.Info, NuGetResources.Log_SuccessfullyRemovedPackageReference, packageFullName, Project.ProjectName);
#else
            // no-op
#endif
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        private void RemovePackageReferenceFromProject(IPackage package)
        {
            if (IsNuGetAwareProject())
            {
                RemovePackageReferenceFromNuGetAwareProject(package);
                return;
            }

            string packageFullName = package.GetFullName();
            Logger.Log(MessageLevel.Info, NuGetResources.Log_BeginRemovePackageReference, packageFullName, Project.ProjectName);

            PackageOperationEventArgs args = CreateOperation(package);
            OnPackageReferenceRemoving(args);

            if (args.Cancel)
            {
                return;
            }


            // Get other packages
            IEnumerable<IPackage> otherPackages = from p in LocalRepository.GetPackages()
                                                  where p.Id != package.Id
                                                  select p;

            // Get other references
            var otherAssemblyReferences = from p in otherPackages
                                          let assemblyReferences = GetFilteredAssembliesToDelete(p)
                                          from assemblyReference in assemblyReferences ?? Enumerable.Empty<IPackageAssemblyReference>() // This can happen if package installed left the project in a bad state
                                          select assemblyReference;

            // Get content files from other packages
            // Exclude transform files since they are treated specially
            var otherContentFiles = from p in otherPackages
                                    from file in GetCompatibleInstalledItemsForPackage(p.Id, p.GetContentFiles(), NetPortableProfileTable.Default)
                                    where !IsTransformFile(file.Path)
                                    select file;

            // Get the files and references for this package, that aren't in use by any other packages so we don't have to do reference counting
            var assemblyReferencesToDelete = GetFilteredAssembliesToDelete(package)
                                             .Except(otherAssemblyReferences, PackageFileComparer.Default);

            var contentFilesToDelete = GetCompatibleInstalledItemsForPackage(package.Id, package.GetContentFiles(), NetPortableProfileTable.Default)
                                       .Except(otherContentFiles, PackageFileComparer.Default);

            var buildFilesToDelete = GetCompatibleInstalledItemsForPackage(package.Id, package.GetBuildFiles(), NetPortableProfileTable.Default);

            // Delete the content files
            Project.DeleteFiles(contentFilesToDelete, otherPackages, _fileTransformers);

            // Remove references
            foreach (IPackageAssemblyReference assemblyReference in assemblyReferencesToDelete)
            {
                Project.RemoveReference(assemblyReference.Name);
            }

            // remove the <Import> statement from projects for the .targets and .props files
            foreach (var importFile in buildFilesToDelete)
            {
                string fullImportFilePath = Path.Combine(PathResolver.GetInstallPath(package), importFile.Path);
                Project.RemoveImport(fullImportFilePath);
            }

            // Remove package to the repository
            LocalRepository.RemovePackage(package);


            Logger.Log(MessageLevel.Info, NuGetResources.Log_SuccessfullyRemovedPackageReference, packageFullName, Project.ProjectName);
            OnPackageReferenceRemoved(args);
        }

        private bool IsTransformFile(string path)
        {
            return _fileTransformers.Keys.Any(
                file => path.EndsWith(file.InstallExtension, StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(file.UninstallExtension, StringComparison.OrdinalIgnoreCase));
        }

        private IList<IPackageAssemblyReference> GetFilteredAssembliesToDelete(IPackage package)
        {
            List<IPackageAssemblyReference> assemblyReferences = GetCompatibleInstalledItemsForPackage(package.Id, package.AssemblyReferences, NetPortableProfileTable.Default).ToList();
            if (assemblyReferences.Count == 0)
            {
                return assemblyReferences;
            }

            var packageReferences = GetCompatibleInstalledItemsForPackage(package.Id, package.PackageAssemblyReferences, NetPortableProfileTable.Default).FirstOrDefault();
            if (packageReferences != null) 
            {
                assemblyReferences.RemoveAll(p => !packageReferences.References.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
            }

            return assemblyReferences;
        }

        private void OnPackageReferenceAdding(PackageOperationEventArgs e)
        {
            if (PackageReferenceAdding != null)
            {
                PackageReferenceAdding(this, e);
            }
        }

        private void OnPackageReferenceAdded(PackageOperationEventArgs e)
        {
            if (PackageReferenceAdded != null)
            {
                PackageReferenceAdded(this, e);
            }
        }

        private void OnPackageReferenceRemoved(PackageOperationEventArgs e)
        {
            if (PackageReferenceRemoved != null)
            {
                PackageReferenceRemoved(this, e);
            }
        }

        private void OnPackageReferenceRemoving(PackageOperationEventArgs e)
        {
            if (PackageReferenceRemoving != null)
            {
                PackageReferenceRemoving(this, e);
            }
        }

        /// <summary>
        /// This method uses the 'targetFramework' attribute in the packages.config to determine compatible items.
        /// Hence, it's only good for uninstall operations.
        /// </summary>
        private IEnumerable<T> GetCompatibleInstalledItemsForPackage<T>(string packageId, IEnumerable<T> items, NetPortableProfileTable portableProfileTable) where T : IFrameworkTargetable
        {
            FrameworkName packageFramework = ProjectManagerExtensions.GetTargetFrameworkForPackage(this, packageId);
            if (packageFramework == null)
            {
                return items;
            }

            IEnumerable<T> compatibleItems;
            if (VersionUtility.TryGetCompatibleItems(packageFramework, items, portableProfileTable, out compatibleItems))
            {
                return compatibleItems;
            }
            return Enumerable.Empty<T>();
        }
       
        public PackageOperationEventArgs CreateOperation(IPackage package)
        {
            return new PackageOperationEventArgs(package, Project, PathResolver.GetInstallPath(package));
        }

        private static IDictionary<XName, Action<XElement, XElement>> GetConfigMappings()
        {
            // REVIEW: This might be an edge case, but we're setting this rule for all xml files.
            // If someone happens to do a transform where the xml file has a configSections node
            // we will add it first. This is probably fine, but this is a config specific scenario
            return new Dictionary<XName, Action<XElement, XElement>>() {
                { "configSections" , (parent, element) => parent.AddFirst(element) }
            };
        }

        private class PackageFileComparer : IEqualityComparer<IPackageFile>
        {
            internal readonly static PackageFileComparer Default = new PackageFileComparer();
            private PackageFileComparer()
            {
            }

            public bool Equals(IPackageFile x, IPackageFile y)
            {
                // technically, this check will fail if, for example, 'x' is a content file and 'y' is a lib file.
                // However, because we only use this comparer to compare files within the same folder type, 
                // this check is sufficient.
                return x.TargetFramework == y.TargetFramework &&
                       x.EffectivePath.Equals(y.EffectivePath, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(IPackageFile obj)
            {
                return obj.Path.GetHashCode();
            }
        }
    }
}