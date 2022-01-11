using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    /// <summary>
    /// Specifies several common places where shortcuts can be installed on a user's system
    /// </summary>
    [Flags]
    public enum ShortcutLocation
    {
        /// <summary>
        /// A shortcut in ProgramFiles within a publisher sub-directory
        /// </summary>
        StartMenu = 1 << 0,

        /// <summary>
        /// A shortcut on the current user desktop
        /// </summary>
        Desktop = 1 << 1,

        /// <summary>
        /// A shortcut in Startup/Run folder will cause the app to be automatially started on user login.
        /// </summary>
        Startup = 1 << 2,

        /// <summary>
        /// A shortcut in the application folder, useful for portable applications.
        /// </summary>
        AppRoot = 1 << 3,

        /// <summary>
        /// A shortcut in ProgramFiles root folder (not in a company/publisher sub-directory). This is commonplace as of more recent versions of windows.
        /// </summary>
        StartMenuRoot = 1 << 4,
    }

    /// <summary>
    /// Indicates whether the UpdateManager is used in a Install or Update scenario.
    /// </summary>
    public enum UpdaterIntention
    {
        /// <summary> 
        /// The current intent is to perform a full app install, and overwrite or 
        /// repair any app already installed of the same name.
        /// </summary>
        Install,

        /// <summary>
        /// The current intent is to perform an app update, and to do nothing if there
        /// is no newer version available to install.
        /// </summary>
        Update
    }

    /// <summary>
    /// Provides update functionality to applications, and general helper
    /// functions for managing installed shortcuts and registry entries. Use this
    /// to check if the current app is installed or not before performing an update.
    /// </summary>
    public interface IUpdateManager : IDisposable, IEnableLogger, IAppTools
    {
        /// <summary>
        /// Fetch the remote store for updates and compare against the current 
        /// version to determine what updates to download.
        /// </summary>
        /// <param name="intention">Indicates whether the UpdateManager is used
        /// in a Install or Update scenario.</param>
        /// <param name="ignoreDeltaUpdates">Set this flag if applying a release
        /// fails to fall back to a full release, which takes longer to download
        /// but is less error-prone.</param>
        /// <param name="progress">A Observer which can be used to report Progress - 
        /// will return values from 0-100 and Complete, or Throw</param>
        /// <returns>An UpdateInfo object representing the updates to install.
        /// </returns>
        Task<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates = false, Action<int> progress = null, UpdaterIntention intention = UpdaterIntention.Update);

        /// <summary>
        /// Download a list of releases into the local package directory.
        /// </summary>
        /// <param name="releasesToDownload">The list of releases to download, 
        /// almost always from UpdateInfo.ReleasesToApply.</param>
        /// <param name="progress">A Observer which can be used to report Progress - 
        /// will return values from 0-100 and Complete, or Throw</param>
        /// <returns>A completion Observable - either returns a single 
        /// Unit.Default then Complete, or Throw</returns>
        Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null);

        /// <summary>
        /// Take an already downloaded set of releases and apply them, 
        /// copying in the new files from the NuGet package and rewriting 
        /// the application shortcuts.
        /// </summary>
        /// <param name="updateInfo">The UpdateInfo instance acquired from 
        /// CheckForUpdate</param>
        /// <param name="progress">A Observer which can be used to report Progress - 
        /// will return values from 0-100 and Complete, or Throw</param>
        /// <returns>The path to the installed application (i.e. the path where
        /// your package's contents ended up</returns>
        Task<string> ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null);

        /// <summary>
        /// Completely Installs a targeted app
        /// </summary>
        /// <param name="silentInstall">If true, don't run the app once install completes.</param>
        /// <param name="progress">A Observer which can be used to report Progress - 
        /// will return values from 0-100 and Complete, or Throw</param>
        Task FullInstall(bool silentInstall, Action<int> progress = null);

        /// <summary>
        /// Completely uninstalls the targeted app
        /// </summary>
        Task FullUninstall();

        /// <summary>
        /// Kills all the executables in the target install directory, excluding
        /// the currently executing process.
        /// </summary>
        void KillAllExecutablesBelongingToPackage();
    }

    /// <summary>
    /// Provides accessory functions such as managing uninstall registry or 
    /// creating, updating, and removing shortcuts.
    /// </summary>
    public interface IAppTools
    {
        /// <summary>
        /// Gets the currently installed version of the given executable, or if
        /// not given, the currently running assembly
        /// </summary>
        /// <param name="executable">The executable to check, or null for this 
        /// executable</param>
        /// <returns>The running version, or null if this is not a Squirrel
        /// installed app (i.e. you're running from VS)</returns>
        SemanticVersion CurrentlyInstalledVersion(string executable = null);

        /// <summary>
        /// Creates an entry in Programs and Features based on the currently 
        /// applied package
        /// </summary>
        /// <param name="uninstallCmd">The command to run to uninstall, usually update.exe --uninstall</param>
        /// <param name="quietSwitch">The switch for silent uninstall, usually --silent</param>
        /// <returns>The registry key that was created</returns>
        Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch);

        /// <summary>
        /// Creates an entry in Programs and Features based on the currently 
        /// applied package. Uses the built-in Update.exe to handle uninstall.
        /// </summary>
        /// <returns>The registry key that was created</returns>
        Task<RegistryKey> CreateUninstallerRegistryEntry();

        /// <summary>
        /// Removes the entry in Programs and Features created via 
        /// CreateUninstallerRegistryEntry
        /// </summary>
        void RemoveUninstallerRegistryEntry();

        /// <summary>
        /// Create a shortcut on the Desktop / Start Menu for the given 
        /// executable. Metadata from the currently installed NuGet package 
        /// and information from the Version Header of the EXE will be used
        /// to construct the shortcut folder / name.
        /// </summary>
        /// <param name="exeName">The name of the executable, relative to the 
        /// app install directory.</param>
        /// <param name="locations">The locations to install the shortcut</param>
        /// <param name="updateOnly">Set to false during initial install, true 
        /// during app update.</param>
        /// <param name="programArguments">The arguments to code into the shortcut</param>
        /// <param name="icon">The shortcut icon</param>
        void CreateShortcutsForExecutable(string exeName, ShortcutLocation locations, bool updateOnly, string programArguments, string icon);

        /// <summary>
        /// Removes shortcuts created by CreateShortcutsForExecutable
        /// </summary>
        /// <param name="exeName">The name of the executable, relative to the
        /// app install directory.</param>
        /// <param name="locations">The locations to install the shortcut</param>
        void RemoveShortcutsForExecutable(string exeName, ShortcutLocation locations);

        /// <summary>
        /// Sets the AppUserModelID of the current process to match that which was added to the 
        /// shell shortcuts. This ID is used to group an application's processes and windows under 
        /// a single taskbar button.
        /// </summary>
        void SetProcessAppUserModelId();
    }

    /// <summary>
    /// Contains extension methods for <see cref="IUpdateManager"/> which provide simplified functionality
    /// </summary>
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public static class EasyModeMixin
    {
        /// <summary>
        /// This will check for updates, download any new available updates, and apply those
        /// updates in a single step. The same task can be accomplished by using <see cref="IUpdateManager.CheckForUpdate"/>, 
        /// followed by <see cref="IUpdateManager.DownloadReleases"/> and <see cref="IUpdateManager.ApplyReleases"/>.
        /// </summary>
        /// <returns>The installed update, or null if there were no updates available</returns>
        public static async Task<ReleaseEntry> UpdateApp(this IUpdateManager This, Action<int> progress = null)
        {
            progress = progress ?? (_ => { });
            This.Log().Info("Starting automatic update");

            bool ignoreDeltaUpdates = false;

        retry:
            var updateInfo = default(UpdateInfo);

            try {
                updateInfo = await This.ErrorIfThrows(() => This.CheckForUpdate(ignoreDeltaUpdates, x => progress(x / 3)),
                    "Failed to check for updates").ConfigureAwait(false);

                await This.ErrorIfThrows(() =>
                    This.DownloadReleases(updateInfo.ReleasesToApply, x => progress(x / 3 + 33)),
                    "Failed to download updates").ConfigureAwait(false);

                await This.ErrorIfThrows(() =>
                    This.ApplyReleases(updateInfo, x => progress(x / 3 + 66)),
                    "Failed to apply updates").ConfigureAwait(false);

                await This.ErrorIfThrows(() =>
                    This.CreateUninstallerRegistryEntry(),
                    "Failed to set up uninstaller").ConfigureAwait(false);
            } catch {
                if (ignoreDeltaUpdates == false) {
                    ignoreDeltaUpdates = true;
                    goto retry;
                }

                throw;
            }

            return updateInfo.ReleasesToApply.Any() ?
                updateInfo.ReleasesToApply.MaxBy(x => x.Version).Last() :
                default(ReleaseEntry);
        }

        /// <summary>
        /// Create a shortcut to the currently running executable at the specified locations. 
        /// See <see cref="IAppTools.CreateShortcutsForExecutable"/> to create a shortcut to a different program
        /// </summary>
        public static void CreateShortcutForThisExe(this IAppTools This, ShortcutLocation location = ShortcutLocation.Desktop | ShortcutLocation.StartMenu)
        {
            This.CreateShortcutsForExecutable(
                Path.GetFileName(AssemblyRuntimeInfo.EntryExePath),
                location,
                Environment.CommandLine.Contains("squirrel-install") == false,
                null,  // shortcut arguments 
                null); // shortcut icon
        }

        /// <summary>
        /// Removes a shortcut for the currently running executable at the specified locations.
        /// </summary>
        public static void RemoveShortcutForThisExe(this IAppTools This, ShortcutLocation location = ShortcutLocation.Desktop | ShortcutLocation.StartMenu)
        {
            This.RemoveShortcutsForExecutable(
                Path.GetFileName(AssemblyRuntimeInfo.EntryExePath),
                location);
        }
    }
}
