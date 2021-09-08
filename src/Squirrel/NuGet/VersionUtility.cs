using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace NuGet
{
    internal static class VersionUtility
    {
        private const string LessThanOrEqualTo = "\u2264";
        private const string GreaterThanOrEqualTo = "\u2265";

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
    }
}
