using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.Resources;

namespace NuGet
{
    /// <summary>
    /// Represents a NuGet package backed by a .nupkg file on disk.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ZipPackage"/>, OptimizedZipPackage doesn't store content files in memory. 
    /// Instead, it unzips the .nupkg file to a temp folder on disk, which helps reduce overall memory usage.
    /// </remarks>
    public class OptimizedZipPackage : LocalPackage
    {
        // The DateTimeOffset entry stores the LastModifiedTime of the original .nupkg file that
        // is passed to this class. This is so that we can invalidate the cache when the original
        // file has changed.
        private static readonly ConcurrentDictionary<PackageName, Tuple<string, DateTimeOffset>> _cachedExpandedFolder 
            = new ConcurrentDictionary<PackageName, Tuple<string, DateTimeOffset>>();
        private static readonly IFileSystem _tempFileSystem = new PhysicalFileSystem(Path.Combine(Path.GetTempPath(), "nuget"));

        private Dictionary<string, PhysicalPackageFile> _files;
        private ICollection<FrameworkName> _supportedFrameworks;
        private readonly IFileSystem _fileSystem;
        private readonly IFileSystem _expandedFileSystem;
        private readonly string _packagePath;
        private string _expandedFolderPath;
        private readonly bool _forceUseCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="OptimizedZipPackage" /> class.
        /// </summary>
        /// <param name="fullPackagePath">The full package path on disk.</param>
        /// <exception cref="System.ArgumentException">fullPackagePath</exception>
        public OptimizedZipPackage(string fullPackagePath)
        {
            if (String.IsNullOrEmpty(fullPackagePath))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "fullPackagePath");
            }

            if (!File.Exists(fullPackagePath))
            {
                throw new ArgumentException(
                    String.Format(CultureInfo.CurrentCulture, NuGetResources.FileDoesNotExit, fullPackagePath),
                    "fullPackagePath");
            }

            string directory = Path.GetDirectoryName(fullPackagePath);
            _fileSystem = new PhysicalFileSystem(directory);
            _packagePath = Path.GetFileName(fullPackagePath);
            _expandedFileSystem = _tempFileSystem;

            EnsureManifest();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptimizedZipPackage" /> class.
        /// </summary>
        /// <param name="fileSystem">The file system which contains the .nupkg file.</param>
        /// <param name="packagePath">The relative package path within the file system.</param>
        public OptimizedZipPackage(IFileSystem fileSystem, string packagePath)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            if (String.IsNullOrEmpty(packagePath))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "packagePath");
            }

            _fileSystem = fileSystem;
            _packagePath = packagePath;
            _expandedFileSystem = _tempFileSystem;

            EnsureManifest();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptimizedZipPackage" /> class.
        /// </summary>
        /// <param name="fileSystem">The file system which contains the .nupkg file.</param>
        /// <param name="packagePath">The relative package path within the file system.</param>
        /// <param name="expandedFileSystem">The file system which should be used to store unzipped content files.</param>
        /// <exception cref="System.ArgumentNullException">fileSystem</exception>
        /// <exception cref="System.ArgumentException">packagePath</exception>
        public OptimizedZipPackage(IFileSystem fileSystem, string packagePath, IFileSystem expandedFileSystem)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            if (expandedFileSystem == null)
            {
                throw new ArgumentNullException("expandedFileSystem");
            }

            if (String.IsNullOrEmpty(packagePath))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "packagePath");
            }

            _fileSystem = fileSystem;
            _packagePath = packagePath;
            _expandedFileSystem = expandedFileSystem;

            EnsureManifest();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OptimizedZipPackage" /> class.
        /// </summary>
        /// <param name="fileSystem">The file system which contains the .nupkg file.</param>
        /// <param name="packagePath">The relative package path within the file system.</param>
        /// <param name="expandedFileSystem">The file system which should be used to store unzipped content files.</param>
        /// <exception cref="System.ArgumentNullException">fileSystem</exception>
        /// <exception cref="System.ArgumentException">packagePath</exception>
        internal OptimizedZipPackage(IFileSystem fileSystem, string packagePath, IFileSystem expandedFileSystem, bool forceUseCache) :
            this(fileSystem, packagePath, expandedFileSystem)
        {
            // this is used by unit test
            _forceUseCache = forceUseCache;
        }

        public bool IsValid
        {
            get
            {
                return _fileSystem.FileExists(_packagePath);
            }
        }

        protected IFileSystem FileSystem
        {
            get
            {
                return _fileSystem;
            }
        }

        public override Stream GetStream()
        {
            return _fileSystem.OpenFile(_packagePath);
        }

        protected override IEnumerable<IPackageFile> GetFilesBase()
        {
            EnsurePackageFiles();
            return _files.Values;
        }

        protected override IEnumerable<IPackageAssemblyReference> GetAssemblyReferencesCore()
        {
            EnsurePackageFiles();

            return from file in _files.Values
                   where IsAssemblyReference(file.Path)
                   select (IPackageAssemblyReference)new PhysicalPackageAssemblyReference(file);
        }

        public override IEnumerable<FrameworkName> GetSupportedFrameworks()
        {
            EnsurePackageFiles();

            if (_supportedFrameworks == null)
            {
                var fileFrameworks = _files.Values.Select(c => c.TargetFramework);
                var combinedFrameworks = base.GetSupportedFrameworks()
                                             .Concat(fileFrameworks)
                                             .Where(f => f != null)
                                             .Distinct();

                _supportedFrameworks = new ReadOnlyCollection<FrameworkName>(combinedFrameworks.ToList());
            }

            return _supportedFrameworks;
        }

        private void EnsureManifest()
        {
            using (Stream stream = _fileSystem.OpenFile(_packagePath))
            {
                Package package = Package.Open(stream);
                PackageRelationship relationshipType = package.GetRelationshipsByType(Constants.PackageRelationshipNamespace + PackageBuilder.ManifestRelationType).SingleOrDefault();

                if (relationshipType == null)
                {
                    throw new InvalidOperationException(NuGetResources.PackageDoesNotContainManifest);
                }

                PackagePart manifestPart = package.GetPart(relationshipType.TargetUri);

                if (manifestPart == null)
                {
                    throw new InvalidOperationException(NuGetResources.PackageDoesNotContainManifest);
                }

                using (Stream manifestStream = manifestPart.GetStream())
                {
                    ReadManifest(manifestStream);
                }
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We want to catch all the exceptions for CreateFile")]
        private void EnsurePackageFiles()
        {
            if (_files != null &&
                _expandedFolderPath != null &&
                _expandedFileSystem.DirectoryExists(_expandedFolderPath))
            {
                return;
            }

            _files = new Dictionary<string, PhysicalPackageFile>();
            _supportedFrameworks = null;

            var packageName = new PackageName(Id, Version);

            // Only use the cache for expanded folders under %temp%, or set from unit tests
            if (_expandedFileSystem == _tempFileSystem || _forceUseCache)
            {
                Tuple<string, DateTimeOffset> cacheValue;
                DateTimeOffset lastModifiedTime = _fileSystem.GetLastModified(_packagePath);

                // if the cache doesn't exist, or it exists but points to a stale package,
                // then we invalidate the cache and store the new entry.
                if (!_cachedExpandedFolder.TryGetValue(packageName, out cacheValue) ||
                    cacheValue.Item2 < lastModifiedTime)
                {
                    cacheValue = Tuple.Create(GetExpandedFolderPath(), lastModifiedTime);
                    _cachedExpandedFolder[packageName] = cacheValue;
                }

                _expandedFolderPath = cacheValue.Item1;
            }
            else
            {
                _expandedFolderPath = GetExpandedFolderPath();
            }

            using (Stream stream = GetStream())
            {
                Package package = Package.Open(stream);
                // unzip files inside package
                var files = from part in package.GetParts()
                            where ZipPackage.IsPackageFile(part)
                            select part;

                // now copy all package's files to disk
                foreach (PackagePart file in files)
                {
                    string path = UriUtility.GetPath(file.Uri);
                    string filePath = Path.Combine(_expandedFolderPath, path);

                    bool copyFile = true;
                    if (_expandedFileSystem.FileExists(filePath))
                    {
                        using (Stream partStream = file.GetStream(),
                                      targetStream = _expandedFileSystem.OpenFile(filePath))
                        {
                            // if the target file already exists, 
                            // don't copy file if the lengths are equal.
                            copyFile = partStream.Length != targetStream.Length;
                        }
                    }

                    if (copyFile)
                    {
                        using (Stream partStream = file.GetStream())
                        {
                            try
                            {
                                using (Stream targetStream = _expandedFileSystem.CreateFile(filePath))
                                {
                                    partStream.CopyTo(targetStream);
                                }
                            }
                            catch (Exception)
                            {
                                // if the file is read-only or has an access denied issue, we just ignore it
                            }
                        }
                    }

                    var packageFile = new PhysicalPackageFile
                    {
                        SourcePath = _expandedFileSystem.GetFullPath(filePath),
                        TargetPath = path
                    };

                    _files[path] = packageFile;
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        protected virtual string GetExpandedFolderPath()
        {
            return Path.GetRandomFileName();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        public static void PurgeCache()
        {
            lock (_cachedExpandedFolder)
            {
                if (_cachedExpandedFolder.Count > 0)
                {
                    foreach (var valueTuple in _cachedExpandedFolder.Values)
                    {
                        try
                        {
                            string expandedFolder = valueTuple.Item1;
                            _tempFileSystem.DeleteDirectory(expandedFolder, recursive: true);
                        }
                        catch (Exception)
                        {
                        }
                    }

                    _cachedExpandedFolder.Clear();
                }
            }
        }
    }
}