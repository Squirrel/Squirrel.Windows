using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace NuGet
{
    internal static class VersionUtility
    {
        private const string NetFrameworkIdentifier = ".NETFramework";
        private const string NetCoreFrameworkIdentifier = ".NETCore";
        private const string PortableFrameworkIdentifier = ".NETPortable";
        private const string StandardIdentifier = ".NETStandard";
        private const string AspNetFrameworkIdentifier = "ASP.NET";
        private const string AspNetCoreFrameworkIdentifier = "ASP.NETCore";
        private const string LessThanOrEqualTo = "\u2264";
        private const string GreaterThanOrEqualTo = "\u2265";

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Security",
            "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes",
            Justification = "The type FrameworkName is immutable.")]
        public static readonly FrameworkName EmptyFramework = new FrameworkName("NoFramework", new Version());

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Security",
            "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes",
            Justification = "The type FrameworkName is immutable.")]
        public static readonly FrameworkName NativeProjectFramework = new FrameworkName("Native", new Version());

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Security",
            "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes",
            Justification = "The type FrameworkName is immutable.")]
        public static readonly FrameworkName UnsupportedFrameworkName = new FrameworkName("Unsupported", new Version());
        private static readonly Version _emptyVersion = new Version();

        private static readonly Dictionary<string, string> _knownIdentifiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            // FYI, the keys are CASE-INSENSITIVE

            // .NET Desktop
            { "NET", NetFrameworkIdentifier },
            { ".NET", NetFrameworkIdentifier },
            { "NETFramework", NetFrameworkIdentifier },
            { ".NETFramework", NetFrameworkIdentifier },

            // .NET Core
            { "NETCore", NetCoreFrameworkIdentifier},
            { ".NETCore", NetCoreFrameworkIdentifier},
            { "WinRT", NetCoreFrameworkIdentifier},     // 'WinRT' is now deprecated. Use 'Windows' or 'win' instead.

            // .NET Micro Framework
            { ".NETMicroFramework", ".NETMicroFramework" },
            { "netmf", ".NETMicroFramework" },

            // .NET Standard
            { "netstandard", StandardIdentifier },
            { ".NETStandard", StandardIdentifier },

            // Silverlight
            { "SL", "Silverlight" },
            { "Silverlight", "Silverlight" },

            // Portable Class Libraries
            { ".NETPortable", PortableFrameworkIdentifier },
            { "NETPortable", PortableFrameworkIdentifier },
            { "portable", PortableFrameworkIdentifier },

            // Windows Phone
            { "wp", "WindowsPhone" },
            { "WindowsPhone", "WindowsPhone" },
            { "WindowsPhoneApp", "WindowsPhoneApp"},
            { "wpa", "WindowsPhoneApp"},
            
            // Windows
            { "Windows", "Windows" },
            { "win", "Windows" },

            // ASP.Net
            { "aspnet", AspNetFrameworkIdentifier },
            { "aspnetcore", AspNetCoreFrameworkIdentifier },
            { "asp.net", AspNetFrameworkIdentifier },
            { "asp.netcore", AspNetCoreFrameworkIdentifier },

            // Native
            { "native", "native"},
            
            // Mono/Xamarin
            { "MonoAndroid", "MonoAndroid" },
            { "MonoTouch", "MonoTouch" },
            { "MonoMac", "MonoMac" },
            { "Xamarin.iOS", "Xamarin.iOS" },
            { "XamariniOS", "Xamarin.iOS" },
            { "Xamarin.Mac", "Xamarin.Mac" },
            { "XamarinMac", "Xamarin.Mac" },
            { "Xamarin.PlayStationThree", "Xamarin.PlayStation3" },
            { "XamarinPlayStationThree", "Xamarin.PlayStation3" },
            { "XamarinPSThree", "Xamarin.PlayStation3" },
            { "Xamarin.PlayStationFour", "Xamarin.PlayStation4" },
            { "XamarinPlayStationFour", "Xamarin.PlayStation4" },
            { "XamarinPSFour", "Xamarin.PlayStation4" },
            { "Xamarin.PlayStationVita", "Xamarin.PlayStationVita" },
            { "XamarinPlayStationVita", "Xamarin.PlayStationVita" },
            { "XamarinPSVita", "Xamarin.PlayStationVita" },
            { "Xamarin.XboxThreeSixty", "Xamarin.Xbox360" },
            { "XamarinXboxThreeSixty", "Xamarin.Xbox360" },
            { "Xamarin.XboxOne", "Xamarin.XboxOne" },
            { "XamarinXboxOne", "Xamarin.XboxOne" }
        };

        private static readonly Dictionary<string, string> _knownProfiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Client", "Client" },
            { "WP", "WindowsPhone" },
            { "WP71", "WindowsPhone71" },
            { "CF", "CompactFramework" },
            { "Full", String.Empty }
        };

        // See IsCompatible
        // The ASP.Net framework authors desire complete compatibility between 'aspnet50' and all 'net' versions
        // So we use this MaxVersion value to achieve complete compatiblilty.
        private static readonly Version MaxVersion = new Version(Int32.MaxValue, Int32.MaxValue, Int32.MaxValue, Int32.MaxValue);

        public static Version DefaultTargetFrameworkVersion
        {
            get
            {
                // We need to parse the version name out from the mscorlib's assembly name since
                // we can't call GetName() in medium trust
                return typeof(string).Assembly.GetName().Version;
            }
        }

        public static FrameworkName DefaultTargetFramework
        {
            get
            {
                return new FrameworkName(NetFrameworkIdentifier, DefaultTargetFrameworkVersion);
            }
        }

        internal static bool IsCompatible(FrameworkName projectFrameworkName, FrameworkName[] packageTargetFrameworkName)
        {
            throw new NotImplementedException();
        }

        public static string PrettyPrint(IVersionSpec versionSpec)
        {
            if (versionSpec.MinVersion != null && versionSpec.IsMinInclusive && versionSpec.MaxVersion == null && !versionSpec.IsMaxInclusive) {
                return String.Format(CultureInfo.InvariantCulture, "({0} {1})", GreaterThanOrEqualTo, versionSpec.MinVersion);
            }

            if (versionSpec.MinVersion != null && versionSpec.MaxVersion != null && versionSpec.MinVersion == versionSpec.MaxVersion && versionSpec.IsMinInclusive && versionSpec.IsMaxInclusive) {
                return String.Format(CultureInfo.InvariantCulture, "(= {0})", versionSpec.MinVersion);
            }

            var versionBuilder = new StringBuilder();
            if (versionSpec.MinVersion != null) {
                if (versionSpec.IsMinInclusive) {
                    versionBuilder.AppendFormat(CultureInfo.InvariantCulture, "({0} ", GreaterThanOrEqualTo);
                } else {
                    versionBuilder.Append("(> ");
                }
                versionBuilder.Append(versionSpec.MinVersion);
            }

            if (versionSpec.MaxVersion != null) {
                if (versionBuilder.Length == 0) {
                    versionBuilder.Append("(");
                } else {
                    versionBuilder.Append(" && ");
                }

                if (versionSpec.IsMaxInclusive) {
                    versionBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0} ", LessThanOrEqualTo);
                } else {
                    versionBuilder.Append("< ");
                }
                versionBuilder.Append(versionSpec.MaxVersion);
            }

            if (versionBuilder.Length > 0) {
                versionBuilder.Append(")");
            }

            return versionBuilder.ToString();
        }

        /// <summary>
        /// The version string is either a simple version or an arithmetic range
        /// e.g.
        ///      1.0         --> 1.0 ≤ x
        ///      (,1.0]      --> x ≤ 1.0
        ///      (,1.0)      --> x &lt; 1.0
        ///      [1.0]       --> x == 1.0
        ///      (1.0,)      --> 1.0 &lt; x
        ///      (1.0, 2.0)   --> 1.0 &lt; x &lt; 2.0
        ///      [1.0, 2.0]   --> 1.0 ≤ x ≤ 2.0
        /// </summary>
        public static IVersionSpec ParseVersionSpec(string value)
        {
            IVersionSpec versionInfo;
            if (!TryParseVersionSpec(value, out versionInfo)) {
                throw new ArgumentException(
                    String.Format(CultureInfo.CurrentCulture,
                     "InvalidVersionString", value));
            }

            return versionInfo;
        }

        public static bool TryParseVersionSpec(string value, out IVersionSpec result)
        {
            if (value == null) {
                throw new ArgumentNullException("value");
            }

            var versionSpec = new VersionSpec();
            value = value.Trim();

            // First, try to parse it as a plain version string
            SemanticVersion version;
            if (SemanticVersion.TryParse(value, out version)) {
                // A plain version is treated as an inclusive minimum range
                result = new VersionSpec {
                    MinVersion = version,
                    IsMinInclusive = true
                };

                return true;
            }

            // It's not a plain version, so it must be using the bracket arithmetic range syntax

            result = null;

            // Fail early if the string is too short to be valid
            if (value.Length < 3) {
                return false;
            }

            // The first character must be [ ot (
            switch (value.First()) {
            case '[':
                versionSpec.IsMinInclusive = true;
                break;
            case '(':
                versionSpec.IsMinInclusive = false;
                break;
            default:
                return false;
            }

            // The last character must be ] ot )
            switch (value.Last()) {
            case ']':
                versionSpec.IsMaxInclusive = true;
                break;
            case ')':
                versionSpec.IsMaxInclusive = false;
                break;
            default:
                return false;
            }

            // Get rid of the two brackets
            value = value.Substring(1, value.Length - 2);

            // Split by comma, and make sure we don't get more than two pieces
            string[] parts = value.Split(',');
            if (parts.Length > 2) {
                return false;
            } else if (parts.All(String.IsNullOrEmpty)) {
                // If all parts are empty, then neither of upper or lower bounds were specified. Version spec is of the format (,]
                return false;
            }

            // If there is only one piece, we use it for both min and max
            string minVersionString = parts[0];
            string maxVersionString = (parts.Length == 2) ? parts[1] : parts[0];

            // Only parse the min version if it's non-empty
            if (!String.IsNullOrWhiteSpace(minVersionString)) {
                if (!TryParseVersion(minVersionString, out version)) {
                    return false;
                }
                versionSpec.MinVersion = version;
            }

            // Same deal for max
            if (!String.IsNullOrWhiteSpace(maxVersionString)) {
                if (!TryParseVersion(maxVersionString, out version)) {
                    return false;
                }
                versionSpec.MaxVersion = version;
            }

            // Successful parse!
            result = versionSpec;
            return true;
        }

        private static bool TryParseVersion(string versionString, out SemanticVersion version)
        {
            version = null;
            if (!SemanticVersion.TryParse(versionString, out version)) {
                // Support integer version numbers (i.e. 1 -> 1.0)
                int versionNumber;
                if (Int32.TryParse(versionString, out versionNumber) && versionNumber > 0) {
                    version = new SemanticVersion(new Version(versionNumber, 0));
                }
            }
            return version != null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "1#")]
        public static FrameworkName ParseFrameworkNameFromFilePath(string filePath, out string effectivePath)
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
                        return VersionUtility.ParseFrameworkFolderName(
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

        public static FrameworkName ParseFrameworkFolderName(string path)
        {
            string effectivePath;
            return ParseFrameworkFolderName(path, strictParsing: true, effectivePath: out effectivePath);
        }

        /// <summary>
        /// Parses the specified string into FrameworkName object.
        /// </summary>
        /// <param name="path">The string to be parse.</param>
        /// <param name="strictParsing">if set to <c>true</c>, parse the first folder of path even if it is unrecognized framework.</param>
        /// <param name="effectivePath">returns the path after the parsed target framework</param>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "2#")]
        public static FrameworkName ParseFrameworkFolderName(string path, bool strictParsing, out string effectivePath)
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
            if (strictParsing || targetFramework != UnsupportedFrameworkName) {
                // skip past the framework folder and the character \
                effectivePath = path.Substring(targetFrameworkString.Length + 1);
                return targetFramework;
            }

            return null;
        }

        /// <summary>
        /// This function tries to normalize a string that represents framework version names into
        /// something a framework name that the package manager understands.
        /// </summary>
        public static FrameworkName ParseFrameworkName(string frameworkName)
        {
            if (frameworkName == null) {
                throw new ArgumentNullException("frameworkName");
            }

            // {Identifier}{Version}-{Profile}

            // Split the framework name into 3 parts, identifier, version and profile.
            string identifierPart = null;
            string versionPart = null;

            string[] parts = frameworkName.Split('-');

            if (parts.Length > 2) {
                throw new ArgumentException("InvalidFrameworkNameFormat", "frameworkName");
            }

            string frameworkNameAndVersion = parts.Length > 0 ? parts[0].Trim() : null;
            string profilePart = parts.Length > 1 ? parts[1].Trim() : null;

            if (String.IsNullOrEmpty(frameworkNameAndVersion)) {
                throw new ArgumentException("MissingFrameworkName", "frameworkName");
            }

            // If we find a version then we try to split the framework name into 2 parts
            var versionMatch = Regex.Match(frameworkNameAndVersion, @"\d+");

            if (versionMatch.Success) {
                identifierPart = frameworkNameAndVersion.Substring(0, versionMatch.Index).Trim();
                versionPart = frameworkNameAndVersion.Substring(versionMatch.Index).Trim();
            } else {
                // Otherwise we take the whole name as an identifier
                identifierPart = frameworkNameAndVersion.Trim();
            }

            if (!String.IsNullOrEmpty(identifierPart)) {
                // Try to normalize the identifier to a known identifier
                if (!_knownIdentifiers.TryGetValue(identifierPart, out identifierPart)) {
                    return UnsupportedFrameworkName;
                }
            }

            if (!String.IsNullOrEmpty(profilePart)) {
                string knownProfile;
                if (_knownProfiles.TryGetValue(profilePart, out knownProfile)) {
                    profilePart = knownProfile;
                }
            }

            Version version = null;
            // We support version formats that are integers (40 becomes 4.0)
            int versionNumber;
            if (Int32.TryParse(versionPart, out versionNumber)) {
                // Remove the extra numbers
                if (versionPart.Length > 4) {
                    versionPart = versionPart.Substring(0, 4);
                }

                // Make sure it has at least 2 digits so it parses as a valid version
                versionPart = versionPart.PadRight(2, '0');
                versionPart = String.Join(".", versionPart.ToCharArray());
            }

            // If we can't parse the version then use the default
            if (!Version.TryParse(versionPart, out version)) {
                // We failed to parse the version string once more. So we need to decide if this is unsupported or if we use the default version.
                // This framework is unsupported if:
                // 1. The identifier part of the framework name is null.
                // 2. The version part is not null.
                if (String.IsNullOrEmpty(identifierPart) || !String.IsNullOrEmpty(versionPart)) {
                    return UnsupportedFrameworkName;
                }

                version = _emptyVersion;
            }

            if (String.IsNullOrEmpty(identifierPart)) {
                identifierPart = NetFrameworkIdentifier;
            }

            // if this is a .NET Portable framework name, validate the profile part to ensure it is valid
            if (identifierPart.Equals(PortableFrameworkIdentifier, StringComparison.OrdinalIgnoreCase)) {
                ValidatePortableFrameworkProfilePart(profilePart);
            }

            return new FrameworkName(identifierPart, version, profilePart);
        }

        internal static void ValidatePortableFrameworkProfilePart(string profilePart)
        {
            if (String.IsNullOrEmpty(profilePart)) {
                throw new ArgumentException("PortableFrameworkProfileEmpty", "profilePart");
            }

            if (profilePart.Contains('-')) {
                throw new ArgumentException("PortableFrameworkProfileHasDash", "profilePart");
            }

            if (profilePart.Contains(' ')) {
                throw new ArgumentException("PortableFrameworkProfileHasSpace", "profilePart");
            }

            string[] parts = profilePart.Split('+');
            if (parts.Any(p => String.IsNullOrEmpty(p))) {
                throw new ArgumentException("PortableFrameworkProfileComponentIsEmpty", "profilePart");
            }

            // Prevent portable framework inside a portable framework - Inception
            if (parts.Any(p => p.StartsWith("portable", StringComparison.OrdinalIgnoreCase)) ||
                parts.Any(p => p.StartsWith("NETPortable", StringComparison.OrdinalIgnoreCase)) ||
                parts.Any(p => p.StartsWith(".NETPortable", StringComparison.OrdinalIgnoreCase))) {
                throw new ArgumentException("PortableFrameworkProfileComponentIsPortable", "profilePart");
            }
        }
    }
}
