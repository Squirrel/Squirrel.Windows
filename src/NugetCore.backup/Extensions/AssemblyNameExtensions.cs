using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace NuGet.Runtime
{
    public static class AssemblyNameExtensions
    {
        /// <summary>
        /// Returns the public key token as hex. e.g. 31bf3856ad364e35
        /// </summary>
        public static string GetPublicKeyTokenString(this AssemblyName assemblyName)
        {
            return String.Join(String.Empty, assemblyName.GetPublicKeyToken()
                                                         .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
