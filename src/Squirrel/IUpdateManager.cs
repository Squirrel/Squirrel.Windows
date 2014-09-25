using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Splat;

namespace Squirrel
{
    [Flags]
    public enum ShortcutLocation {
        StartMenu = 1<<0,
        Desktop = 1<<1,
    }

    public interface IUpdateManager : IDisposable, IEnableLogger
    {
        /// <summary>
        /// Fetch the remote store for updates and compare against the current 
        /// version to determine what updates to download.
        /// </summary>
        /// <param name="ignoreDeltaUpdates">Set this flag if applying a release
        /// fails to fall back to a full release, which takes longer to download
        /// but is less error-prone.</param>
        /// <param name="progress">A Observer which can be used to report Progress - 
        /// will return values from 0-100 and Complete, or Throw</param>
        /// <returns>An UpdateInfo object representing the updates to install.
        /// </returns>
        Task<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates = false, Action<int> progress = null);

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
        /// <returns>Completion</returns>
        Task FullInstall(bool silentInstall);

        /// <summary>
        /// Completely uninstalls the targeted app
        /// </summary>
        /// <returns>Completion</returns>
        Task FullUninstall();

        /// <summary>
        /// Gets the currently installed version of the given executable, or if
        /// not given, the currently running assembly
        /// </summary>
        /// <param name="executable">The executable to check, or null for this 
        /// executable</param>
        /// <returns>The running version, or null if this is not a Squirrel
        /// installed app (i.e. you're running from VS)</returns>
        Version CurrentlyInstalledVersion(string executable = null);

        /// <summary>
        /// Creates an entry in Programs and Features based on the currently 
        /// applied package
        /// </summary>
        /// <param name="uninstallCmd">The command to run to uninstall, usually update.exe --uninstall</param>
        /// <param name="quietSwitch">The switch for silent uninstall, usually --silent</param>
        /// <returns>The registry key that was created</returns>
        Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch);

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
        void CreateShortcutsForExecutable(string exeName, ShortcutLocation locations);

        /// <summary>
        /// Removes shortcuts created by CreateShortcutsForExecutable
        /// </summary>
        /// <param name="exeName">The name of the executable, relative to the
        /// app install directory.</param>
        /// <param name="locations">The locations to install the shortcut</param>
        void RemoveShortcutsForExecutable(string exeName, ShortcutLocation locations);
    }

    public static class EasyModeMixin
    {
        public static async Task<ReleaseEntry> UpdateApp(this IUpdateManager This, Action<int> progress = null)
        {
            progress = progress ?? (_ => {});

            This.Log().Info("Starting automatic update");

            var updateInfo = await This.ErrorIfThrows(() => This.CheckForUpdate(false, x => progress(x / 3)),
                "Failed to check for updates");

            await This.ErrorIfThrows(() =>
                This.DownloadReleases(updateInfo.ReleasesToApply, x => progress(x / 3 + 33)),
                "Failed to download updates");

            await This.ErrorIfThrows(() =>
                This.ApplyReleases(updateInfo, x => progress(x / 3 + 66)),
                "Failed to apply updates");

            return updateInfo.ReleasesToApply.MaxBy(x => x.Version).LastOrDefault();
        }

        public static void CreateShortcutForThisExe(this IUpdateManager This)
        {
            This.CreateShortcutsForExecutable(Path.GetFileName(Assembly.GetEntryAssembly().Location),
                ShortcutLocation.Desktop | ShortcutLocation.StartMenu);
        }
    }
}
