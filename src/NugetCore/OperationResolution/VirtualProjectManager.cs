using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGet.Resolver
{
    public class VirtualProjectManager
    {
        public IProjectManager ProjectManager { get; private set; }

        public VirtualRepository LocalRepository { get; private set; }

        public VirtualProjectManager(IProjectManager projectManager)
        {
            ProjectManager = projectManager;
            LocalRepository = new VirtualRepository(projectManager.LocalRepository);
        }
    }

    public class VirtualRepository : IPackageRepository
    {
        HashSet<IPackage> _packages;

        public VirtualRepository(IPackageRepository repo)
        {
            _packages = new HashSet<IPackage>(PackageEqualityComparer.IdAndVersion);
            if (repo != null)
            {
                _packages.AddRange(repo.GetPackages());
            }
        }

        public string Source
        {
            get { return string.Empty; }
        }

        public PackageSaveModes PackageSaveMode
        {
            get;
            set;
        }

        public bool SupportsPrereleasePackages
        {
            get { return true; }
        }

        public IQueryable<IPackage> GetPackages()
        {
            return _packages.AsQueryable();
        }

        public void AddPackage(IPackage package)
        {
            _packages.Add(package);
        }

        public void RemovePackage(IPackage package)
        {
            _packages.Remove(package);
        }
    }
}
