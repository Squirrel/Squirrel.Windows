using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace NuGet
{
    /// <summary>
    /// The default comparer of FrameworkName performs case-sensitive comparison. This class does case-insensitive comparison.
    /// </summary>
    internal class FrameworkNameEqualityComparer : IEqualityComparer<FrameworkName>
    {
        public static readonly FrameworkNameEqualityComparer Default = new FrameworkNameEqualityComparer();

        private FrameworkNameEqualityComparer()
        {
        }

        public bool Equals(FrameworkName x, FrameworkName y)
        {
            return String.Equals(x.Identifier, y.Identifier, StringComparison.OrdinalIgnoreCase) &&
                   x.Version == y.Version &&
                   String.Equals(x.Profile, y.Profile, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(FrameworkName x)
        {
            return x.GetHashCode();
        }
    }
}
