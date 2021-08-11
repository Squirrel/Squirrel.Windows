using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using NuGet.Resources;
using CompatibilityMapping = System.Collections.Generic.Dictionary<string, string[]>;

namespace NuGet
{
    public static class VersionUtility
    {
        private const string NetFrameworkIdentifier = ".NETFramework";
        private const string NetCoreFrameworkIdentifier = ".NETCore";
        private const string PortableFrameworkIdentifier = ".NETPortable";
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

        private static readonly Dictionary<string, string> _identifierToFrameworkFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { NetFrameworkIdentifier, "net" },
            { ".NETMicroFramework", "netmf" },
            { AspNetFrameworkIdentifier, "aspnet" },
            { AspNetCoreFrameworkIdentifier, "aspnetcore" },
            { "Silverlight", "sl" },
            { ".NETCore", "win"},
            { "Windows", "win"},
            { ".NETPortable", "portable" },
            { "WindowsPhone", "wp"},
            { "WindowsPhoneApp", "wpa"},
            { "Xamarin.iOS", "xamarinios" },
            { "Xamarin.Mac", "xamarinmac" },
            { "Xamarin.PlayStation3", "xamarinpsthree" },
            { "Xamarin.PlayStation4", "xamarinpsfour" },
            { "Xamarin.PlayStationVita", "xamarinpsvita" },
            { "Xamarin.Xbox360", "xamarinxboxthreesixty" },
            { "Xamarin.XboxOne", "xamarinxboxone" }
        };

        private static readonly Dictionary<string, string> _identifierToProfileFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "WindowsPhone", "wp" },
            { "WindowsPhone71", "wp71" },
            { "CompactFramework", "cf" }
        };

        private static readonly Dictionary<string, CompatibilityMapping> _compatibiltyMapping = new Dictionary<string, CompatibilityMapping>(StringComparer.OrdinalIgnoreCase) {
            {
                // Client profile is compatible with the full framework (empty string is full)
                NetFrameworkIdentifier, new CompatibilityMapping(StringComparer.OrdinalIgnoreCase) {
                    { "", new [] { "Client" } },
                    { "Client", new [] { "" } }
                }
            },
            {
                "Silverlight", new CompatibilityMapping(StringComparer.OrdinalIgnoreCase) {
                    { "WindowsPhone", new[] { "WindowsPhone71" } },
                    { "WindowsPhone71", new[] { "WindowsPhone" } }
                }
            }
        };

        // These aliases allow us to accept 'wp', 'wp70', 'wp71', 'windows', 'windows8' as valid target farmework folders.
        private static readonly Dictionary<FrameworkName, FrameworkName> _frameworkNameAlias = new Dictionary<FrameworkName, FrameworkName>(FrameworkNameEqualityComparer.Default)
        {
            { new FrameworkName("WindowsPhone, Version=v0.0"), new FrameworkName("Silverlight, Version=v3.0, Profile=WindowsPhone") },
            { new FrameworkName("WindowsPhone, Version=v7.0"), new FrameworkName("Silverlight, Version=v3.0, Profile=WindowsPhone") },
            { new FrameworkName("WindowsPhone, Version=v7.1"), new FrameworkName("Silverlight, Version=v4.0, Profile=WindowsPhone71") },
            { new FrameworkName("WindowsPhone, Version=v8.0"), new FrameworkName("Silverlight, Version=v8.0, Profile=WindowsPhone") },
            { new FrameworkName("WindowsPhone, Version=v8.1"), new FrameworkName("Silverlight, Version=v8.1, Profile=WindowsPhone") },

            { new FrameworkName("Windows, Version=v0.0"), new FrameworkName(".NETCore, Version=v4.5") },
            { new FrameworkName("Windows, Version=v8.0"), new FrameworkName(".NETCore, Version=v4.5") },
            { new FrameworkName("Windows, Version=v8.1"), new FrameworkName(".NETCore, Version=v4.5.1") }
        };

        // See IsCompatible
        // The ASP.Net framework authors desire complete compatibility between 'aspnet50' and all 'net' versions
        // So we use this MaxVersion value to achieve complete compatiblilty.
        private static readonly Version MaxVersion = new Version(Int32.MaxValue, Int32.MaxValue, Int32.MaxValue, Int32.MaxValue);
        private static readonly Dictionary<string, FrameworkName> _equivalentProjectFrameworks = new Dictionary<string, FrameworkName>()
        {
            { AspNetFrameworkIdentifier, new FrameworkName(".NETFramework", MaxVersion) },
        };

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

        /// <summary>
        /// This function tries to normalize a string that represents framework version names into
        /// something a framework name that the package manager understands.
        /// </summary>
        public static FrameworkName ParseFrameworkName(string frameworkName)
        {
            if (frameworkName == null)
            {
                throw new ArgumentNullException("frameworkName");
            }

            // {Identifier}{Version}-{Profile}

            // Split the framework name into 3 parts, identifier, version and profile.
            string identifierPart = null;
            string versionPart = null;

            string[] parts = frameworkName.Split('-');

            if (parts.Length > 2)
            {
                throw new ArgumentException(NuGetResources.InvalidFrameworkNameFormat, "frameworkName");
            }

            string frameworkNameAndVersion = parts.Length > 0 ? parts[0].Trim() : null;
            string profilePart = parts.Length > 1 ? parts[1].Trim() : null;

            if (String.IsNullOrEmpty(frameworkNameAndVersion))
            {
                throw new ArgumentException(NuGetResources.MissingFrameworkName, "frameworkName");
            }

            // If we find a version then we try to split the framework name into 2 parts
            var versionMatch = Regex.Match(frameworkNameAndVersion, @"\d+");

            if (versionMatch.Success)
            {
                identifierPart = frameworkNameAndVersion.Substring(0, versionMatch.Index).Trim();
                versionPart = frameworkNameAndVersion.Substring(versionMatch.Index).Trim();
            }
            else
            {
                // Otherwise we take the whole name as an identifier
                identifierPart = frameworkNameAndVersion.Trim();
            }

            if (!String.IsNullOrEmpty(identifierPart))
            {
                // Try to normalize the identifier to a known identifier
                if (!_knownIdentifiers.TryGetValue(identifierPart, out identifierPart))
                {
                    return UnsupportedFrameworkName;
                }
            }

            if (!String.IsNullOrEmpty(profilePart))
            {
                string knownProfile;
                if (_knownProfiles.TryGetValue(profilePart, out knownProfile))
                {
                    profilePart = knownProfile;
                }
            }

            Version version = null;
            // We support version formats that are integers (40 becomes 4.0)
            int versionNumber;
            if (Int32.TryParse(versionPart, out versionNumber))
            {
                // Remove the extra numbers
                if (versionPart.Length > 4)
                {
                    versionPart = versionPart.Substring(0, 4);
                }

                // Make sure it has at least 2 digits so it parses as a valid version
                versionPart = versionPart.PadRight(2, '0');
                versionPart = String.Join(".", versionPart.ToCharArray());
            }

            // If we can't parse the version then use the default
            if (!Version.TryParse(versionPart, out version))
            {
                // We failed to parse the version string once more. So we need to decide if this is unsupported or if we use the default version.
                // This framework is unsupported if:
                // 1. The identifier part of the framework name is null.
                // 2. The version part is not null.
                if (String.IsNullOrEmpty(identifierPart) || !String.IsNullOrEmpty(versionPart))
                {
                    return UnsupportedFrameworkName;
                }

                version = _emptyVersion;
            }

            if (String.IsNullOrEmpty(identifierPart))
            {
                identifierPart = NetFrameworkIdentifier;
            }

            // if this is a .NET Portable framework name, validate the profile part to ensure it is valid
            if (identifierPart.Equals(PortableFrameworkIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                ValidatePortableFrameworkProfilePart(profilePart);
            }

            return new FrameworkName(identifierPart, version, profilePart);
        }

        internal static void ValidatePortableFrameworkProfilePart(string profilePart)
        {
            if (String.IsNullOrEmpty(profilePart))
            {
                throw new ArgumentException(NuGetResources.PortableFrameworkProfileEmpty, "profilePart");
            }

            if (profilePart.Contains('-'))
            {
                throw new ArgumentException(NuGetResources.PortableFrameworkProfileHasDash, "profilePart");
            }

            if (profilePart.Contains(' '))
            {
                throw new ArgumentException(NuGetResources.PortableFrameworkProfileHasSpace, "profilePart");
            }

            string[] parts = profilePart.Split('+');
            if (parts.Any(p => String.IsNullOrEmpty(p)))
            {
                throw new ArgumentException(NuGetResources.PortableFrameworkProfileComponentIsEmpty, "profilePart");
            }

            // Prevent portable framework inside a portable framework - Inception
            if (parts.Any(p => p.StartsWith("portable", StringComparison.OrdinalIgnoreCase)) ||
                parts.Any(p => p.StartsWith("NETPortable", StringComparison.OrdinalIgnoreCase)) ||
                parts.Any(p => p.StartsWith(".NETPortable", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(NuGetResources.PortableFrameworkProfileComponentIsPortable, "profilePart");
            }
        }

        /// <summary>
        /// Trims trailing zeros in revision and build.
        /// </summary>
        public static Version TrimVersion(Version version)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }

            if (version.Build == 0 && version.Revision == 0)
            {
                version = new Version(version.Major, version.Minor);
            }
            else if (version.Revision == 0)
            {
                version = new Version(version.Major, version.Minor, version.Build);
            }

            return version;
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
            if (!TryParseVersionSpec(value, out versionInfo))
            {
                throw new ArgumentException(
                    String.Format(CultureInfo.CurrentCulture,
                     NuGetResources.InvalidVersionString, value));
            }

            return versionInfo;
        }

        public static bool TryParseVersionSpec(string value, out IVersionSpec result)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            var versionSpec = new VersionSpec();
            value = value.Trim();

            // First, try to parse it as a plain version string
            SemanticVersion version;
            if (SemanticVersion.TryParse(value, out version))
            {
                // A plain version is treated as an inclusive minimum range
                result = new VersionSpec
                {
                    MinVersion = version,
                    IsMinInclusive = true
                };

                return true;
            }

            // It's not a plain version, so it must be using the bracket arithmetic range syntax

            result = null;

            // Fail early if the string is too short to be valid
            if (value.Length < 3)
            {
                return false;
            }

            // The first character must be [ ot (
            switch (value.First())
            {
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
            switch (value.Last())
            {
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
            if (parts.Length > 2)
            {
                return false;
            }
            else if (parts.All(String.IsNullOrEmpty))
            {
                // If all parts are empty, then neither of upper or lower bounds were specified. Version spec is of the format (,]
                return false;
            }

            // If there is only one piece, we use it for both min and max
            string minVersionString = parts[0];
            string maxVersionString = (parts.Length == 2) ? parts[1] : parts[0];

            // Only parse the min version if it's non-empty
            if (!String.IsNullOrWhiteSpace(minVersionString))
            {
                if (!TryParseVersion(minVersionString, out version))
                {
                    return false;
                }
                versionSpec.MinVersion = version;
            }

            // Same deal for max
            if (!String.IsNullOrWhiteSpace(maxVersionString))
            {
                if (!TryParseVersion(maxVersionString, out version))
                {
                    return false;
                }
                versionSpec.MaxVersion = version;
            }

            // Successful parse!
            result = versionSpec;
            return true;
        }

        /// <summary>
        /// The safe range is defined as the highest build and revision for a given major and minor version
        /// </summary>
        public static IVersionSpec GetSafeRange(SemanticVersion version)
        {
            return new VersionSpec
            {
                IsMinInclusive = true,
                MinVersion = version,
                MaxVersion = new SemanticVersion(new Version(version.Version.Major, version.Version.Minor + 1))
            };
        }

        public static string PrettyPrint(IVersionSpec versionSpec)
        {
            if (versionSpec.MinVersion != null && versionSpec.IsMinInclusive && versionSpec.MaxVersion == null && !versionSpec.IsMaxInclusive)
            {
                return String.Format(CultureInfo.InvariantCulture, "({0} {1})", GreaterThanOrEqualTo, versionSpec.MinVersion);
            }

            if (versionSpec.MinVersion != null && versionSpec.MaxVersion != null && versionSpec.MinVersion == versionSpec.MaxVersion && versionSpec.IsMinInclusive && versionSpec.IsMaxInclusive)
            {
                return String.Format(CultureInfo.InvariantCulture, "(= {0})", versionSpec.MinVersion);
            }

            var versionBuilder = new StringBuilder();
            if (versionSpec.MinVersion != null)
            {
                if (versionSpec.IsMinInclusive)
                {
                    versionBuilder.AppendFormat(CultureInfo.InvariantCulture, "({0} ", GreaterThanOrEqualTo);
                }
                else
                {
                    versionBuilder.Append("(> ");
                }
                versionBuilder.Append(versionSpec.MinVersion);
            }

            if (versionSpec.MaxVersion != null)
            {
                if (versionBuilder.Length == 0)
                {
                    versionBuilder.Append("(");
                }
                else
                {
                    versionBuilder.Append(" && ");
                }

                if (versionSpec.IsMaxInclusive)
                {
                    versionBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0} ", LessThanOrEqualTo);
                }
                else
                {
                    versionBuilder.Append("< ");
                }
                versionBuilder.Append(versionSpec.MaxVersion);
            }

            if (versionBuilder.Length > 0)
            {
                versionBuilder.Append(")");
            }

            return versionBuilder.ToString();
        }

        public static string GetFrameworkString(FrameworkName frameworkName)
        {
            string name = frameworkName.Identifier + frameworkName.Version;
            if (String.IsNullOrEmpty(frameworkName.Profile))
            {
                return name;
            }
            return name + "-" + frameworkName.Profile;
        }

        public static string GetShortFrameworkName(FrameworkName frameworkName)
        {
            return GetShortFrameworkName(frameworkName, NetPortableProfileTable.Default);
        }

        public static string GetShortFrameworkName(FrameworkName frameworkName, NetPortableProfileTable portableProfileTable)
        {
            if (frameworkName == null)
            {
                throw new ArgumentNullException("frameworkName");
            }

            // Do a reverse lookup in _frameworkNameAlias. This is so that we can produce the more user-friendly
            // "windowsphone" string, rather than "sl3-wp". The latter one is also prohibited in portable framework's profile string.
            foreach (KeyValuePair<FrameworkName, FrameworkName> pair in _frameworkNameAlias)
            {
                // use our custom equality comparer because we want to perform case-insensitive comparison
                if (FrameworkNameEqualityComparer.Default.Equals(pair.Value, frameworkName))
                {
                    frameworkName = pair.Key;
                    break;
                }
            }

            string name;
            if (!_identifierToFrameworkFolder.TryGetValue(frameworkName.Identifier, out name))
            {
                name = frameworkName.Identifier;
            }

            // for Portable framework name, the short name has the form "portable-sl4+wp7+net45"
            string profile;
            if (name.Equals("portable", StringComparison.OrdinalIgnoreCase))
            {
                if (portableProfileTable == null)
                {
                    throw new ArgumentException(NuGetResources.PortableProfileTableMustBeSpecified, "portableProfileTable");
                }
                NetPortableProfile portableProfile = NetPortableProfile.Parse(frameworkName.Profile, portableProfileTable: portableProfileTable);
                if (portableProfile != null)
                {
                    profile = portableProfile.CustomProfileString;
                }
                else
                {
                    profile = frameworkName.Profile;
                }
            }
            else
            {
                // only show version part if it's > 0.0.0.0
                if (frameworkName.Version > new Version())
                {
                    // Remove the . from versions
                    name += frameworkName.Version.ToString().Replace(".", String.Empty);
                }

                if (String.IsNullOrEmpty(frameworkName.Profile))
                {
                    return name;
                }

                if (!_identifierToProfileFolder.TryGetValue(frameworkName.Profile, out profile))
                {
                    profile = frameworkName.Profile;
                }
            }

            return name + "-" + profile;
        }

        public static string GetTargetFrameworkLogString(FrameworkName targetFramework)
        {
            return (targetFramework == null || targetFramework == VersionUtility.EmptyFramework) ? NuGetResources.Debug_TargetFrameworkInfo_NotFrameworkSpecific : String.Empty;
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

            for (int i = 0; i < knownFolders.Length; i++)
            {
                string folderPrefix = knownFolders[i] + System.IO.Path.DirectorySeparatorChar;
                if (filePath.Length > folderPrefix.Length &&
                    filePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string frameworkPart = filePath.Substring(folderPrefix.Length);

                    try
                    {
                        return VersionUtility.ParseFrameworkFolderName(
                            frameworkPart,
                            strictParsing: knownFolders[i] == Constants.LibDirectory,
                            effectivePath: out effectivePath);
                    }
                    catch (ArgumentException)
                    {
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

            if (String.IsNullOrEmpty(targetFrameworkString))
            {
                return null;
            }

            var targetFramework = ParseFrameworkName(targetFrameworkString);
            if (strictParsing || targetFramework != UnsupportedFrameworkName)
            {
                // skip past the framework folder and the character \
                effectivePath = path.Substring(targetFrameworkString.Length + 1);
                return targetFramework;
            }

            return null;
        }

        public static bool TryGetCompatibleItems<T>(FrameworkName projectFramework, IEnumerable<T> items, out IEnumerable<T> compatibleItems) where T : IFrameworkTargetable
        {
            return TryGetCompatibleItems(projectFramework, items, NetPortableProfileTable.Default, out compatibleItems);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static bool TryGetCompatibleItems<T>(FrameworkName projectFramework, IEnumerable<T> items, NetPortableProfileTable portableProfileTable, out IEnumerable<T> compatibleItems) where T : IFrameworkTargetable
        {
            if (!items.Any())
            {
                compatibleItems = Enumerable.Empty<T>();
                return true;
            }

            // Not all projects have a framework, we need to consider those projects.
            var internalProjectFramework = projectFramework ?? EmptyFramework;

            // Turn something that looks like this:
            // item -> [Framework1, Framework2, Framework3] into
            // [{item, Framework1}, {item, Framework2}, {item, Framework3}]
            var normalizedItems = from item in items
                                  let frameworks = (item.SupportedFrameworks != null && item.SupportedFrameworks.Any()) ? item.SupportedFrameworks : new FrameworkName[] { null }
                                  from framework in frameworks
                                  select new
                                  {
                                      Item = item,
                                      TargetFramework = framework
                                  };

            // Group references by target framework (if there is no target framework we assume it is the default)
            var frameworkGroups = normalizedItems.GroupBy(g => g.TargetFramework, g => g.Item).ToList();

            // Try to find the best match
            // Not all projects have a framework, we need to consider those projects.
            compatibleItems = (from g in frameworkGroups
                               where g.Key != null && IsCompatible(internalProjectFramework, g.Key, portableProfileTable)
                               orderby GetProfileCompatibility(internalProjectFramework, g.Key, portableProfileTable) descending
                               select g).FirstOrDefault();

            bool hasItems = compatibleItems != null && compatibleItems.Any();
            if (!hasItems)
            {
                // if there's no matching profile, fall back to the items without target framework
                // because those are considered to be compatible with any target framework
                compatibleItems = frameworkGroups.Where(g => g.Key == null).SelectMany(g => g);
                hasItems = compatibleItems != null && compatibleItems.Any();
            }

            if (!hasItems)
            {
                compatibleItems = null;
            }

            return hasItems;
        }

        

        internal static Version NormalizeVersion(Version version)
        {
            return new Version(version.Major,
                               version.Minor,
                               Math.Max(version.Build, 0),
                               Math.Max(version.Revision, 0));
        }

        public static FrameworkName NormalizeFrameworkName(FrameworkName framework)
        {
            FrameworkName aliasFramework;
            if (_frameworkNameAlias.TryGetValue(framework, out aliasFramework))
            {
                return aliasFramework;
            }

            return framework;
        }

        /// <summary>
        /// Returns all possible versions for a version. i.e. 1.0 would return 1.0, 1.0.0, 1.0.0.0
        /// </summary>
        public static IEnumerable<SemanticVersion> GetPossibleVersions(SemanticVersion semver)
        {
            // Trim the version so things like 1.0.0.0 end up being 1.0
            Version version = TrimVersion(semver.Version);

            yield return new SemanticVersion(version, semver.SpecialVersion);

            if (version.Build == -1 && version.Revision == -1)
            {
                yield return new SemanticVersion(new Version(version.Major, version.Minor, 0), semver.SpecialVersion);
                yield return new SemanticVersion(new Version(version.Major, version.Minor, 0, 0), semver.SpecialVersion);
            }
            else if (version.Revision == -1)
            {
                yield return new SemanticVersion(new Version(version.Major, version.Minor, version.Build, 0), semver.SpecialVersion);
            }
        }

        public static bool IsCompatible(FrameworkName projectFrameworkName, IEnumerable<FrameworkName> packageSupportedFrameworks)
        {
            return IsCompatible(projectFrameworkName, packageSupportedFrameworks, NetPortableProfileTable.Default);
        }

        public static bool IsCompatible(FrameworkName projectFrameworkName, IEnumerable<FrameworkName> packageSupportedFrameworks, NetPortableProfileTable portableProfileTable)
        {
            if (packageSupportedFrameworks.Any())
            {
                return packageSupportedFrameworks.Any(packageSupportedFramework => IsCompatible(projectFrameworkName, packageSupportedFramework, portableProfileTable));
            }

            // No supported frameworks means that everything is supported.
            return true;
        }

        internal static bool IsCompatible(FrameworkName projectFrameworkName, FrameworkName packageTargetFrameworkName)
        {
            return IsCompatible(projectFrameworkName, packageTargetFrameworkName, NetPortableProfileTable.Default);
        }

        /// <summary>
        /// Determines if a package's target framework can be installed into a project's framework.
        /// </summary>
        /// <param name="projectFrameworkName">The project's framework</param>
        /// <param name="packageTargetFrameworkName">The package's target framework</param>
        internal static bool IsCompatible(FrameworkName projectFrameworkName, FrameworkName packageTargetFrameworkName, NetPortableProfileTable portableProfileTable)
        {
            if (projectFrameworkName == null)
            {
                return true;
            }

            // Treat portable library specially
            if (packageTargetFrameworkName.IsPortableFramework())
            {
                return IsPortableLibraryCompatible(projectFrameworkName, packageTargetFrameworkName, portableProfileTable);
            }

            packageTargetFrameworkName = NormalizeFrameworkName(packageTargetFrameworkName);
            projectFrameworkName = NormalizeFrameworkName(projectFrameworkName);

            if (!projectFrameworkName.Identifier.Equals(packageTargetFrameworkName.Identifier, StringComparison.OrdinalIgnoreCase))
            {
                // Try to convert the project framework into an equivalent target framework
                // If the identifiers didn't match, we need to see if this framework has an equivalent framework that DOES match.
                // If it does, we use that from here on.
                // Example:
                //  If the Project Targets ASP.Net, Version=5.0. It can accept Packages targetting .NETFramework, Version=4.5.1
                //  so since the identifiers don't match, we need to "translate" the project target framework to .NETFramework
                //  however, we still want direct ASP.Net == ASP.Net matches, so we do this ONLY if the identifiers don't already match
                FrameworkName equivalentFramework;
                if (_equivalentProjectFrameworks.TryGetValue(projectFrameworkName.Identifier, out equivalentFramework) &&
                    equivalentFramework.Identifier.Equals(packageTargetFrameworkName.Identifier, StringComparison.OrdinalIgnoreCase))
                {
                    projectFrameworkName = equivalentFramework;
                }
                else
                {
                return false;
            }
            }

            if (NormalizeVersion(projectFrameworkName.Version) <
                NormalizeVersion(packageTargetFrameworkName.Version))
            {
                return false;
            }

            // If the profile names are equal then they're compatible
            if (String.Equals(projectFrameworkName.Profile, packageTargetFrameworkName.Profile, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Get the compatibility mapping for this framework identifier
            CompatibilityMapping mapping;
            if (_compatibiltyMapping.TryGetValue(projectFrameworkName.Identifier, out mapping))
            {
                // Get all compatible profiles for the target profile
                string[] compatibleProfiles;
                if (mapping.TryGetValue(packageTargetFrameworkName.Profile, out compatibleProfiles))
                {
                    // See if this profile is in the list of compatible profiles
                    return compatibleProfiles.Contains(projectFrameworkName.Profile, StringComparer.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        private static bool IsPortableLibraryCompatible(FrameworkName projectFrameworkName, FrameworkName packageTargetFrameworkName, NetPortableProfileTable portableProfileTable)
        {
            if (String.IsNullOrEmpty(packageTargetFrameworkName.Profile))
            {
                return false;
            }

            NetPortableProfile targetFrameworkPortableProfile = NetPortableProfile.Parse(packageTargetFrameworkName.Profile, portableProfileTable: portableProfileTable);
            if (targetFrameworkPortableProfile == null)
            {
                return false;
            }

            if (projectFrameworkName.IsPortableFramework())
            {
                // this is the case with Portable Library vs. Portable Library
                if (String.Equals(projectFrameworkName.Profile, packageTargetFrameworkName.Profile, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                NetPortableProfile frameworkPortableProfile = NetPortableProfile.Parse(projectFrameworkName.Profile, portableProfileTable: portableProfileTable);
                if (frameworkPortableProfile == null)
                {
                    return false;
                }

                return targetFrameworkPortableProfile.IsCompatibleWith(frameworkPortableProfile, portableProfileTable);
            }
            else
            {
                // this is the case with Portable Library installed into a normal project
                return targetFrameworkPortableProfile.IsCompatibleWith(projectFrameworkName);
            }
        }

        /// <summary>
        /// Given 2 framework names, this method returns a number which determines how compatible
        /// the names are. The higher the number the more compatible the frameworks are.
        /// </summary>
        private static long GetProfileCompatibility(FrameworkName projectFrameworkName, FrameworkName packageTargetFrameworkName, NetPortableProfileTable portableProfileTable)
        {
            projectFrameworkName = NormalizeFrameworkName(projectFrameworkName);
            packageTargetFrameworkName = NormalizeFrameworkName(packageTargetFrameworkName);

            if (packageTargetFrameworkName.IsPortableFramework())
            {
                if (projectFrameworkName.IsPortableFramework())
                {
                    return GetCompatibilityBetweenPortableLibraryAndPortableLibrary(projectFrameworkName, packageTargetFrameworkName, portableProfileTable);
                }
                else
                {
                    // we divide by 2 to ensure Portable framework has less compatibility value than specific framework.
                    return GetCompatibilityBetweenPortableLibraryAndNonPortableLibrary(projectFrameworkName, packageTargetFrameworkName, portableProfileTable) / 2;
                }
            }

            long compatibility = 0;

            // Calculate the "distance" between the target framework version and the project framework version.
            // When comparing two framework candidates, we pick the one with higher version.
            compatibility += CalculateVersionDistance(
                projectFrameworkName.Version,
                GetEffectiveFrameworkVersion(projectFrameworkName, packageTargetFrameworkName, portableProfileTable));

            // Things with matching profiles are more compatible than things without.
            // This means that if we have net40 and net40-client assemblies and the target framework is
            // net40, both sets of assemblies are compatible but we prefer net40 since it matches
            // the profile exactly.
            if (packageTargetFrameworkName.Profile.Equals(projectFrameworkName.Profile, StringComparison.OrdinalIgnoreCase))
            {
                compatibility++;
            }

            // this is to give specific profile higher compatibility than portable profile
            if (packageTargetFrameworkName.Identifier.Equals(projectFrameworkName.Identifier, StringComparison.OrdinalIgnoreCase))
            {
                // Let's say a package has two framework folders: 'net40' and 'portable-net45+wp8'.
                // The package is installed into a net45 project. We want to pick the 'net40' folder, even though
                // the 'net45' in portable folder has a matching version with the project's framework.
                //
                // So, in order to achieve that, here we give the folder that has matching identifer with the project's 
                // framework identifier a compatibility score of 10, to make sure it weighs more than the compatibility of matching version.

                compatibility += 10 * (1L << 32);
            }

            return compatibility;
        }

        private static long CalculateVersionDistance(Version projectVersion, Version targetFrameworkVersion)
        {
            // the +5 is to counter the profile compatibility increment (+1)
            const long MaxValue = 1L << 32 + 5;

            // calculate the "distance" between 2 versions
            var distance = (projectVersion.Major - targetFrameworkVersion.Major) * 255L * 255 * 255 +
                           (projectVersion.Minor - targetFrameworkVersion.Minor) * 255L * 255 +
                           (projectVersion.Build - targetFrameworkVersion.Build) * 255L +
                           (projectVersion.Revision - targetFrameworkVersion.Revision);

            Debug.Assert(MaxValue >= distance);

            // the closer the versions are, the higher the returned value is.
            return MaxValue - distance;
        }

        private static Version GetEffectiveFrameworkVersion(FrameworkName projectFramework, FrameworkName targetFrameworkVersion, NetPortableProfileTable portableProfileTable)
        {
            if (targetFrameworkVersion.IsPortableFramework())
            {
                NetPortableProfile profile = NetPortableProfile.Parse(targetFrameworkVersion.Profile, portableProfileTable: portableProfileTable);
                if (profile != null)
                {
                    // if it's a portable library, return the version of the matching framework
                    var compatibleFramework = profile.SupportedFrameworks.FirstOrDefault(f => VersionUtility.IsCompatible(projectFramework, f, portableProfileTable));
                    if (compatibleFramework != null)
                    {
                        return compatibleFramework.Version;
                    }
                }
            }

            return targetFrameworkVersion.Version;
        }

        /// <summary>
        /// Attempt to calculate how compatible a portable framework folder is to a portable project.
        /// The two portable frameworks passed to this method MUST be compatible with each other.
        /// </summary>
        /// <remarks>
        /// The returned score will be negative value.
        /// </remarks>
        internal static int GetCompatibilityBetweenPortableLibraryAndPortableLibrary(FrameworkName projectFrameworkName, FrameworkName packageTargetFrameworkName)
        {
            return GetCompatibilityBetweenPortableLibraryAndPortableLibrary(projectFrameworkName, packageTargetFrameworkName, NetPortableProfileTable.Default);
        }
        internal static int GetCompatibilityBetweenPortableLibraryAndPortableLibrary(FrameworkName projectFrameworkName, FrameworkName packageTargetFrameworkName, NetPortableProfileTable portableProfileTable)
        {
            // Algorithms: Give a score from 0 to N indicating how close *in version* each package platform is the project’s platforms 
            // and then choose the folder with the lowest score. If the score matches, choose the one with the least platforms.
            // 
            // For example:
            // 
            // Project targeting: .NET 4.5 + SL5 + WP71
            // 
            // Package targeting:
            // .NET 4.5 (0) + SL5 (0) + WP71 (0)                            == 0
            // .NET 4.5 (0) + SL5 (0) + WP71 (0) + Win8 (0)                 == 0
            // .NET 4.5 (0) + SL4 (1) + WP71 (0) + Win8 (0)                 == 1
            // .NET 4.0 (1) + SL4 (1) + WP71 (0) + Win8 (0)                 == 2
            // .NET 4.0 (1) + SL4 (1) + WP70 (1) + Win8 (0)                 == 3
            // 
            // Above, there’s two matches with the same result, choose the one with the least amount of platforms.
            // 
            // There will be situations, however, where there is still undefined behavior, such as:
            // 
            // .NET 4.5 (0) + SL4 (1) + WP71 (0)                            == 1
            // .NET 4.0 (1) + SL5 (0) + WP71 (0)                            == 1

            NetPortableProfile projectFrameworkProfile = NetPortableProfile.Parse(projectFrameworkName.Profile, portableProfileTable: portableProfileTable);
            Debug.Assert(projectFrameworkProfile != null);

            NetPortableProfile packageTargetFrameworkProfile = NetPortableProfile.Parse(packageTargetFrameworkName.Profile, treatOptionalFrameworksAsSupportedFrameworks: true, portableProfileTable: portableProfileTable);
            Debug.Assert(packageTargetFrameworkProfile != null);

            int nonMatchingCompatibleFrameworkCount = 0;
            int inCompatibleOptionalFrameworkCount = 0;
            foreach (var supportedPackageTargetFramework in packageTargetFrameworkProfile.SupportedFrameworks)
            {
                var compatibleProjectFramework = projectFrameworkProfile.SupportedFrameworks.FirstOrDefault(f => IsCompatible(f, supportedPackageTargetFramework, portableProfileTable));
                if (compatibleProjectFramework != null && compatibleProjectFramework.Version > supportedPackageTargetFramework.Version)
                {
                    nonMatchingCompatibleFrameworkCount++;
                }
            }

            foreach (var optionalProjectFramework in projectFrameworkProfile.OptionalFrameworks)
            {
                var compatiblePackageTargetFramework = packageTargetFrameworkProfile.SupportedFrameworks.FirstOrDefault(f => IsCompatible(f, optionalProjectFramework, portableProfileTable));
                if(compatiblePackageTargetFramework == null || compatiblePackageTargetFramework.Version > optionalProjectFramework.Version)
                {
                    inCompatibleOptionalFrameworkCount++;
                }
                else if (compatiblePackageTargetFramework != null && compatiblePackageTargetFramework.Version < optionalProjectFramework.Version)
                {
                    // we check again if the package version < project version, because, if they are equal, they are matching compatible frameworks
                    // neither inCompatibleOptionalFrameworkCount nor nonMatchingCompatibleFrameworkCount should be incremented
                    nonMatchingCompatibleFrameworkCount++;
                }
            }

            // The following is the maximum project framework count which is also the maximum possible incompatibilities
            int maxPossibleIncompatibleFrameworkCount = 1 + projectFrameworkProfile.SupportedFrameworks.Count + projectFrameworkProfile.OptionalFrameworks.Count;

            // This is to ensure that profile with compatible optional frameworks wins over profiles without, even, when supported frameworks are highly compatible
            // If there are no incompatible optional frameworks, the score below will be simply nonMatchingCompatibleFrameworkCount
            // For example, Let Project target net45+sl5+monotouch+monoandroid. And, Package has 4 profiles, (THIS EXAMPLE IS LIKELY NOT A REAL_WORLD SCENARIO :))
            // A: net45+sl5, B: net40+sl5+monotouch, C: net40+sl4+monotouch+monoandroid, D: net40+sl4+monotouch+monoandroid+wp71
            // At this point, Compatibility is as follows. C = D > B > A. Scores for A = (5 * 2 + 0), B = (5 * 1 + 1), C = (5 * 0 + 2), D = (5 * 0 + 2)
            // The scores are 10, 6, 2 and 2. Both C and D are the most compatible with a score of 2
            // Clearly, having more number of frameworks, supported and optional, that are compatible is preferred over most compatible supported frameworks alone
            int score = maxPossibleIncompatibleFrameworkCount * inCompatibleOptionalFrameworkCount +
                nonMatchingCompatibleFrameworkCount;

            // This is to ensure that if two portable frameworks have the same score,
            // we pick the one that has less number of supported platforms.
            // In the example described in comments above, both C and D had an equal score of 2. With the following correction, new scores are as follows
            // A = (10 * 50 + 2), B = (6 * 50 + 3), C = (2 * 50 + 4), D = (2 * 50 + 5)
            // A = 502, B = 303, C = 104, D = 105. And, C has the lowest score and the most compatible
            score = score * 50 + packageTargetFrameworkProfile.SupportedFrameworks.Count;

            // Our algorithm returns lowest score for the most compatible framework. 
            // However, the caller of this method expects it to have the highest score. 
            // Hence, we return the negative value of score here.
            return -score;
        }

        internal static long GetCompatibilityBetweenPortableLibraryAndNonPortableLibrary(FrameworkName projectFrameworkName, FrameworkName packagePortableFramework)
        {
            return GetCompatibilityBetweenPortableLibraryAndNonPortableLibrary(projectFrameworkName, packagePortableFramework, NetPortableProfileTable.Default);
        }
        internal static long GetCompatibilityBetweenPortableLibraryAndNonPortableLibrary(FrameworkName projectFrameworkName, FrameworkName packagePortableFramework, NetPortableProfileTable portableProfileTable)
        {
            NetPortableProfile packageFrameworkProfile = NetPortableProfile.Parse(packagePortableFramework.Profile, treatOptionalFrameworksAsSupportedFrameworks: true, portableProfileTable: portableProfileTable);
            if (packageFrameworkProfile == null)
            {
                // defensive coding, this should never happen
                Debug.Fail("'portableFramework' is not a valid portable framework.");
                return long.MinValue;
            }

            // among the supported frameworks by the Portable library, pick the one that is compatible with 'projectFrameworkName'
            var compatibleFramework = packageFrameworkProfile.SupportedFrameworks.FirstOrDefault(f => IsCompatible(projectFrameworkName, f, portableProfileTable));

            if (compatibleFramework != null)
            {
                var score = GetProfileCompatibility(projectFrameworkName, compatibleFramework, portableProfileTable);

                // This is to ensure that if two portable frameworks have the same score,
                // we pick the one that has less number of supported platforms.
                // The *2 is to make up for the /2 to which the result of this method is subject.
                score -= (packageFrameworkProfile.SupportedFrameworks.Count * 2);

                return score;
            }
            else if(portableProfileTable.HasCompatibleProfileWith(packageFrameworkProfile, projectFrameworkName, portableProfileTable))
            {
                // Get the list of portable profiles that supports projectFrameworkName
                // And, see if there is atleast 1 profile which is compatible with packageFrameworkProfile
                // If so, return 0 - (packageFrameworkProfile.SupportedFrameworks.Count * 2)
                return 0 - (packageFrameworkProfile.SupportedFrameworks.Count * 2);
            }

            return long.MinValue;
        }

        private static bool TryParseVersion(string versionString, out SemanticVersion version)
        {
            version = null;
            if (!SemanticVersion.TryParse(versionString, out version))
            {
                // Support integer version numbers (i.e. 1 -> 1.0)
                int versionNumber;
                if (Int32.TryParse(versionString, out versionNumber) && versionNumber > 0)
                {
                    version = new SemanticVersion(new Version(versionNumber, 0));
                }
            }
            return version != null;
        }

        public static bool IsPortableFramework(this FrameworkName framework)
        {
            // The profile part has been verified in the ParseFrameworkName() method. 
            // By the time it is called here, it's guaranteed to be valid.
            // Thus we can ignore the profile part here
            return framework != null && PortableFrameworkIdentifier.Equals(framework.Identifier, StringComparison.OrdinalIgnoreCase);
        }
    }
}
