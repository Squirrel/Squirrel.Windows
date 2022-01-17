using System;
using System.Linq;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    /// <summary>
    /// A delegate type for handling Squirrel command line events easily
    /// </summary>
    /// <param name="version">The currently executing version of this application</param>
    /// <param name="tools">Helper functions for managing application shortcuts and registry</param>
    public delegate void SquirrelHook(SemanticVersion version, IAppTools tools);

    /// <summary>
    /// A delegate type for handling Squirrel command line events easily
    /// </summary>
    /// <param name="version">The currently executing version of this application</param>
    /// <param name="tools">Helper functions for managing application shortcuts and registry</param>
    /// <param name="firstRun">True if this is the first run following application installation</param>
    public delegate void SquirrelRunHook(SemanticVersion version, IAppTools tools, bool firstRun);

    /// <summary>
    /// SquirrelAwareApp helps you to handle Squirrel app activation events
    /// correctly.
    /// </summary>
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public static class SquirrelAwareApp
    {
        /// <summary>
        /// This overload is obsolete and will be removed in a future version. 
        /// See <see cref="HandleEvents(SquirrelHook, SquirrelHook, SquirrelHook, SquirrelHook, SquirrelRunHook, string[])" />
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Use the HandleEvents overload which provides a SemanticVersion as the argument")]
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
                (onFirstRun ?? (() => { }))();
                return;
            }

            if (args.Length != 2) return;

            if (!lookup.ContainsKey(args[0])) return;
            var version = new SemanticVersion(args[1]).Version;

            try {
                lookup[args[0]](version);
                if (!ModeDetector.InUnitTestRunner()) Environment.Exit(0);
            } catch (Exception ex) {
                LogHost.Default.ErrorException("Failed to handle Squirrel events", ex);
                if (!ModeDetector.InUnitTestRunner()) Environment.Exit(-1);
            }
        }

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
        /// <param name="onEveryRun">Called when your application is run normally,
        /// also indicates whether this is first time your app is run, so you can
        /// show a welcome screen. Also see <see cref="IAppTools.SetProcessAppUserModelId"/>
        /// which can be executed here.</param>
        /// <param name="arguments">Use in a unit-test runner to mock the 
        /// arguments. In your app, leave this as null.</param>
        public static void HandleEvents(
            SquirrelHook onInitialInstall = null,
            SquirrelHook onAppUpdate = null,
            SquirrelHook onAppObsoleted = null,
            SquirrelHook onAppUninstall = null,
            SquirrelRunHook onEveryRun = null,
            string[] arguments = null)
        {
            SquirrelHook defaultBlock = ((v, t) => { });
            var args = arguments ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Length == 0) return;

            var fastExitlookup = new[] {
                new { Key = "--squirrel-install", Value = onInitialInstall ?? defaultBlock },
                new { Key = "--squirrel-updated", Value = onAppUpdate ?? defaultBlock },
                new { Key = "--squirrel-obsolete", Value = onAppObsoleted ?? defaultBlock },
                new { Key = "--squirrel-uninstall", Value = onAppUninstall ?? defaultBlock },
            }.ToDictionary(k => k.Key, v => v.Value);

            // CS: call dispose immediately means this instance of UpdateManager
            // can never acquire an update lock (it will throw if tried).
            // this is fine because we are downcasting to IAppTools and no 
            // functions which acquire a lock are exposed to the consumer.
            // also this means the "urlOrPath" param will never be used, 
            // so we can pass null safely.
            var um = new UpdateManager(null);
            um.Dispose();

            // in the fastExitLookup arguments, we run the squirrel hook and then exit the process
            if (fastExitlookup.ContainsKey(args[0]) && args.Length >= 2) {
                var version = new SemanticVersion(args[1]);
                try {
                    fastExitlookup[args[0]](version, um);
                    if (!ModeDetector.InUnitTestRunner()) Environment.Exit(0);
                } catch (Exception ex) {
                    LogHost.Default.ErrorException("Failed to handle Squirrel events", ex);
                    if (!ModeDetector.InUnitTestRunner()) Environment.Exit(-1);
                }
            }

            // otherwise we execute the 'everyrun' hook with the firstRun parameter.
            bool firstRun = args[0] == "--squirrel-firstrun";
            onEveryRun?.Invoke(um.CurrentlyInstalledVersion(), um, firstRun);
        }
    }
}