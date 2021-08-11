using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.Resources;

namespace NuGet.Analysis.Rules
{
    internal class InvalidFrameworkFolderRule : IPackageRule
    {
        public IEnumerable<PackageIssue> Validate(IPackage package)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in package.GetFiles())
            {
                string path = file.Path;
                string[] parts = path.Split(Path.DirectorySeparatorChar);
                if (parts.Length >= 3 && parts[0].Equals(Constants.LibDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(parts[1]);
                }
            }

            return set.Where(s => !IsValidFrameworkName(s) && !IsValidCultureName(package, s))
                      .Select(CreatePackageIssue);
        }

        private static bool IsValidFrameworkName(string name)
        {
            FrameworkName fx;
            try
            {
                fx = VersionUtility.ParseFrameworkName(name);
            }
            catch (ArgumentException) 
            {
                fx = VersionUtility.UnsupportedFrameworkName;
            }

            return fx != VersionUtility.UnsupportedFrameworkName;
        }

        private static bool IsValidCultureName(IPackage package, string name)
        {
            // starting from NuGet 1.8, we support localized packages, which 
            // can have a culture folder under lib, e.g. lib\fr-FR\strings.resources.dll

            if (String.IsNullOrEmpty(package.Language))
            {
                return false;
            }

            // the folder name is considered valid if it matches the package's Language property.
            return name.Equals(package.Language, StringComparison.OrdinalIgnoreCase);
        }

        private PackageIssue CreatePackageIssue(string target)
        {
            return new PackageIssue(
                AnalysisResources.InvalidFrameworkTitle,
                String.Format(CultureInfo.CurrentCulture, AnalysisResources.InvalidFrameworkDescription, target),
                AnalysisResources.InvalidFrameworkSolution
            );
        }
    }
}