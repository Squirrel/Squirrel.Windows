using System;
using System.Collections.Generic;

namespace NuGet
{
    public interface IPackageSourceProvider
    {
        IEnumerable<PackageSource> LoadPackageSources();

        event EventHandler PackageSourcesSaved;
        void SavePackageSources(IEnumerable<PackageSource> sources);
        void DisablePackageSource(PackageSource source);
        bool IsPackageSourceEnabled(PackageSource source);
    }
}