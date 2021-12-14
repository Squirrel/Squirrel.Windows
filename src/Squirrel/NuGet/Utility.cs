using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Squirrel.NuGet
{
    internal static class UriUtility
    {
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

        internal static Uri CreatePartUri(string path)
        {
            // Only the segments between the path separators should be escaped
            var segments = path.Split(new[] { '/', Path.DirectorySeparatorChar }, StringSplitOptions.None)
                               .Select(Uri.EscapeDataString);
            var escapedPath = String.Join("/", segments);
            return PackUriHelper.CreatePartUri(new Uri(escapedPath, UriKind.Relative));
        }

        // Bug 2379: SettingsCredentialProvider does not work
        private static Uri CreateODataAgnosticUri(string uri)
        {
            if (uri.EndsWith("$metadata", StringComparison.OrdinalIgnoreCase)) {
                uri = uri.Substring(0, uri.Length - 9).TrimEnd('/');
            }
            return new Uri(uri);
        }

        /// <summary>
        /// Determines if the scheme, server and path of two Uris are identical.
        /// </summary>
        public static bool UriEquals(Uri uri1, Uri uri2)
        {
            uri1 = CreateODataAgnosticUri(uri1.OriginalString.TrimEnd('/'));
            uri2 = CreateODataAgnosticUri(uri2.OriginalString.TrimEnd('/'));

            return Uri.Compare(uri1, uri2, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// This routine was implemented to assist with finding credentials in the settings file using the uri
        /// It appears the package uri is used to find the credentials ex) https://hostname/api/nuget/Download/packagename/versionnumber  
        /// but the settings file will more than likely only have the repository uri ex) https://hostname/api/nuget 
        /// This routine will attempt to find the uri in settings as is
        /// If not found, see if source Uri is base of uri
        /// </summary>
        /// <param name="uri1">base or source URI</param>
        /// <param name="uri2">full URI</param>
        /// <returns></returns>
        public static bool UriStartsWith(Uri uri1, Uri uri2)
        {
            return UriUtility.UriEquals(uri1, uri2) || uri1.IsBaseOf(uri2);
        }
    }

    internal class VersionUtility
    {
        public static string ParseVersionSpec(string versionSpec)
        {
            if (versionSpec == null) {
                throw new ArgumentNullException("spec");
            }

            return versionSpec;
        }

        public static string ParseFrameworkName(string frameworkName)
        {
            if (frameworkName == null) {
                throw new ArgumentNullException("frameworkName");
            }

            return frameworkName;
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

            var targetFramework = ParseFrameworkName(targetFrameworkString);
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
                Constants.ContentDirectory,
                Constants.LibDirectory,
                Constants.ToolsDirectory,
                Constants.BuildDirectory
            };

            for (int i = 0; i < knownFolders.Length; i++) {
                string folderPrefix = knownFolders[i] + System.IO.Path.DirectorySeparatorChar;
                if (filePath.Length > folderPrefix.Length &&
                    filePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)) {
                    string frameworkPart = filePath.Substring(folderPrefix.Length);

                    try {
                        return ParseFrameworkFolderName(
                            frameworkPart,
                            strictParsing: knownFolders[i] == Constants.LibDirectory,
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
    }

    internal static class XmlUtility
    {
        public static XDocument LoadSafe(string filePath)
        {
            var settings = CreateSafeSettings();
            using (var reader = XmlReader.Create(filePath, settings)) {
                return XDocument.Load(reader);
            }
        }

        public static XDocument LoadSafe(Stream input)
        {
            var settings = CreateSafeSettings();
            var reader = XmlReader.Create(input, settings);
            return XDocument.Load(reader);
        }

        public static XDocument LoadSafe(Stream input, bool ignoreWhiteSpace)
        {
            var settings = CreateSafeSettings(ignoreWhiteSpace);
            var reader = XmlReader.Create(input, settings);
            return XDocument.Load(reader);
        }

        public static XDocument LoadSafe(Stream input, LoadOptions options)
        {
            var settings = CreateSafeSettings();
            var reader = XmlReader.Create(input, settings);
            return XDocument.Load(reader, options);
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

        internal static bool TryParseDocument(string content, out XDocument document)
        {
            document = null;
            try {
                document = XDocument.Parse(content);
                return true;
            } catch (XmlException) {
                return false;
            }
        }
    }
}
