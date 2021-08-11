using System;

namespace NuGet
{
    [Flags]
    public enum PackageTargets
    {
        None = 0,
        Project = 1,

        // Indicates that the package is a solution level package
        External = 2, 

        All = Project | External
    }
}
