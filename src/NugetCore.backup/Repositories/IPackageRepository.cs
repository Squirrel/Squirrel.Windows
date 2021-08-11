using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace NuGet
{
    [Flags]
    public enum PackageSaveModes
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

    public interface IPackageRepository
    {
        string Source { get; }        
        PackageSaveModes PackageSaveMode { get; set; }
        bool SupportsPrereleasePackages { get; }
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "This call might be expensive")]
        IQueryable<IPackage> GetPackages();

        // Which files (nuspec/nupkg) are saved is controlled by property PackageSaveMode.
        void AddPackage(IPackage package);
        void RemovePackage(IPackage package);
    }
}