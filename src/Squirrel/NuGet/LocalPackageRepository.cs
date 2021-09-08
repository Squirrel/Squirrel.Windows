using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace NuGet
{
    [Flags]
    internal enum PackageSaveModes
    {
        None = 0,
        Nuspec = 1,

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Naming",
            "CA1704:IdentifiersShouldBeSpelledCorrectly",
            MessageId = "Nupkg",
            Justification = "nupkg is the file extension of the package file")]
        Nupkg = 2
    }

    internal interface IPackageRepository
    {
        string Source { get; }
        IEnumerable<IPackage> FindPackagesById(string packageId);
    }

    internal class LocalPackageRepository : IPackageRepository
    {
        public LocalPackageRepository(string packagesDir)
        {
        }

        public string Source => throw new NotImplementedException();

        public IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            throw new NotImplementedException();
        }
    }
}
