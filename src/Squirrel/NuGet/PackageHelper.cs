using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace Squirrel.NuGet
{
    public static class PackageHelper
    {
        public static bool IsManifest(string path)
        {
            return Path.GetExtension(path).Equals(Constants.ManifestExtension, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPackageFile(string path)
        {
            return Path.GetExtension(path).Equals(Constants.PackageExtension, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAssembly(string path)
        {
            return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".winmd", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
