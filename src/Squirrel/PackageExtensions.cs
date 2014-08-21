using System;
using System.Linq;
using NuGet;

namespace Squirrel
{
    public static class PackageExtensions
    {
        public static string ExtractTitle(this IPackage package)
        {
            if (package == null) return String.Empty;

            var title = package.Title;
            return !String.IsNullOrWhiteSpace(title) ? title : package.Id;
        }

        public static FrameworkVersion DetectFrameworkVersion(this IPackage package)
        {
            return package.GetFiles().Any(x => x.Path.Contains("lib") && x.Path.Contains("45")) ? 
                FrameworkVersion.Net45 : FrameworkVersion.Net40;
        }
    }
}
