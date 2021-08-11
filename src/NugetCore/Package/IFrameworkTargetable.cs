using System.Collections.Generic;
using System.Runtime.Versioning;

namespace NuGet
{
    public interface IFrameworkTargetable
    {
        IEnumerable<FrameworkName> SupportedFrameworks { get; }
    }
}
