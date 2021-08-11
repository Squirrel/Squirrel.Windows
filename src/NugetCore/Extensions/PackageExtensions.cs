using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Versioning;

namespace NuGet
{
    public static class PackageExtensions
    {
        private const string TagsProperty = "Tags";
        private static readonly string[] _packagePropertiesToSearch = new[] { "Id", "Description", TagsProperty };

        public static bool IsReleaseVersion(this IPackageName packageMetadata)
        {
            return String.IsNullOrEmpty(packageMetadata.Version.SpecialVersion);
        }

        public static bool IsListed(this IPackage package)
        {
            return package.Listed || package.Published > Constants.Unpublished;
        }

        /// <summary>
        /// A package is deemed to be a satellite package if it has a language property set, the id of the package is of the format [.*].[Language]
        /// and it has at least one dependency with an id that maps to the core package .
        /// </summary>
        public static bool IsSatellitePackage(this IPackageMetadata package)
        {
            if (!String.IsNullOrEmpty(package.Language) &&
                    package.Id.EndsWith('.' + package.Language, StringComparison.OrdinalIgnoreCase))
            {
                // The satellite pack's Id is of the format <Core-Package-Id>.<Language>. Extract the core package id using this.
                // Additionally satellite packages have a strict dependency on the core package
                string corePackageId = package.Id.Substring(0, package.Id.Length - package.Language.Length - 1);
                return package.DependencySets.SelectMany(s => s.Dependencies).Any(
                       d => d.Id.Equals(corePackageId, StringComparison.OrdinalIgnoreCase) &&
                       d.VersionSpec != null &&
                       d.VersionSpec.MaxVersion == d.VersionSpec.MinVersion && d.VersionSpec.IsMaxInclusive && d.VersionSpec.IsMinInclusive);
            }
            return false;
        }

        public static bool IsEmptyFolder(this IPackageFile packageFile)
        {
            return packageFile != null &&
                   Constants.PackageEmptyFileName.Equals(Path.GetFileName(packageFile.Path), StringComparison.OrdinalIgnoreCase);
        }

        public static IEnumerable<IPackage> FindByVersion(this IEnumerable<IPackage> source, IVersionSpec versionSpec)
        {
            if (versionSpec == null)
            {
                throw new ArgumentNullException("versionSpec");
            }

            return source.Where(versionSpec.ToDelegate());
        }

        public static IEnumerable<IPackageFile> GetFiles(this IPackage package, string directory)
        {
            string folderPrefix = directory + Path.DirectorySeparatorChar;
            return package.GetFiles().Where(file => file.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<IPackageFile> GetContentFiles(this IPackage package)
        {
            return package.GetFiles(Constants.ContentDirectory);
        }

        public static IEnumerable<IPackageFile> GetToolFiles(this IPackage package)
        {
            return package.GetFiles(Constants.ToolsDirectory);
        }

        public static IEnumerable<IPackageFile> GetBuildFiles(this IPackage package)
        {
            // build files must be either <package id>.props or <package id>.targets
            string targetsFile = package.Id + ".targets";
            string propsFile = package.Id + ".props";

            return package.GetFiles(Constants.BuildDirectory)
                          .Where(p => targetsFile.Equals(p.EffectivePath, StringComparison.OrdinalIgnoreCase) ||
                                      propsFile.Equals(p.EffectivePath, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<IPackageFile> GetLibFiles(this IPackage package)
        {
            return package.GetFiles(Constants.LibDirectory);
        }

        public static bool HasFileWithNullTargetFramework(this IPackage package)
        {
            return package.GetContentFiles()
                .Concat(package.GetLibFiles())
                .Any(file => file.TargetFramework == null);
        }

        /// <summary>
        /// Returns the list of files from a satellite package that are considered satellite files.
        /// </summary>
        /// <remarks>
        /// This method must only be called for packages that specify a language
        /// </remarks>
        /// <param name="package">The package to get satellite files from.</param>
        /// <returns>The list of satellite files, which may be an empty list.</returns>
        public static IEnumerable<IPackageFile> GetSatelliteFiles(this IPackage package)
        {
            if (String.IsNullOrEmpty(package.Language))
            {
                return Enumerable.Empty<IPackageFile>();
            }

            // Satellite files are those within the Lib folder that have a culture-specific subfolder anywhere in the path
            return package.GetLibFiles().Where(file => Path.GetDirectoryName(file.Path).Split(Path.DirectorySeparatorChar)
                                                                .Contains(package.Language, StringComparer.OrdinalIgnoreCase));
        }

        public static IEnumerable<PackageIssue> Validate(this IPackage package, IEnumerable<IPackageRule> rules)
        {
            if (package == null)
            {
                return null;
            }

            if (rules == null)
            {
                throw new ArgumentNullException("rules");
            }

            return rules.Where(r => r != null).SelectMany(r => r.Validate(package));
        }

        public static string GetHash(this IPackage package, string hashAlgorithm)
        {
            return GetHash(package, new CryptoHashProvider(hashAlgorithm));
        }

        public static string GetHash(this IPackage package, IHashProvider hashProvider)
        {
            using (Stream stream = package.GetStream())
            {
                return Convert.ToBase64String(hashProvider.CalculateHash(stream));
            }
        }

        /// <summary>
        /// Returns true if a package has no content that applies to a project.
        /// </summary>
        public static bool HasProjectContent(this IPackage package)
        {
            // Note that while checking for both AssemblyReferences and LibFiles seems redundant,
            // there are tests that directly inject items into the AssemblyReferences collection
            // without having those files represented in the Lib folder.  We keep the redundant
            // check to play it on the safe side.
            return package.FrameworkAssemblies.Any() ||
                   package.AssemblyReferences.Any() ||
                   package.GetContentFiles().Any() ||
                   package.GetLibFiles().Any() ||
                   package.GetBuildFiles().Any();
        }

        public static IEnumerable<PackageDependency> GetCompatiblePackageDependencies(this IPackageMetadata package, FrameworkName targetFramework)
        {
            IEnumerable<PackageDependencySet> compatibleDependencySets;
            if (targetFramework == null)
            {
                compatibleDependencySets = package.DependencySets;
            }
            else if (!VersionUtility.TryGetCompatibleItems(targetFramework, package.DependencySets, out compatibleDependencySets))
            {
                compatibleDependencySets = new PackageDependencySet[0];
            }

            return compatibleDependencySets.SelectMany(d => d.Dependencies);
        }

        public static string GetFullName(this IPackageName package)
        {
            return package.Id + " " + package.Version;
        }

        /// <summary>
        /// Returns a distinct set of elements using the comparer specified. This implementation will pick the last occurrence
        /// of each element instead of picking the first. This method assumes that similar items occur in order.
        /// </summary>
        public static IEnumerable<IPackage> AsCollapsed(this IEnumerable<IPackage> source)
        {
            return source.DistinctLast<IPackage>(PackageEqualityComparer.Id, PackageComparer.Version);
        }

        /// <summary>
        /// Collapses the packages by Id picking up the highest version for each Id that it encounters
        /// </summary>
        internal static IEnumerable<IPackage> CollapseById(this IEnumerable<IPackage> source)
        {
            return source.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.OrderByDescending(p => p.Version).First());
        }

        public static IEnumerable<IPackage> FilterByPrerelease(this IEnumerable<IPackage> packages, bool allowPrerelease)
        {
            if (packages == null)
            {
                return null;
            }

            if (!allowPrerelease)
            {
                packages = packages.Where(p => p.IsReleaseVersion());
            }

            return packages;
        }

        /// <summary>
        /// Returns packages where the search text appears in the default set of properties to search. The default set includes Id, Description and Tags.
        /// </summary>
        public static IQueryable<T> Find<T>(this IQueryable<T> packages, string searchText) where T : IPackage
        {
            return Find(packages, _packagePropertiesToSearch, searchText);
        }

        /// <summary>
        /// Returns packages where the search text appears in any of the properties to search. 
        /// Note that not all properties can be successfully queried via this method particularly over a OData feed. Verify indepedently if it works for the properties that need to be searched.
        /// </summary>
        public static IQueryable<T> Find<T>(this IQueryable<T> packages, IEnumerable<string> propertiesToSearch, string searchText) where T : IPackage
        {
            if (propertiesToSearch.IsEmpty())
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "propertiesToSearch");
            }

            if (String.IsNullOrEmpty(searchText))
            {
                return packages;
            }
            return Find(packages, propertiesToSearch, searchText.Split());
        }

        private static IQueryable<T> Find<T>(this IQueryable<T> packages, IEnumerable<string> propertiesToSearch, IEnumerable<string> searchTerms) where T : IPackage
        {
            if (!searchTerms.Any())
            {
                return packages;
            }

            IEnumerable<string> nonNullTerms = searchTerms.Where(s => s != null);
            if (!nonNullTerms.Any())
            {
                return packages;
            }

            return packages.Where(BuildSearchExpression<T>(propertiesToSearch, nonNullTerms));
        }

        /// <summary>
        /// Returns a find query that is further restricted to just the latest package version.
        /// </summary>
        public static IQueryable<T> FindLatestVersion<T>(this IQueryable<T> packages) where T : IPackage
        {
            return from p in packages where p.IsLatestVersion select p;
        }
        
        /// <summary>
        /// Constructs an expression to search for individual tokens in a search term in the Id and Description of packages
        /// </summary>
        private static Expression<Func<T, bool>> BuildSearchExpression<T>(IEnumerable<string> propertiesToSearch, IEnumerable<string> searchTerms) where T : IPackage
        {
            Debug.Assert(searchTerms != null);
            var parameterExpression = Expression.Parameter(typeof(IPackageMetadata));

            // package.Id.ToLower().Contains(term1) || package.Id.ToLower().Contains(term2)  ...
            Expression condition = (from term in searchTerms
                                    from property in propertiesToSearch
                                    select BuildExpressionForTerm(parameterExpression, term, property)).Aggregate(Expression.OrElse);

            return Expression.Lambda<Func<T, bool>>(condition, parameterExpression);
        }

        [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo", MessageId = "System.String.ToLower",
            Justification = "The expression is remoted using Odata which does not support the culture parameter")]
        private static Expression BuildExpressionForTerm(
            ParameterExpression packageParameterExpression,
            string term,
            string propertyName)
        {
            // For tags we want to prepend and append spaces to do an exact match
            if (propertyName.Equals(TagsProperty, StringComparison.OrdinalIgnoreCase))
            {
                term = " " + term + " ";
            }

            MethodInfo stringContains = typeof(String).GetMethod("Contains", new Type[] { typeof(string) });
            MethodInfo stringToLower = typeof(String).GetMethod("ToLower", Type.EmptyTypes);

            // package.Id / package.Description

            MemberExpression propertyExpression;

            if (propertyName.Equals("Id", StringComparison.OrdinalIgnoreCase)) 
            {
                var cast = Expression.TypeAs(packageParameterExpression, typeof(IPackageName));
                propertyExpression = Expression.Property(cast, propertyName);
            }
            else 
            {
                propertyExpression = Expression.Property(packageParameterExpression, propertyName);
            }

            // .ToLower()
            var toLowerExpression = Expression.Call(propertyExpression, stringToLower);

            // Handle potentially null properties
            // package.{propertyName} != null && package.{propertyName}.ToLower().Contains(term.ToLower())
            return Expression.AndAlso(Expression.NotEqual(propertyExpression, Expression.Constant(null)),
                                      Expression.Call(toLowerExpression, stringContains, Expression.Constant(term.ToLower())));
        }
    }
}
