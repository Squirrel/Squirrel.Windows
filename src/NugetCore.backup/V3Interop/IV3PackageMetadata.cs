using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.V3Interop
{
    public interface IV3PackageMetadata : IPackageMetadata
    {
        PackageTargets PackageTarget { get; }
    }
}
