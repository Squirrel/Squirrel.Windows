using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Splat;
using NuGet;

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
            var args = arguments ?? Environment.GetCommandLineArgs().Skip(1).ToArray();

            var lookup = BuildLookup(onInitialInstall, onAppUpdate, onAppObsoleted, onAppUninstall);

            Func<string, Version> selector = new Func<string, Version>((arg) =>
            {
                return new Version(arg);
            });

            HandleEventsImpl(onFirstRun, lookup, selector, args);            
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
        /// <param name="onFirstRun">Called the first time an app is run after
        /// being installed. Your application will **not** exit after this is
        /// dispatched, you should use this as a hint (i.e. show a 'Welcome' 
        /// screen, etc etc.</param>
        /// <param name="arguments">Use in a unit-test runner to mock the 
        /// arguments. In your app, leave this as null.</param>
        public static void HandleEvents(
            Action<SemanticVersion> onInitialInstall = null,
            Action<SemanticVersion> onAppUpdate = null,
            Action<SemanticVersion> onAppObsoleted = null,
            Action<SemanticVersion> onAppUninstall = null,
            Action onFirstRun = null,
            string[] arguments = null)
        {
            var args = arguments ?? Environment.GetCommandLineArgs().Skip(1).ToArray();

            var lookup = BuildLookup(onInitialInstall, onAppUpdate, onAppObsoleted, onAppUninstall);

            Func<string, SemanticVersion> selector = new Func<string, SemanticVersion>((arg) =>
            {
                return new SemanticVersion(arg);
            });

            HandleEventsImpl(onFirstRun, lookup, selector, arguments);
        }

        /// <summary>
        /// Builds a lookup dicitonary of command line arguments to actions
        /// to invoke.
        /// </summary>
        /// <typeparam name="T">While this should be a class, this is 
        /// intended to be used with Version or SemanticVersion, since
        /// they do not inherit from each other, this is the most
        /// "constrained" we can work with
        /// </typeparam>
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
        /// <returns>The Action Lookup dictionary</returns>
        internal static Dictionary<string, Action<T>> BuildLookup<T>(
            Action<T> onInitialInstall = null,
            Action<T> onAppUpdate = null,
            Action<T> onAppObsoleted = null,
            Action<T> onAppUninstall = null) where T : class
        {
            Action<T> defaultBlock = (v => { });
            var lookup = new[] {
                new { Key = "--squirrel-install", Value = onInitialInstall ?? defaultBlock },
                new { Key = "--squirrel-updated", Value = onAppUpdate ?? defaultBlock },
                new { Key = "--squirrel-obsolete", Value = onAppObsoleted ?? defaultBlock },
                new { Key = "--squirrel-uninstall", Value = onAppUninstall ?? defaultBlock },
            }.ToDictionary(k => k.Key, v => v.Value);

            return lookup;
        }

        /// <summary>
        /// Takes the passed arguments, looks up the specified action
        /// from the arguments, gets a <typeparamref name="T"/>
        /// via <paramref name="selector"/> and then passes it to
        /// the Action<T> found in the <paramref name="lookup"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="onFirstRun">Called the first time an app is run after
        /// being installed. Your application will **not** exit after this is
        /// dispatched, you should use this as a hint (i.e. show a 'Welcome' 
        /// screen, etc etc.</param>
        /// <param name="args">The arguments passed (either via command line
        /// or via a unit test runner)
        /// </param>
        /// <param name="lookup">
        /// Uses the passed arguments to lookup the action to perform via
        /// this dictionary
        /// </param>
        /// <param name="selector">Used to create a new <typeparamref name="T"/>
        /// based on the passed arguments (via command line
        /// or via the <paramref name="args"/> function)
        /// </param>        
        internal static void HandleEventsImpl<T>(Action onFirstRun,
                                                 Dictionary<string, Action<T>> lookup,
                                                 Func<string, T> selector,
                                                 string[] args) where T : class
        {            
            if (args.Length == 0) return;

            if (args[0] == "--squirrel-firstrun")
            {
                (onFirstRun ?? (() => { }))();
                return;
            }

            if (args.Length != 2) return;

            if (!lookup.ContainsKey(args[0])) return;
            T version = selector(args[1]);

            try
            {
                lookup[args[0]](version);
                if (!ModeDetector.InUnitTestRunner()) Environment.Exit(0);
            }
            catch (Exception ex)
            {
                LogHost.Default.ErrorException("Failed to handle Squirrel events", ex);
                if (!ModeDetector.InUnitTestRunner()) Environment.Exit(-1);
            }
        }
    }
}