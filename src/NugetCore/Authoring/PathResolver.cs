using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NuGet
{
    public static class PathResolver
    {
        /// <summary>
        /// Returns a collection of files from the source that matches the wildcard.
        /// </summary>
        /// <param name="source">The collection of files to match.</param>
        /// <param name="getPath">Function that returns the path to filter a package file </param>
        /// <param name="wildcards">The wildcards to apply to match the path with.</param>
        /// <returns></returns>
        public static IEnumerable<T> GetMatches<T>(IEnumerable<T> source, Func<T, string> getPath, IEnumerable<string> wildcards)
        {
            var filters = wildcards.Select(WildcardToRegex);
            return source.Where(item =>
            {
                string path = getPath(item);
                return filters.Any(f => f.IsMatch(path));
            });
        }

        /// <summary>
        /// Removes files from the source that match any wildcard.
        /// </summary>
        public static void FilterPackageFiles<T>(ICollection<T> source, Func<T, string> getPath, IEnumerable<string> wildcards)
        {
            var matchedFiles = new HashSet<T>(GetMatches(source, getPath, wildcards));
            source.RemoveAll(matchedFiles.Contains);
        }

        public static string NormalizeWildcardForExcludedFiles(string basePath, string wildcard)
        {
            if (wildcard.StartsWith("**", StringComparison.OrdinalIgnoreCase))
            {
                // Allow any path to match the first '**' segment, see issue 2891 for more details.
                return wildcard;
            }
            basePath = NormalizeBasePath(basePath, ref wildcard);
            return Path.Combine(basePath, wildcard);
        }

        private static Regex WildcardToRegex(string wildcard)
        {
            var pattern = Regex.Escape(wildcard);
            if (Path.DirectorySeparatorChar == '/')
            {
                // regex wildcard adjustments for *nix-style file systems
                pattern = pattern
                    .Replace(@"\*\*/", ".*") //For recursive wildcards /**/, include the current directory.
                    .Replace(@"\*\*", ".*") // For recursive wildcards that don't end in a slash e.g. **.txt would be treated as a .txt file at any depth
                    .Replace(@"\*", @"[^/]*(/)?") // For non recursive searches, limit it any character that is not a directory separator
                    .Replace(@"\?", "."); // ? translates to a single any character
            }
            else
            {
                // regex wildcard adjustments for Windows-style file systems
                pattern = pattern
                    .Replace("/", @"\\") // On Windows, / is treated the same as \.
                    .Replace(@"\*\*\\", ".*") //For recursive wildcards \**\, include the current directory.
                    .Replace(@"\*\*", ".*") // For recursive wildcards that don't end in a slash e.g. **.txt would be treated as a .txt file at any depth
                    .Replace(@"\*", @"[^\\]*(\\)?") // For non recursive searches, limit it any character that is not a directory separator
                    .Replace(@"\?", "."); // ? translates to a single any character
            }

            return new Regex('^' + pattern + '$', RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
        }

        internal static IEnumerable<PhysicalPackageFile> ResolveSearchPattern(string basePath, string searchPath, string targetPath, bool includeEmptyDirectories)
        {
            string normalizedBasePath;
            IEnumerable<SearchPathResult> searchResults = PerformWildcardSearchInternal(basePath, searchPath, includeEmptyDirectories, out normalizedBasePath);

            return searchResults.Select(result =>
                result.IsFile
                    ? new PhysicalPackageFile
                      {
                          SourcePath = result.Path,
                          TargetPath = ResolvePackagePath(normalizedBasePath, searchPath, result.Path, targetPath)
                      }
                    : new EmptyFrameworkFolderFile(ResolvePackagePath(normalizedBasePath, searchPath, result.Path, targetPath))
                      {
                          SourcePath = result.Path
                      }
            );
        }

        public static IEnumerable<string> PerformWildcardSearch(string basePath, string searchPath)
        {
            string normalizedBasePath;
            var searchResults = PerformWildcardSearchInternal(basePath, searchPath, includeEmptyDirectories: false, normalizedBasePath: out normalizedBasePath);
            return searchResults.Select(s => s.Path);
        }

        private static IEnumerable<SearchPathResult> PerformWildcardSearchInternal(string basePath, string searchPath, bool includeEmptyDirectories, out string normalizedBasePath)
        {
            if (!searchPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
                && Path.DirectorySeparatorChar != '/')
            {
                //If the system's DirectorySeparatorChar is '/' we're probably dealing with Mac or *nix
                // In any case, if '/' is the separator, we don't want to trim off the first char ever
                // since it would completely change the meaning of the path
                //  eg: /var/somedir/ is not at all the same as var/somedir (relative vs absolute)

                // If we aren't dealing with network paths, trim the leading slash. 
                searchPath = searchPath.TrimStart(Path.DirectorySeparatorChar);
            }

            bool searchDirectory = false;
            
            // If the searchPath ends with \ or /, we treat searchPath as a directory,
            // and will include everything under it, recursively
            if (IsDirectoryPath(searchPath))
            {
                searchPath = searchPath + "**" + Path.DirectorySeparatorChar + "*";
                searchDirectory = true;
            }

            basePath = NormalizeBasePath(basePath, ref searchPath);
            normalizedBasePath = GetPathToEnumerateFrom(basePath, searchPath);

            // Append the basePath to searchPattern and get the search regex. We need to do this because the search regex is matched from line start.
            Regex searchRegex = WildcardToRegex(Path.Combine(basePath, searchPath));

            // This is a hack to prevent enumerating over the entire directory tree if the only wildcard characters are the ones in the file name. 
            // If the path portion of the search path does not contain any wildcard characters only iterate over the TopDirectory.
            SearchOption searchOption = SearchOption.AllDirectories;
            // (a) Path is not recursive search
            bool isRecursiveSearch = searchPath.IndexOf("**", StringComparison.OrdinalIgnoreCase) != -1;
            // (b) Path does not have any wildcards.
            bool isWildcardPath = Path.GetDirectoryName(searchPath).Contains('*');
            if (!isRecursiveSearch && !isWildcardPath)
            {
                searchOption = SearchOption.TopDirectoryOnly;
            }

            // Starting from the base path, enumerate over all files and match it using the wildcard expression provided by the user.
            // Note: We use Directory.GetFiles() instead of Directory.EnumerateFiles() here to support Mono
            var matchedFiles = from file in Directory.GetFiles(normalizedBasePath, "*.*", searchOption)
                               where searchRegex.IsMatch(file)
                               select new SearchPathResult(file, isFile: true);

            if (!includeEmptyDirectories)
            {
                return matchedFiles;
            }

            // retrieve empty directories
            // Note: We use Directory.GetDirectories() instead of Directory.EnumerateDirectories() here to support Mono
            var matchedDirectories = from directory in Directory.GetDirectories(normalizedBasePath, "*.*", searchOption)
                                     where searchRegex.IsMatch(directory) && IsEmptyDirectory(directory)
                                     select new SearchPathResult(directory, isFile: false);

            if (searchDirectory && IsEmptyDirectory(normalizedBasePath))
            {
                matchedDirectories = matchedDirectories.Concat(new [] { new SearchPathResult(normalizedBasePath, isFile: false) });
            }

            return matchedFiles.Concat(matchedDirectories);
        }

        internal static string GetPathToEnumerateFrom(string basePath, string searchPath)
        {
            string basePathToEnumerate;
            int wildcardIndex = searchPath.IndexOf('*');
            if (wildcardIndex == -1)
            {
                // For paths without wildcard, we could either have base relative paths (such as lib\foo.dll) or paths outside the base path
                // (such as basePath: C:\packages and searchPath: D:\packages\foo.dll)
                // In this case, Path.Combine would pick up the right root to enumerate from.
                var searchRoot = Path.GetDirectoryName(searchPath);
                basePathToEnumerate = Path.Combine(basePath, searchRoot);
            }
            else
            {
                // If not, find the first directory separator and use the path to the left of it as the base path to enumerate from.
                int directorySeparatoryIndex = searchPath.LastIndexOf(Path.DirectorySeparatorChar, wildcardIndex);
                if (directorySeparatoryIndex == -1)
                {
                    // We're looking at a path like "NuGet*.dll", NuGet*\bin\release\*.dll
                    // In this case, the basePath would continue to be the path to begin enumeration from.
                    basePathToEnumerate = basePath;
                }
                else
                {
                    string nonWildcardPortion = searchPath.Substring(0, directorySeparatoryIndex);
                    basePathToEnumerate = Path.Combine(basePath, nonWildcardPortion);
                }
            }
            return basePathToEnumerate;
        }

        /// <summary>
        /// Determins the path of the file inside a package.
        /// For recursive wildcard paths, we preserve the path portion beginning with the wildcard.
        /// For non-recursive wildcard paths, we use the file name from the actual file path on disk.
        /// </summary>
        internal static string ResolvePackagePath(string searchDirectory, string searchPattern, string fullPath, string targetPath)
        {
            string packagePath;
            bool isDirectorySearch = IsDirectoryPath(searchPattern);
            bool isWildcardSearch = IsWildcardSearch(searchPattern);
            bool isRecursiveWildcardSearch = isWildcardSearch && searchPattern.IndexOf("**", StringComparison.OrdinalIgnoreCase) != -1;

            if ((isRecursiveWildcardSearch || isDirectorySearch) && fullPath.StartsWith(searchDirectory, StringComparison.OrdinalIgnoreCase))
            {
                // The search pattern is recursive. Preserve the non-wildcard portion of the path.
                // e.g. Search: X:\foo\**\*.cs results in SearchDirectory: X:\foo and a file path of X:\foo\bar\biz\boz.cs
                // Truncating X:\foo\ would result in the package path.
                packagePath = fullPath.Substring(searchDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
            }
            else if (!isWildcardSearch && Path.GetExtension(searchPattern).Equals(Path.GetExtension(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                // If the search does not contain wild cards, and the target path shares the same extension, copy it
                // e.g. <file src="ie\css\style.css" target="Content\css\ie.css" /> --> Content\css\ie.css
                return targetPath;
            }
            else
            {
                packagePath = Path.GetFileName(fullPath);
            }
            return Path.Combine(targetPath ?? String.Empty, packagePath);
        }

        private static readonly string OneDotSlash = "." + Path.DirectorySeparatorChar;
        private static readonly string TwoDotSlash = ".." + Path.DirectorySeparatorChar;

        internal static string NormalizeBasePath(string basePath, ref string searchPath)
        {
            // If no base path is provided, use the current directory.
            basePath = String.IsNullOrEmpty(basePath) ? OneDotSlash : basePath;

            // If the search path is relative, transfer the ..\ portion to the base path. 
            // This needs to be done because the base path determines the root for our enumeration.
            while (searchPath.StartsWith(TwoDotSlash, StringComparison.OrdinalIgnoreCase))
            {
                basePath = Path.Combine(basePath, TwoDotSlash);
                searchPath = searchPath.Substring(TwoDotSlash.Length);
            }

            return Path.GetFullPath(basePath);
        }

        /// <summary>
        /// Returns true if the path contains any wildcard characters.
        /// </summary>
        internal static bool IsWildcardSearch(string filter)
        {
            return filter.IndexOf('*') != -1;
        }

        internal static bool IsDirectoryPath(string path)
        {
            return path != null && path.Length > 1 &&
                (path[path.Length - 1] == Path.DirectorySeparatorChar ||
                path[path.Length - 1] == Path.AltDirectorySeparatorChar);
        }

        private static bool IsEmptyDirectory(string directory)
        {
            return !Directory.EnumerateFileSystemEntries(directory).Any();
        }

        private struct SearchPathResult
        {
            private readonly string _path;
            private readonly bool _isFile;

            public string Path
            {
                get
                {
                    return _path;
                }
            }

            public bool IsFile
            {
                get
                {
                    return _isFile;
                }
            }

            public SearchPathResult(string path, bool isFile)
            {
                _path = path;
                _isFile = isFile;
            }
        }
    }
}
