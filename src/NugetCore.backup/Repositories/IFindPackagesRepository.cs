using System.Collections.Generic;

namespace NuGet
{
    public interface IFindPackagesRepository
    {
        IEnumerable<IPackage> FindPackagesById(string packageId);
    }
}
