using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;

namespace NuGet
{
    internal static class UriUtility
    {
        /// <summary>
        /// Converts a uri to a path. Only used for local paths.
        /// </summary>
        internal static string GetPath(Uri uri)
        {
            string path = uri.OriginalString;
            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            // Bug 483: We need the unescaped uri string to ensure that all characters are valid for a path.
            // Change the direction of the slashes to match the filesystem.
            return Uri.UnescapeDataString(path.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static Uri CreatePartUri(string path)
        {
            // Only the segments between the path separators should be escaped
            var segments = path.Split( new[] { '/', Path.DirectorySeparatorChar }, StringSplitOptions.None)
                               .Select(Uri.EscapeDataString);
            var escapedPath = String.Join("/", segments);
            return PackUriHelper.CreatePartUri(new Uri(escapedPath, UriKind.Relative));
        }

        // Bug 2379: SettingsCredentialProvider does not work
        private static Uri CreateODataAgnosticUri(string uri)
        {
            if (uri.EndsWith("$metadata", StringComparison.OrdinalIgnoreCase))
            {
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
}
