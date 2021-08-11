using System.Collections.Generic;

namespace NuGet
{
    public interface IPackageOperationResolver
    {
        IEnumerable<PackageOperation> ResolveOperations(IPackage package);
    }
}
