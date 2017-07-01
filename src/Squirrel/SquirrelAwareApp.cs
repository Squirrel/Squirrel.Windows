using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
    /// <summary>
    /// SquirrelAwareApp helps you to handle Squirrel app activation events
    /// correctly.
    /// </summary>
    public static class SquirrelAwareApp
    {
        /// <summary>
        /// Call this method as early as possible in app startup. This method
        /// will dispatch to your methods to set up your app. Depending on the
        /// parameter, your app will exit after this method is called, which 
        /// is required by Squirrel. UpdateManager has methods to help you to
        /// do this, such as CreateShortcutForThisExe.
        /// </summary>
        /// <param name="onInitialInstall">Called when your app is initially
        /// installed. Set up app shortcuts here as well as file associations.
        /// </param>
        /// <param name="onAppUpdate">Called when your app is updated to a new
        /// version.</param>
        /// <param name="onAppObsoleted">Called when your app is no longer the
        /// latest version (i.e. they have installed a new version and your app
        /// is now the old version)</param>
        /// <param name="onAppUninstall">Called when your app is uninstalled 
        /// via Programs and Features. Remove all of the things that you created
        /// in onInitialInstall.</param>
        /// <param name="onFirstRun">Called the first time an app is run after
        /// being installed. Your application will **not** exit after this is
        /// dispatched, you should use this as a hint (i.e. show a 'Welcome' 
        /// screen, etc etc.</param>
        /// <param name="arguments">Use in a unit-test runner to mock the 
        /// arguments. In your app, leave this as null.</param>
        public static void HandleEvents(
            Action<Version> onInitialInstall = null,
            Action<Version> onAppUpdate = null,
            Action<Version> onAppObsoleted = null,
            Action<Version> onAppUninstall = null,
            Action onFirstRun = null,
            string[] arguments = null)
        {
            Action<Version> defaultBlock = (v => { });
            var args = arguments ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Length == 0) return;

            var lookup = new[] {
                new { Key = "--squirrel-install", Value = onInitialInstall ?? defaultBlock },
                new { Key = "--squirrel-updated", Value = onAppUpdate ?? defaultBlock },
                new { Key = "--squirrel-obsolete", Value = onAppObsoleted ?? defaultBlock },
                new { Key = "--squirrel-uninstall", Value = onAppUninstall ?? defaultBlock },
            }.ToDictionary(k => k.Key, v => v.Value);

            if (args[0] == "--squirrel-firstrun") {
                (onFirstRun ?? (() => {}))();
                return;
            }

            if (args.Length != 2) return;

            if (!lookup.ContainsKey(args[0])) return;
            var version = args[1].ToSemanticVersion().Version;

            try {
                lookup[args[0]](version);
                if (!ModeDetector.InUnitTestRunner()) Environment.Exit(0);
            } catch (Exception ex) {
                LogHost.Default.ErrorException("Failed to handle Squirrel events", ex);
                if (!ModeDetector.InUnitTestRunner()) Environment.Exit(-1);
            }
        }
    }
}