using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Squirrel.Lib;
using Squirrel.NuGet;

namespace Squirrel
{
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    internal static class SquirrelAwareExecutableDetector
    {
        const string SQUIRREL_AWARE_KEY = "SquirrelAwareVersion";

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

            // ways to search for SquirrelAwareVersion, ordered by precedence
            // search exe-embedded values first, and if not found, move on to sidecar files
            var detectors = new Func<string, int?>[] {
                GetEmbeddedManifestSquirrelAwareValue,
                GetVersionBlockSquirrelAwareValue,
                GetSidecarSquirrelAwareValue,
                GetSideBySideManifestSquirrelAwareValue,
                GetSideBySideDllManifestSquirrelAwareValue,
            };

            for (int i = 0; i < 3; i++) {
                foreach (var fn in detectors) {
                    try {
                        var v = fn(exePath);
                        if (v != null) return v;
                    } catch {
                        // do not throw, otherwise other detectors will not run
                    }
                }
                // retry 3 times with 100ms delay
                Thread.Sleep(100);
            }

            return null;
        }

        static int? GetVersionBlockSquirrelAwareValue(string executable)
        {
            return StringFileInfo.ReadVersionInfo(executable, out var vi)
                .Where(i => i.Key == SQUIRREL_AWARE_KEY)
                .Where(i => int.TryParse(i.Value, out var _))
                .Select(i => (int?)int.Parse(i.Value))
                .FirstOrDefault(i => i > 0);
        }

        static int? GetSidecarSquirrelAwareValue(string executable)
        {
            // Looks for a "MyApp.exe.squirrel" sidecar file
            // the file should contain just the integer version (eg. "1")
            var sidecarPath = executable + ".squirrel";
            if (File.Exists(sidecarPath)) {
                var txt = File.ReadAllText(sidecarPath);
                if (int.TryParse(txt, out var pv)) {
                    return pv;
                }
            }
            return null;
        }

        static int? GetSideBySideManifestSquirrelAwareValue(string executable)
        {
            // Looks for an external application manifest eg. "MyApp.exe.manifest"
            var manifestPath = executable + ".manifest";
            if (File.Exists(manifestPath)) {
                return ParseManifestAwareValue(File.ReadAllBytes(manifestPath));
            }
            return null;
        }

        static int? GetSideBySideDllManifestSquirrelAwareValue(string executable)
        {
            // Looks for an external application DLL manifest eg. "MyApp.dll.manifest"
            var manifestPath = Path.Combine(
                Path.GetDirectoryName(executable),
                Path.GetFileNameWithoutExtension(executable) + ".dll.manifest");
            if (File.Exists(manifestPath)) {
                return ParseManifestAwareValue(File.ReadAllBytes(manifestPath));
            }
            return null;
        }

        static int? GetEmbeddedManifestSquirrelAwareValue(string executable)
        {
            // Looks for an embedded application manifest
            byte[] buffer = null;
            using (var rr = new ResourceReader(executable))
                buffer = rr.ReadAssemblyManifest();
            return ParseManifestAwareValue(buffer);
        }

        static int? ParseManifestAwareValue(byte[] buffer)
        {
            if (buffer == null)
                return null;

            var document = XDocument.Load(new MemoryStream(buffer));
            var aware = document.Root.ElementsNoNamespace(SQUIRREL_AWARE_KEY).FirstOrDefault();
            if (aware != null && int.TryParse(aware.Value, out var pv)) {
                return pv;
            }

            return null;
        }
    }
}
