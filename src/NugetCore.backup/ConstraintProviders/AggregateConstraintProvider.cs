using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGet
{
    public class AggregateConstraintProvider : IPackageConstraintProvider
    {
        private readonly IEnumerable<IPackageConstraintProvider> _constraintProviders;
        public AggregateConstraintProvider(params IPackageConstraintProvider[] constraintProviders)
        {
            if (constraintProviders.IsEmpty() || constraintProviders.Any(cp => cp == null))
            {
                throw new ArgumentNullException("constraintProviders");
            }
            _constraintProviders = constraintProviders;
        }

        public string Source
        {
            get
            {
                return String.Join(", ", _constraintProviders.Select(cp => cp.Source));
            }
        }

        public IVersionSpec GetConstraint(string packageId)
        {
            return _constraintProviders.Select(cp => cp.GetConstraint(packageId))
                                       .FirstOrDefault(constraint => constraint != null);
        }
    }
}
