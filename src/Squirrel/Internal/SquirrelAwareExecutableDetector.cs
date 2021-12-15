using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Squirrel.NuGet;
using static Squirrel.NativeMethods;

namespace Squirrel
{
    static class SquirrelAwareExecutableDetector
    {
        const string SQUIRREL_AWARE_KEY = "SquirrelAwareVersion";
        const uint LOAD_LIBRARY_AS_DATAFILE = 2;

        public static List<string> GetAllSquirrelAwareApps(string directory, int minimumVersion = 1)
        {
            var di = new DirectoryInfo(directory);

            return di.EnumerateFiles()
                .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.FullName)
                .Where(x => (GetSquirrelAwareVersion(x) ?? -1) >= minimumVersion)
                .ToList();
        }

        public static int? GetSquirrelAwareVersion(string exePath)
        {
            if (!File.Exists(exePath)) return null;
            var fullname = Path.GetFullPath(exePath);

            try {
                return Utility.Retry<int?>(() => {
                    try {
                        var maniVer = GetManifestSquirrelAwareValue(exePath);
                        if (maniVer != null)
                            return GetManifestSquirrelAwareValue(exePath);
                    } catch { }

                    try {
                        var vblockVer = GetVersionBlockSquirrelAwareValue(exePath);
                        if (vblockVer != null)
                            return GetVersionBlockSquirrelAwareValue(exePath);
                    } catch { }

                    throw new Exception("Unable to determine Squirrel version"); // triggers a retry
                });
            } catch {
                return null;
            }
        }

        static int? GetVersionBlockSquirrelAwareValue(string executable)
        {
            int size = NativeMethods.GetFileVersionInfoSize(executable, IntPtr.Zero);

            // Nice try, buffer overflow
            if (size <= 0 || size > 4096) return null;

            var buf = new byte[size];
            if (!NativeMethods.GetFileVersionInfo(executable, 0, size, buf)) return null;

            const string englishUS = "040904B0";
            const string neutral = "000004B0";
            var supportedLanguageCodes = new[] { englishUS, neutral };

            IntPtr result;
            int resultSize;
            if (!supportedLanguageCodes.Any(
                languageCode =>
                    NativeMethods.VerQueryValue(
                        buf,
                        $"\\StringFileInfo\\{languageCode}\\SquirrelAwareVersion",
                        out result, out resultSize
                    )
            )) {
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

        static int? GetManifestSquirrelAwareValue(string executable)
        {
            var buffer = GetManifestFromPEResources(executable);
            if (buffer == null)
                return null;

            var document = XDocument.Load(new MemoryStream(buffer));
            var aware = document.Root.ElementsNoNamespace(SQUIRREL_AWARE_KEY).FirstOrDefault();
            if (aware != null && int.TryParse(aware.Value, out var pv)) {
                return pv;
            }

            return null;
        }

        static byte[] GetManifestFromPEResources(string filePath)
        {
            var hModule = LoadLibraryEx(filePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (hModule == IntPtr.Zero) {
                throw new Win32Exception();
            }

            try {
                var hResource = FindResource(hModule, "#1", "#24");
                if (hResource == IntPtr.Zero)
                    return null;

                uint size = SizeofResource(hModule, hResource);
                if (size == 0)
                    throw new Win32Exception();

                var hGlobal = LoadResource(hModule, hResource);
                if (hGlobal == IntPtr.Zero)
                    throw new Win32Exception();

                var data = LockResource(hGlobal);
                if (data == IntPtr.Zero)
                    throw new Win32Exception(0x21);

                var buf = new byte[size];
                Marshal.Copy(data, buf, 0, (int) size);
                return buf;
            } finally {
                FreeLibrary(hModule);
            }
        }
    }
}
