using System;
using System.Linq;

namespace NuGet
{
    public abstract class PackageRepositoryBase : IPackageRepository
    {
        private PackageSaveModes _packageSave;

        protected PackageRepositoryBase()
        {
            _packageSave = PackageSaveModes.Nupkg;
        }

        public abstract string Source { get; }


        public PackageSaveModes PackageSaveMode 
        {
            get { return _packageSave; }
            set
            {
                if (value == PackageSaveModes.None)
                {
                    throw new ArgumentException("PackageSave cannot be set to None");
                }

                _packageSave = value;
            }
        }

        public abstract IQueryable<IPackage> GetPackages();

        public abstract bool SupportsPrereleasePackages { get; }

        public virtual void AddPackage(IPackage package)
        {
            throw new NotSupportedException();
        }

        public virtual void RemovePackage(IPackage package)
        {
            throw new NotSupportedException();
        }
    }
}
