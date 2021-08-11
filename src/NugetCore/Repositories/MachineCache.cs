using NuGet.Resources;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;

namespace NuGet
{
    /// <summary>
    /// The machine cache represents a location on the machine where packages are cached. It is a specific implementation of a local repository and can be used as such.
    /// NOTE: this is a shared location, and as such all IO operations need to be properly serialized
    /// </summary>
    public class MachineCache : LocalPackageRepository, IPackageCacheRepository
    {
        /// <summary>
        /// Maximum number of packages that can live in this cache.
        /// </summary>
        private const int MaxPackages = 200;

        private const string NuGetCachePathEnvironmentVariable = "NuGetCachePath";

        private static readonly Lazy<MachineCache> _instance = new Lazy<MachineCache>(() => CreateDefault(GetCachePath));

        internal MachineCache(IFileSystem fileSystem)
            : base(new DefaultPackagePathResolver(fileSystem), fileSystem, enableCaching: false)
        {
        }

        public static MachineCache Default
        {
            get { return _instance.Value; }
        }

        /// <summary>
        /// Creates a Machine Cache instance, assigns it to the instance variable and returns it.
        /// </summary>
        internal static MachineCache CreateDefault(Func<string> getCachePath)
        {
            IFileSystem fileSystem;
            try
            {
                string path = getCachePath();
                if (String.IsNullOrEmpty(path))
                {
                    // If we don't get a path, use a null file system to make the cache object do nothing
                    // This can happen when there is no LocalApplicationData folder
                    fileSystem = NullFileSystem.Instance;
                }
                else
                {
                    fileSystem = new PhysicalFileSystem(path);
                }
            }
            catch (SecurityException)
            {
                // We are unable to access the special directory. Create a machine cache using an empty file system
                fileSystem = NullFileSystem.Instance;
            }
            return new MachineCache(fileSystem);
        }

        public override void AddPackage(IPackage package)
        {
            // If we exceed the package count then clear the cache.
            var files = GetPackageFiles().ToList();
            if (files.Count >= MaxPackages)
            {
                // It's expensive to hit the file system to get the last accessed date for files
                // To reduce this cost from occuring frequently, we'll purge packages in batches allowing for a 20% buffer.
                var filesToDelete = files.OrderBy(FileSystem.GetLastAccessed)
                                         .Take(files.Count - (int)(0.8 * MaxPackages))
                                         .ToList();
                TryClear(filesToDelete);
            }

            string path = GetPackageFilePath(package);
            TryAct(() =>
                {
                    // we want to do this in the TryAct, i.e. in the mutex
                    // for cases where package was added to cache by another process
                    if (FileSystem.FileExists(path))
                    {
                        return true;
                    }
                    string tmp = GetTempFile(path);
                    using (var stream = package.GetStream())
                    {
                        FileSystem.AddFile(tmp, stream);
                    }
                    FileSystem.MoveFile(tmp, path);
                    return true;
                }, path);
        }

        // Unfortunately, there are many locations that query directly the filesystem to
        // assess if a package is present in the cache instead of calling into MachineCache.Exists
        // To guard against file in use issues, we create the cache entry with a tmp name and 
        // rename when the file is ready for consumption.
        private static string GetTempFile(string filename)
        {
            return filename + ".tmp";
        }

        public override bool Exists(string packageId, SemanticVersion version)
        {
            string packagePath = GetPackageFilePath(packageId, version);
            return TryAct(() => FileSystem.FileExists(packagePath), packagePath);
        }

        public bool InvokeOnPackage(string packageId, SemanticVersion version, Action<Stream> action)
        {
            if (FileSystem is NullFileSystem)
            {
                return false;
            }

            string packagePath = GetPackageFilePath(packageId, version);
            return TryAct(() =>
                {
                    string tmp = GetTempFile(packagePath);
                    using (var stream = FileSystem.CreateFile(tmp))
                    {
                        if (stream == null)
                        {
                            return false;
                        }
                        action(stream);

                        // After downloading a package, check if it is an empty package
                        // If so, do not store it in the machine cache
                        if (stream == null || stream.Length == 0)
                        {
                            return false;
                        }
                    }

                    // fix up the package name if the nuspec gives the version in a different format
                    // this allows versions to be normalized on the server while still supporting legacy formats for package restore
                    IPackage package = OpenPackage(FileSystem.GetFullPath(tmp));

                    // for legacy support the package name needs to match the nuspec
                    // Ex: owin.1.0.0.nupkg -> Owin.1.0.nupkg
                    packagePath = GetPackageFilePath(package.Id, package.Version);

                    FileSystem.DeleteFile(packagePath);
                    FileSystem.MoveFile(tmp, packagePath);

                    return true;
                }, packagePath);
        }

        protected override IPackage OpenPackage(string path)
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

            return package;
        }

        public void Clear()
        {
            TryClear(GetPackageFiles().ToList());
        }

        private void TryClear(IEnumerable<string> files)
        {
            foreach (var packageFile in files)
            {
                TryAct(() => 
                { 
                    FileSystem.DeleteFileSafe(packageFile);
                    return true;
                }, packageFile);
            }
        }

        protected override string GetPackageFilePath(IPackage package)
        {
            return Path.GetFileName(base.GetPackageFilePath(package));
        }

        protected override string GetPackageFilePath(string id, SemanticVersion version)
        {
            return Path.GetFileName(base.GetPackageFilePath(id, version));
        }

        /// <summary>
        /// Determines the cache path to use for NuGet.exe. By default, NuGet caches files under %LocalAppData%\NuGet\Cache.
        /// This path can be overridden by specifying a value in the NuGetCachePath environment variable.
        /// </summary>
        internal static string GetCachePath()
        {
            return GetCachePath(Environment.GetEnvironmentVariable, Environment.GetFolderPath);
        }

        internal static string GetCachePath(Func<string, string> getEnvironmentVariable, Func<System.Environment.SpecialFolder, string> getFolderPath)
        {
            string cacheOverride = getEnvironmentVariable(NuGetCachePathEnvironmentVariable);
            if (!String.IsNullOrEmpty(cacheOverride))
            {
                return cacheOverride;
            }
            else
            {
                string localAppDataPath = getFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (String.IsNullOrEmpty(localAppDataPath))
                {
                    // there's a bug on Windows Azure Web Sites environment where calling through the Environment.GetFolderPath()
                    // will returns empty string, but the environment variable will return the correct value
                    localAppDataPath = getEnvironmentVariable("LocalAppData");
                }

                if (String.IsNullOrEmpty(localAppDataPath))
                {
                    return null;
                }
                return Path.Combine(localAppDataPath, "NuGet", "Cache");
            }
        }

        /// <remarks>
        /// We use this method instead of the "safe" methods in FileSystem because it attempts to retry multiple times with delays.
        /// In our case, if we are unable to perform IO over the machine cache, we want to quit trying immediately.
        /// </remarks>
        private bool TryAct(Func<bool> action, string path)
        {
            try
            {
                // Global: machine cache is per user across TS sessions
                var mutexName = "Global\\" + EncryptionUtility.GenerateUniqueToken(FileSystem.GetFullPath(path) ?? path);
                using (var mutex = new Mutex(false, mutexName))
                {
                    bool owner = false;
                    try
                    {
                        try
                        {
                            owner = mutex.WaitOne(TimeSpan.FromMinutes(3));
                            // ideally we should throw an exception here if !owner such as
                            // throw new TimeoutException(string.Format("Timeout waiting for Machine Cache mutex for {0}", fullPath));
                            // we decided against it: machine cache operations being "best effort" basis.
                            // this may cause "File in use" exceptions for long lasting operations such as downloading a large package on 
                            // a slow network connection
                        }
                        catch (AbandonedMutexException)
                        {
                            // TODO: consider logging a warning; abandonning a mutex is an indication something wrong is going on
                            owner = true; // now mine
                        }

                        return action();
                    }
                    finally
                    {
                        if (owner)
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
                // Do nothing if this fails. 
            }
            return false;
        }
    }
}