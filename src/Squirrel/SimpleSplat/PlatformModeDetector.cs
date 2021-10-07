using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

#if SILVERLIGHT
using System.Windows;
#elif NETFX_CORE
using Windows.ApplicationModel;
#endif

namespace Squirrel.SimpleSplat
{
    public class PlatformModeDetector : IModeDetector
    {
        public bool? InUnitTestRunner()
        {
            var testAssemblies = new[] {
                "CSUNIT",
                "NUNIT",
                "XUNIT",
                "MBUNIT",
                "NBEHAVE",
            };

            try {
                return searchForAssembly(testAssemblies);
            } catch (Exception) {
                return null;
            }
        }

        public bool? InDesignMode()
        {
#if SILVERLIGHT
            if (Application.Current.RootVisual != null) {
                return System.ComponentModel.DesignerProperties.GetIsInDesignMode(Application.Current.RootVisual);
            }

            return false;
#elif NETFX_CORE
            return DesignMode.DesignModeEnabled;
#else
            var designEnvironments = new[] {
                "BLEND.EXE",
                "XDESPROC.EXE",
            };

            var entry = Assembly.GetEntryAssembly();
            if (entry != null) {
                var exeName = (new FileInfo(entry.Location)).Name.ToUpperInvariant();

                if (designEnvironments.Any(x => x.Contains(exeName))) {
                    return true;
                }
            }

            return false;
#endif
        }
        
        static bool searchForAssembly(IEnumerable<string> assemblyList)
        {
#if SILVERLIGHT
            return Deployment.Current.Parts.Any(x => assemblyList.Any(name => x.Source.ToUpperInvariant().Contains(name)));
#elif NETFX_CORE
            var depPackages = Package.Current.Dependencies.Select(x => x.Id.FullName);
            if (depPackages.Any(x => assemblyList.Any(name => x.ToUpperInvariant().Contains(name)))) return true;

            var fileTask = Task.Factory.StartNew(async () => {
                var files = await Package.Current.InstalledLocation.GetFilesAsync();
                return files.Select(x => x.Path).ToArray();
            }, TaskCreationOptions.HideScheduler).Unwrap();

            return fileTask.Result.Any(x => assemblyList.Any(name => x.ToUpperInvariant().Contains(name)));
#else
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => assemblyList.Any(name => x.FullName.ToUpperInvariant().Contains(name)));
#endif
        }
    }
}
