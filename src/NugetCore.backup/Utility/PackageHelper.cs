using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.Resources;

namespace NuGet
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "3#", Justification="We need to return the runtime package.")]
        public static bool IsSatellitePackage(
            IPackageMetadata package, 
            IPackageRepository repository,
            FrameworkName targetFramework,
            out IPackage runtimePackage)
        {
            // A satellite package has the following properties:
            //     1) A package suffix that matches the package's language, with a dot preceding it
            //     2) A dependency on the package with the same Id minus the language suffix
            //     3) The dependency can be found by Id in the repository (as its path is needed for installation)
            // Example: foo.ja-jp, with a dependency on foo

            runtimePackage = null;

            if (package.IsSatellitePackage())
            {
                string runtimePackageId = package.Id.Substring(0, package.Id.Length - (package.Language.Length + 1));
                PackageDependency dependency = package.FindDependency(runtimePackageId, targetFramework);

                if (dependency != null)
                {
                    runtimePackage = repository.FindPackage(runtimePackageId, versionSpec: dependency.VersionSpec, allowPrereleaseVersions: true, allowUnlisted: true);
                }
            }

            return runtimePackage != null;
        }

        /// <summary>
        /// Finds a package from the source repository that matches the id and version. 
        /// </summary>
        /// <param name="repository">The repository to find the package in.</param>
        /// <param name="packageId">Id of the package to find.</param>
        /// <param name="version">Version of the package to find.</param>
        /// <exception cref="System.InvalidOperationException">If the specified package cannot be found in the repository.</exception>
        public static IPackage ResolvePackage(IPackageRepository repository, string packageId, SemanticVersion version)
        {
            if (repository == null)
            {
                throw new ArgumentNullException("repository");
            }

            if (String.IsNullOrEmpty(packageId))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "packageId");
            }

            if (version == null)
            {
                throw new ArgumentNullException("version");
            }

            var package = repository.FindPackage(packageId, version);
            if (package == null)
            {
                throw new InvalidOperationException(
                        String.Format(CultureInfo.CurrentCulture,
                        NuGetResources.UnknownPackageSpecificVersion, packageId, version));
            }

            return package;
        }
    }
}
