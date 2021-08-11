using System.Collections.Generic;

namespace NuGet
{
    public interface IPackageRule
    {
        IEnumerable<PackageIssue> Validate(IPackage package);
    }
}