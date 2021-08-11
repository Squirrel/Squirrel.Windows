using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Versioning;

namespace NuGet
{
    public class PackageReferenceSet : IFrameworkTargetable
    {
        private readonly FrameworkName _targetFramework;
        private readonly ICollection<string> _references;

        public PackageReferenceSet(FrameworkName targetFramework, IEnumerable<string> references)
        {
            if (references == null)
            {
                throw new ArgumentNullException("references");
            }

            _targetFramework = targetFramework;
            _references = new ReadOnlyCollection<string>(references.ToList());
        }

        public PackageReferenceSet(ManifestReferenceSet manifestReferenceSet)
        {
            if (manifestReferenceSet == null) 
            {
                throw new ArgumentNullException("manifestReferenceSet");
            }

            if (!String.IsNullOrEmpty(manifestReferenceSet.TargetFramework))
            {
                _targetFramework = VersionUtility.ParseFrameworkName(manifestReferenceSet.TargetFramework);
            }

            _references = new ReadOnlyHashSet<string>(manifestReferenceSet.References.Select(r => r.File), StringComparer.OrdinalIgnoreCase);
        }

        public ICollection<string> References
        {
            get
            {
                return _references;
            }
        }

        public FrameworkName TargetFramework
        {
            get { return _targetFramework; }
        }

        public IEnumerable<FrameworkName> SupportedFrameworks
        {
            get
            {
                if (TargetFramework == null)
                {
                    yield break;
                }

                yield return TargetFramework;
            }
        }
    }
}