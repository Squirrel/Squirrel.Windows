using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NuGet
{
    public class UnzippedPackageRepository : PackageRepositoryBase, IPackageLookup
    {
        public UnzippedPackageRepository(string physicalPath)
            : this(new DefaultPackagePathResolver(physicalPath), new PhysicalFileSystem(physicalPath))
        {
        }

        public UnzippedPackageRepository(IPackagePathResolver pathResolver, IFileSystem fileSystem)
        {
            FileSystem = fileSystem;
            PathResolver = pathResolver;
        }

        protected IFileSystem FileSystem
        {
            get;
            private set;
        }

        internal IPackagePathResolver PathResolver
        {
            get;
            set;
        }

        public override string Source
        {
            get { return FileSystem.Root; }
        }

        public override bool SupportsPrereleasePackages
        {
            get { return true; }
        }

        public override IQueryable<IPackage> GetPackages()
        {
            return (from file in FileSystem.GetFiles("", "*" + Constants.PackageExtension)
                    let packageName = Path.GetFileNameWithoutExtension(file)
                    where FileSystem.DirectoryExists(packageName)
                    select new UnzippedPackage(FileSystem, packageName)).AsQueryable();
        }

        public IPackage FindPackage(string packageId, SemanticVersion version)
        {
            string packageName = GetPackageFileName(packageId, version); 
            if (Exists(packageId, version))
            {
                return new UnzippedPackage(FileSystem, packageName);
            }
            return null;
        }

        public IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            return GetPackages().Where(p => p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        }

        public bool Exists(string packageId, SemanticVersion version)
        {
            string packageName = GetPackageFileName(packageId, version);
            string packageFile = packageName + Constants.PackageExtension;
            return FileSystem.FileExists(packageFile) && FileSystem.DirectoryExists(packageName);
        }

        private static string GetPackageFileName(string packageId, SemanticVersion version)
        {
            return packageId + "." + version.ToString();
        }
    }
}