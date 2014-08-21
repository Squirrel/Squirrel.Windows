using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Squirrel
{
    public static class VersionExtensions
    {
        public static Version ToVersion(this IReleasePackage package)
        {
            return package.InputPackageFile.ToVersion();
        }

        public static Version ToVersion(this string fileName)
        {
            var parts = (new FileInfo(fileName)).Name
                .Replace(".nupkg", "").Replace("-delta", "")
                .Split('.', '-').Reverse();

            var numberRegex = new Regex(@"^\d+$");

            var versionFields = parts
                .Where(x => numberRegex.IsMatch(x))
                .Select(Int32.Parse)
                .Reverse()
                .ToArray();

            if (versionFields.Length < 2 || versionFields.Length > 4) {
                return null;
            }

            switch (versionFields.Length) {
            case 2:
                return new Version(versionFields[0], versionFields[1]);
            case 3:
                return new Version(versionFields[0], versionFields[1], versionFields[2]);
            case 4:
                return new Version(versionFields[0], versionFields[1], versionFields[2], versionFields[3]);
            }

            return null;
        }
    }
}
