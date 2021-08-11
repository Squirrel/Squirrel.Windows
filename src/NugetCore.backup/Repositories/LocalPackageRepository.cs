using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NuGet.Resources;

namespace NuGet
{
    public class LocalPackageRepository : PackageRepositoryBase, IPackageLookup
    {
        private readonly ConcurrentDictionary<string, PackageCacheEntry> _packageCache = new ConcurrentDictionary<string, PackageCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<PackageName, string> _packagePathLookup = new ConcurrentDictionary<PackageName, string>();
        private readonly bool _enableCaching;

        public LocalPackageRepository(string physicalPath)
            : this(physicalPath, enableCaching: true)
        {
        }

        public LocalPackageRepository(string physicalPath, bool enableCaching)
            : this(new DefaultPackagePathResolver(physicalPath),
                   new PhysicalFileSystem(physicalPath),
                   enableCaching)
        {
        }

        public LocalPackageRepository(IPackagePathResolver pathResolver, IFileSystem fileSystem)
            : this(pathResolver, fileSystem, enableCaching: true)
        {
        }

        public LocalPackageRepository(IPackagePathResolver pathResolver, IFileSystem fileSystem, bool enableCaching)
        {
            if (pathResolver == null)
            {
                throw new ArgumentNullException("pathResolver");
            }

            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            FileSystem = fileSystem;
            PathResolver = pathResolver;
            _enableCaching = enableCaching;
        }

        public override string Source
        {
            get
            {
                return FileSystem.Root;
            }
        }

        public IPackagePathResolver PathResolver
        {
            get;
            set;
        }

        public override bool SupportsPrereleasePackages
        {
            get { return true; }
        }

        protected IFileSystem FileSystem
        {
            get;
            private set;
        }

        public override IQueryable<IPackage> GetPackages()
        {
            return GetPackages(OpenPackage).AsQueryable();
        }

        public override void AddPackage(IPackage package)
        {
            if (PackageSaveMode.HasFlag(PackageSaveModes.Nuspec))
            {
                // Starting from 2.1, we save the nuspec file into the subdirectory with the name as <packageId>.<version>
                // for example, for jQuery version 1.0, it will be "jQuery.1.0\\jQuery.1.0.nuspec"
                string packageFilePath = GetManifestFilePath(package.Id, package.Version);
                Manifest manifest = Manifest.Create(package);

                // The IPackage object doesn't carry the References information.
                // Thus we set the References for the manifest to the set of all valid assembly references
                manifest.Metadata.ReferenceSets = package.AssemblyReferences
                                                      .GroupBy(f => f.TargetFramework)
                                                      .Select(
                                                        g => new ManifestReferenceSet
                                                        {
                                                            TargetFramework = g.Key == null ? null : VersionUtility.GetFrameworkString(g.Key),
                                                            References = g.Select(p => new ManifestReference { File = p.Name }).ToList()
                                                        })
                                                      .ToList();

                FileSystem.AddFileWithCheck(packageFilePath, manifest.Save);
            }

            if (PackageSaveMode.HasFlag(PackageSaveModes.Nupkg))
            {
                string packageFilePath = GetPackageFilePath(package);

                FileSystem.AddFileWithCheck(packageFilePath, package.GetStream);
            }
        }

        public override void RemovePackage(IPackage package)
        {
            string manifestFilePath = GetManifestFilePath(package.Id, package.Version);
            if (FileSystem.FileExists(manifestFilePath))
            {
                // delete .nuspec file
                FileSystem.DeleteFileSafe(manifestFilePath);
            }

            // Delete the package file
            string packageFilePath = GetPackageFilePath(package);
            FileSystem.DeleteFileSafe(packageFilePath);

            // Delete the package directory if any
            FileSystem.DeleteDirectorySafe(PathResolver.GetPackageDirectory(package), recursive: false);

            // If this is the last package delete the package directory
            if (!FileSystem.GetFilesSafe(String.Empty).Any() &&
                !FileSystem.GetDirectoriesSafe(String.Empty).Any())
            {
                FileSystem.DeleteDirectorySafe(String.Empty, recursive: false);
            }
        }

        public virtual IPackage FindPackage(string packageId, SemanticVersion version)
        {
            if (String.IsNullOrEmpty(packageId))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "packageId");
            }
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }
            return FindPackage(OpenPackage, packageId, version);
        }

        public virtual IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            if (String.IsNullOrEmpty(packageId))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "packageId");
            }

            return FindPackagesById(OpenPackage, packageId);
        }

        public virtual bool Exists(string packageId, SemanticVersion version)
        {
            return FindPackage(packageId, version) != null;
        }

        public virtual IEnumerable<string> GetPackageLookupPaths(string packageId, SemanticVersion version)
        {
            // Files created by the path resolver. This would take into account the non-side-by-side scenario 
            // and we do not need to match this for id and version.
            var packageFileName = PathResolver.GetPackageFileName(packageId, version);
            var manifestFileName = Path.ChangeExtension(packageFileName, Constants.ManifestExtension);
            var filesMatchingFullName = Enumerable.Concat(
                GetPackageFiles(packageFileName), 
                GetPackageFiles(manifestFileName));

            if (version != null && version.Version.Revision < 1)
            {
                // If the build or revision number is not set, we need to look for combinations of the format
                // * Foo.1.2.nupkg
                // * Foo.1.2.3.nupkg
                // * Foo.1.2.0.nupkg
                // * Foo.1.2.0.0.nupkg
                // To achieve this, we would look for files named 1.2*.nupkg if both build and revision are 0 and
                // 1.2.3*.nupkg if only the revision is set to 0.
                string partialName = version.Version.Build < 1 ?
                                        String.Join(".", packageId, version.Version.Major, version.Version.Minor) :
                                        String.Join(".", packageId, version.Version.Major, version.Version.Minor, version.Version.Build);
                string partialManifestName = partialName + "*" + Constants.ManifestExtension;
                partialName += "*" + Constants.PackageExtension;

                // Partial names would result is gathering package with matching major and minor but different build and revision. 
                // Attempt to match the version in the path to the version we're interested in.
                var partialNameMatches = GetPackageFiles(partialName).Where(path => FileNameMatchesPattern(packageId, version, path));
                var partialManifestNameMatches = GetPackageFiles(partialManifestName).Where(
                    path => FileNameMatchesPattern(packageId, version, path));
                return Enumerable.Concat(filesMatchingFullName, partialNameMatches).Concat(partialManifestNameMatches);
            }
            return filesMatchingFullName;
        }

        internal IPackage FindPackage(Func<string, IPackage> openPackage, string packageId, SemanticVersion version)
        {
            var lookupPackageName = new PackageName(packageId, version);
            string packagePath;
            // If caching is enabled, check if we have a cached path. Additionally, verify that the file actually exists on disk since it might have moved.
            if (_enableCaching &&
                _packagePathLookup.TryGetValue(lookupPackageName, out packagePath) &&
                FileSystem.FileExists(packagePath))
            {
                // When depending on the cached path, verify the file exists on disk.
                return GetPackage(openPackage, packagePath);
            }

            // Lookup files which start with the name "<Id>." and attempt to match it with all possible version string combinations (e.g. 1.2.0, 1.2.0.0) 
            // before opening the package. To avoid creating file name strings, we attempt to specifically match everything after the last path separator
            // which would be the file name and extension.
            return (from path in GetPackageLookupPaths(packageId, version)
                    let package = GetPackage(openPackage, path)
                    where lookupPackageName.Equals(new PackageName(package.Id, package.Version))
                    select package).FirstOrDefault();
        }

        internal IEnumerable<IPackage> FindPackagesById(Func<string, IPackage> openPackage, string packageId)
        {
            Debug.Assert(!String.IsNullOrEmpty(packageId), "The caller has to ensure packageId is never null.");

            HashSet<IPackage> packages = new HashSet<IPackage>(PackageEqualityComparer.IdAndVersion);

            // get packages through nupkg files
            packages.AddRange(
                GetPackages(
                    openPackage, 
                    packageId, 
                    GetPackageFiles(packageId + "*" + Constants.PackageExtension)));

            // then, get packages through nuspec files
            packages.AddRange(
                GetPackages(
                    openPackage, 
                    packageId, 
                    GetPackageFiles(packageId + "*" + Constants.ManifestExtension)));
            return packages;
        }

        internal IEnumerable<IPackage> GetPackages(Func<string, IPackage> openPackage, 
            string packageId,
            IEnumerable<string> packagePaths)
        {
            foreach (var path in packagePaths)
            {
                IPackage package = null;
                try 
                {
                    package = GetPackage(openPackage, path);
                }
                catch (InvalidOperationException)
                {
                    // ignore error for unzipped packages (nuspec files).
                    if (string.Equals(
                        Constants.ManifestExtension, 
                        Path.GetExtension(path), 
                        StringComparison.OrdinalIgnoreCase))
                    {                        
                    }
                    else 
                    {
                        throw;
                    }
                }

                if (package != null && package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                {
                    yield return package;
                }
            }
        }

        internal IEnumerable<IPackage> GetPackages(Func<string, IPackage> openPackage)
        {
            return from path in GetPackageFiles()
                   select GetPackage(openPackage, path);
        }

        private IPackage GetPackage(Func<string, IPackage> openPackage, string path)
        {
            PackageCacheEntry cacheEntry;
            DateTimeOffset lastModified = FileSystem.GetLastModified(path);
            // If we never cached this file or we did and it's current last modified time is newer
            // create a new entry
            if (!_packageCache.TryGetValue(path, out cacheEntry) ||
                (cacheEntry != null && lastModified > cacheEntry.LastModifiedTime))
            {
                // We need to do this so we capture the correct loop variable
                string packagePath = path;

                // Create the package
                IPackage package = openPackage(packagePath);

                // create a cache entry with the last modified time
                cacheEntry = new PackageCacheEntry(package, lastModified);

                if (_enableCaching)
                {
                    // Store the entry
                    _packageCache[packagePath] = cacheEntry;
                    _packagePathLookup.GetOrAdd(new PackageName(package.Id, package.Version), path);
                }
            }

            return cacheEntry.Package;
        }

        internal IEnumerable<string> GetPackageFiles(string filter = null)
        {
            filter = filter ?? "*" + Constants.PackageExtension;
            Debug.Assert(
                filter.EndsWith(Constants.PackageExtension, StringComparison.OrdinalIgnoreCase) ||
                filter.EndsWith(Constants.ManifestExtension, StringComparison.OrdinalIgnoreCase));

            // Check for package files one level deep. We use this at package install time
            // to determine the set of installed packages. Installed packages are copied to 
            // {id}.{version}\{packagefile}.{extension}.
            foreach (var dir in FileSystem.GetDirectories(String.Empty))
            {
                foreach (var path in FileSystem.GetFiles(dir, filter))
                {
                    yield return path;
                }
            }

            // Check top level directory
            foreach (var path in FileSystem.GetFiles(String.Empty, filter))
            {
                yield return path;
            }
        }

        protected virtual IPackage OpenPackage(string path)
        {
            if (!FileSystem.FileExists(path))
            {
                return null;
            }

            if (Path.GetExtension(path) == Constants.PackageExtension)
            {
                OptimizedZipPackage package;
                try
                {
                    package = new OptimizedZipPackage(FileSystem, path);
                }
                catch (FileFormatException ex)
                {
                    throw new InvalidDataException(String.Format(CultureInfo.CurrentCulture, NuGetResources.ErrorReadingPackage, path), ex);
                }
                // Set the last modified date on the package
                package.Published = FileSystem.GetLastModified(path);

                return package;
            }
            else if (Path.GetExtension(path) == Constants.ManifestExtension)
            {
                if (FileSystem.FileExists(path))
                {
                    return new UnzippedPackage(FileSystem, Path.GetFileNameWithoutExtension(path));
                }
            }

            return null;
        }

        protected virtual string GetPackageFilePath(IPackage package)
        {
            return Path.Combine(PathResolver.GetPackageDirectory(package),
                                PathResolver.GetPackageFileName(package));
        }

        protected virtual string GetPackageFilePath(string id, SemanticVersion version)
        {
            return Path.Combine(PathResolver.GetPackageDirectory(id, version),
                                PathResolver.GetPackageFileName(id, version));
        }

        private static bool FileNameMatchesPattern(string packageId, SemanticVersion version, string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            SemanticVersion parsedVersion;

            // When matching by pattern, we will always have a version token. Packages without versions would be matched early on by the version-less path resolver 
            // when doing an exact match.
            return name.Length > packageId.Length &&
                   SemanticVersion.TryParse(name.Substring(packageId.Length + 1), out parsedVersion) &&
                   parsedVersion == version;
        }

        private string GetManifestFilePath(string packageId, SemanticVersion version)
        {
            string packageDirectory = PathResolver.GetPackageDirectory(packageId, version);
            string manifestFileName = packageDirectory + Constants.ManifestExtension;

            return Path.Combine(packageDirectory, manifestFileName);
        }

        private class PackageCacheEntry
        {
            public PackageCacheEntry(IPackage package, DateTimeOffset lastModifiedTime)
            {
                Package = package;
                LastModifiedTime = lastModifiedTime;
            }

            public IPackage Package { get; private set; }
            public DateTimeOffset LastModifiedTime { get; private set; }
        }
    }
}