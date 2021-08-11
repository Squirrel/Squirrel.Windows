using System;
using System.Collections.Generic;

namespace NuGet
{
    public class DefaultConstraintProvider : IPackageConstraintProvider
    {
        private readonly Dictionary<string, IVersionSpec> _constraints = new Dictionary<string, IVersionSpec>(StringComparer.OrdinalIgnoreCase);

        public string Source
        {
            get
            {
                return String.Empty;
            }
        }

        public void AddConstraint(string packageId, IVersionSpec versionSpec)
        {
            _constraints[packageId] = versionSpec;
        }

        public IVersionSpec GetConstraint(string packageId)
        {
            IVersionSpec versionSpec;
            if (_constraints.TryGetValue(packageId, out versionSpec))
            {
                return versionSpec;
            }
            return null;
        }
    }
}
