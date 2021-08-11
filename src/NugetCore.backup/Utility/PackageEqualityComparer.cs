using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NuGet
{
    public sealed class PackageEqualityComparer : IEqualityComparer<IPackageName>
    {
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes", Justification = "This type isn't mutable")]
        public static readonly PackageEqualityComparer IdAndVersion = new PackageEqualityComparer((x, y) => x.Id.Equals(y.Id, StringComparison.OrdinalIgnoreCase) &&
                                                                                                                    x.Version.Equals(y.Version),
                                                                                                   x => x.Id.GetHashCode() ^ x.Version.GetHashCode());

        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes", Justification = "This type isn't mutable")]
        public static readonly PackageEqualityComparer Id = new PackageEqualityComparer((x, y) => x.Id.Equals(y.Id, StringComparison.OrdinalIgnoreCase),
                                                                                         x => x.Id.GetHashCode());

        private readonly Func<IPackageName, IPackageName, bool> _equals;
        private readonly Func<IPackageName, int> _getHashCode;

        private PackageEqualityComparer(Func<IPackageName, IPackageName, bool> equals, Func<IPackageName, int> getHashCode)
        {
            _equals = equals;
            _getHashCode = getHashCode;
        }

        public bool Equals(IPackageName x, IPackageName y)
        {
            return _equals(x, y);
        }

        public int GetHashCode(IPackageName obj)
        {
            return _getHashCode(obj);
        }
    }
}