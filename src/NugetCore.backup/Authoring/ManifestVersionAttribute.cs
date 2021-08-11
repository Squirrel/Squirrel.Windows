using System;

namespace NuGet
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    internal sealed class ManifestVersionAttribute : Attribute
    {
        public ManifestVersionAttribute(int version)
        {
            Version = version;

        }
        public int Version { get; private set; }
    }
}
