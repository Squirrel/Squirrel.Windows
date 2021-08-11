using System.Collections.Generic;

namespace NuGet
{
    public interface IMachineWideSettings
    {
        IEnumerable<Settings> Settings { get; }
    }
}
