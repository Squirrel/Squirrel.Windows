using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;

namespace Squirrel.NuGet
{
    internal static class NugetUtil
    {
        public static readonly string PackageExtension = ".nupkg";
        public static readonly string ManifestExtension = ".nuspec";
        public static readonly string ContentDirectory = "content";
        public static readonly string LibDirectory = "lib";
        public static readonly string ToolsDirectory = "tools";
        public static readonly string BuildDirectory = "build";
        public static readonly string BinDirectory = "bin";
        public static readonly string SettingsFileName = "NuGet.Config";
        public static readonly string PackageReferenceFile = "packages.config";
        public static readonly string MirroringReferenceFile = "mirroring.config";

        public static void ThrowIfInvalidNugetId(string id)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[\w\.-]*$"))
                throw new ArgumentException($"Invalid package Id '{id}', it must contain only alphanumeric characters, underscores, dashes, and dots.");
        }

        public static void ThrowIfVersionNotSemverCompliant(string version)
        {
            if (SemanticVersion.TryParse(version, out var parsed)) {
                if (parsed < new SemanticVersion(0, 0, 1)) {
                    throw new Exception($"Invalid package version '{version}', it must be >= 0.0.1.");
                }
            } else {
                throw new Exception($"Invalid package version '{version}', it must be a 3-part SemVer2 compliant version string.");
            }
        }

        public static string SafeTrim(this string value)
        {
            return value == null ? null : value.Trim();
        }

        public static string GetOptionalAttributeValue(this XElement element, string localName, string namespaceName = null)
        {
            XAttribute attr;
            if (String.IsNullOrEmpty(namespaceName)) {
                attr = element.Attribute(localName);
            } else {
                attr = element.Attribute(XName.Get(localName, namespaceName));
            }
            return attr != null ? attr.Value : null;
        }

        public static string GetOptionalElementValue(this XContainer element, string localName, string namespaceName = null)
        {
            XElement child;
            if (String.IsNullOrEmpty(namespaceName)) {
                child = element.ElementsNoNamespace(localName).FirstOrDefault();
            } else {
                child = element.Element(XName.Get(localName, namespaceName));
            }
            return child != null ? child.Value : null;
        }

        public static IEnumerable<XElement> ElementsNoNamespace(this XContainer container, string localName)
        {
            return container.Elements().Where(e => e.Name.LocalName == localName);
        }

        public static IEnumerable<XElement> ElementsNoNamespace(this IEnumerable<XContainer> source, string localName)
        {
            return source.Elements().Where(e => e.Name.LocalName == localName);
        }

        /// <summary>
        /// Converts a uri to a path. Only used for local paths.
        /// </summary>
        internal static string GetPath(Uri uri)
        {
            string path = uri.OriginalString;
            if (path.StartsWith("/", StringComparison.Ordinal)) {
                path = path.Substring(1);
            }

            // Bug 483: We need the unescaped uri string to ensure that all characters are valid for a path.
            // Change the direction of the slashes to match the filesystem.
            return Uri.UnescapeDataString(path.Replace('/', Path.DirectorySeparatorChar));
        }

        public static string ParseFrameworkFolderName(string path, bool strictParsing, out string effectivePath)
        {
            // The path for a reference might look like this for assembly foo.dll:            
            // foo.dll
            // sub\foo.dll
            // {FrameworkName}{Version}\foo.dll
            // {FrameworkName}{Version}\sub1\foo.dll
            // {FrameworkName}{Version}\sub1\sub2\foo.dll

            // Get the target framework string if specified
            string targetFrameworkString = Path.GetDirectoryName(path).Split(Path.DirectorySeparatorChar).First();

            effectivePath = path;

            if (String.IsNullOrEmpty(targetFrameworkString)) {
                return null;
            }

            var targetFramework = targetFrameworkString;
            if (strictParsing) {
                // skip past the framework folder and the character \
                effectivePath = path.Substring(targetFrameworkString.Length + 1);
                return targetFramework;
            }

            return null;
        }


        public static string ParseFrameworkNameFromFilePath(string filePath, out string effectivePath)
        {
            var knownFolders = new string[]
            {
                NugetUtil.ContentDirectory,
                NugetUtil.LibDirectory,
                NugetUtil.ToolsDirectory,
                NugetUtil.BuildDirectory
            };

            for (int i = 0; i < knownFolders.Length; i++) {
                string folderPrefix = knownFolders[i] + System.IO.Path.DirectorySeparatorChar;
                if (filePath.Length > folderPrefix.Length &&
                    filePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)) {
                    string frameworkPart = filePath.Substring(folderPrefix.Length);

                    try {
                        return ParseFrameworkFolderName(
                            frameworkPart,
                            strictParsing: knownFolders[i] == NugetUtil.LibDirectory,
                            effectivePath: out effectivePath);
                    } catch (ArgumentException) {
                        // if the parsing fails, we treat it as if this file
                        // doesn't have target framework.
                        effectivePath = frameworkPart;
                        return null;
                    }
                }

            }

            effectivePath = filePath;
            return null;
        }

        public static XDocument LoadSafe(Stream input, bool ignoreWhiteSpace)
        {
            var settings = CreateSafeSettings(ignoreWhiteSpace);
            var reader = XmlReader.Create(input, settings);
            return XDocument.Load(reader);
        }

        private static XmlReaderSettings CreateSafeSettings(bool ignoreWhiteSpace = false)
        {
            var safeSettings = new XmlReaderSettings {
                XmlResolver = null,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = ignoreWhiteSpace
            };

            return safeSettings;
        }
    }
}