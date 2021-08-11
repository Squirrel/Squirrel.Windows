using System;
using System.Runtime.Versioning;

namespace NuGet
{
    public class PackageReference : IEquatable<PackageReference>
    {
        public PackageReference(string id, SemanticVersion version, IVersionSpec versionConstraint, FrameworkName targetFramework, bool isDevelopmentDependency, bool requireReinstallation = false)
        {
            Id = id;
            Version = version;
            VersionConstraint = versionConstraint;
            TargetFramework = targetFramework;
            IsDevelopmentDependency = isDevelopmentDependency;
            RequireReinstallation = requireReinstallation;
        }

        public string Id { get; private set; }
        public SemanticVersion Version { get; private set; }
        public IVersionSpec VersionConstraint { get; set; }
        public FrameworkName TargetFramework { get; private set; }
        public bool IsDevelopmentDependency { get; private set; }
        public bool RequireReinstallation { get; private set; }

        public override bool Equals(object obj)
        {
            var reference = obj as PackageReference;
            if (reference != null)
            {
                return Equals(reference);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode() * 3137 + (Version == null ? 0 : Version.GetHashCode());
        }

        public override string ToString()
        {
            if (Version == null)
            {
                return Id;
            }
            if (VersionConstraint == null)
            {
                return Id + " " + Version;
            }
            return Id + " " + Version + " (" + VersionConstraint + ")";
        }

        public bool Equals(PackageReference other)
        {
            return Id.Equals(other.Id, StringComparison.OrdinalIgnoreCase) &&
                   Version == other.Version;
        }
    }
}
