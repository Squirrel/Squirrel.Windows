using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel
{
    static class SquirrelAwareExecutableDetector
    {
        public static List<string> GetAllSquirrelAwareApps(string directory, int minimumVersion = 1)
        {
            var di = new DirectoryInfo(directory);

            return di.EnumerateFiles()
                .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.FullName)
                .Where(x => (GetPESquirrelAwareVersion(x) ?? -1) >= minimumVersion)
                .ToList();
        }

        public static int? GetPESquirrelAwareVersion(string executable)
        {
            if (!File.Exists(executable)) return null;
            var fullname = Path.GetFullPath(executable);

            return Utility.Retry<int?>(() => 
                GetAssemblySquirrelAwareVersion(fullname) ?? GetVersionBlockSquirrelAwareValue(fullname));
        }

        static int? GetAssemblySquirrelAwareVersion(string executable)
        {
            try {
                //ReflectionOnlyLoadFrom to load assembly without dependencies in isolated reflection-only context
                Assembly assembly = Assembly.ReflectionOnlyLoadFrom(executable);

                var attributeDataList = CustomAttributeData.GetCustomAttributes(assembly);
                if (attributeDataList == null || attributeDataList.Count == 0)
                    return null;

                foreach (var attributeData in attributeDataList.Where(ad => ad.AttributeType == typeof(AssemblyMetadataAttribute))){
                    var constructorArguments = attributeData.ConstructorArguments;
                    if (constructorArguments[0].Value as string == "SquirrelAwareVersion"){
                        int result;
                        if (!Int32.TryParse(constructorArguments[1].Value as string, NumberStyles.Integer, CultureInfo.CurrentCulture, out result))
                        {
                            return null;
                        }

                        return result;
                    }

                }
                return null;
            } 
            catch (FileLoadException) { return null; }
            catch (BadImageFormatException) { return null; }
        }

        static int? GetVersionBlockSquirrelAwareValue(string executable)
        {
            int size = NativeMethods.GetFileVersionInfoSize(executable, IntPtr.Zero);

            // Nice try, buffer overflow
            if (size <= 0 || size > 4096) return null;

            var buf = new byte[size];
            if (!NativeMethods.GetFileVersionInfo(executable, IntPtr.Zero, size, buf)) return null;

            IntPtr result; int resultSize;
            if (!NativeMethods.VerQueryValue(buf, "\\StringFileInfo\\040904B0\\SquirrelAwareVersion", out result, out resultSize)) {
                return null;
            }

            // NB: I have **no** idea why, but Atom.exe won't return the version
            // number "1" despite it being in the resource file and being 100% 
            // identical to the version block that actually works. I've got stuff
            // to ship, so we're just going to return '1' if we find the name in 
            // the block at all. I hate myself for this.
            return 1;

#if __NOT__DEFINED_EVAR__
            int ret;
            string resultData = Marshal.PtrToStringAnsi(result, resultSize-1 /* Subtract one for null terminator */);
            if (!Int32.TryParse(resultData, NumberStyles.Integer, CultureInfo.CurrentCulture, out ret)) return null;

            return ret;
#endif
        }
    }
}
