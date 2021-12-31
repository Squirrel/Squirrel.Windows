using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;
using Squirrel.Shell;

namespace Squirrel
{
    /// <inheritdoc cref="IUpdateManager"/>
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public sealed partial class UpdateManager : IUpdateManager, IEnableLogger
    {
        readonly string rootAppDirectory;
        readonly string applicationName;
        readonly IFileDownloader urlDownloader;
        readonly string updateUrlOrPath;

        IDisposable updateLock;

        public UpdateManager(string urlOrPath,
            string applicationName = null,
            string rootDirectory = null,
            IFileDownloader urlDownloader = null)
        {
            Contract.Requires(!String.IsNullOrEmpty(urlOrPath));
            Contract.Requires(!String.IsNullOrEmpty(applicationName));

            updateUrlOrPath = urlOrPath;
            this.applicationName = applicationName ?? UpdateManager.getApplicationName();
            this.urlDownloader = urlDownloader ?? new FileDownloader();

            if (rootDirectory != null) {
                this.rootAppDirectory = Path.Combine(rootDirectory, this.applicationName);
                return;
            }

            this.rootAppDirectory = Path.Combine(rootDirectory ?? GetLocalAppDataDirectory(), this.applicationName);
        }

        /// <inheritdoc/>
        public async Task<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates = false, Action<int> progress = null, UpdaterIntention intention = UpdaterIntention.Update)
        {
            var checkForUpdate = new CheckForUpdateImpl(rootAppDirectory);

            await acquireUpdateLock().ConfigureAwait(false);
            return await checkForUpdate.CheckForUpdate(intention, Utility.LocalReleaseFileForAppDir(rootAppDirectory), updateUrlOrPath, ignoreDeltaUpdates, progress, urlDownloader).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null)
        {
            var downloadReleases = new DownloadReleasesImpl(rootAppDirectory);
            await acquireUpdateLock().ConfigureAwait(false);

            await downloadReleases.DownloadReleases(updateUrlOrPath, releasesToDownload, progress, urlDownloader).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<string> ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null)
        {
            var applyReleases = new ApplyReleasesImpl(rootAppDirectory);
            await acquireUpdateLock().ConfigureAwait(false);

            return await applyReleases.ApplyReleases(updateInfo, false, false, progress).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FullInstall(bool silentInstall = false, Action<int> progress = null)
        {
            var updateInfo = await CheckForUpdate(intention: UpdaterIntention.Install).ConfigureAwait(false);
            await DownloadReleases(updateInfo.ReleasesToApply).ConfigureAwait(false);

            var applyReleases = new ApplyReleasesImpl(rootAppDirectory);
            await acquireUpdateLock().ConfigureAwait(false);

            await applyReleases.ApplyReleases(updateInfo, silentInstall, true, progress).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FullUninstall()
        {
            var applyReleases = new ApplyReleasesImpl(rootAppDirectory);
            await acquireUpdateLock().ConfigureAwait(false);

            this.KillAllExecutablesBelongingToPackage();
            await applyReleases.FullUninstall().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch)
        {
            var installHelpers = new InstallHelperImpl(applicationName, rootAppDirectory);
            return installHelpers.CreateUninstallerRegistryEntry(uninstallCmd, quietSwitch);
        }

        /// <inheritdoc/>
        public Task<RegistryKey> CreateUninstallerRegistryEntry()
        {
            var installHelpers = new InstallHelperImpl(applicationName, rootAppDirectory);
            return installHelpers.CreateUninstallerRegistryEntry();
        }

        /// <inheritdoc/>
        public void RemoveUninstallerRegistryEntry()
        {
            var installHelpers = new InstallHelperImpl(applicationName, rootAppDirectory);
            installHelpers.RemoveUninstallerRegistryEntry();
        }

        /// <inheritdoc/>
        public void CreateShortcutsForExecutable(string exeName, ShortcutLocation locations, bool updateOnly, string programArguments = null, string icon = null)
        {
            var installHelpers = new ApplyReleasesImpl(rootAppDirectory);
            installHelpers.CreateShortcutsForExecutable(exeName, locations, updateOnly, programArguments, icon);
        }

        public Dictionary<ShortcutLocation, ShellLink> GetShortcutsForExecutable(string exeName, ShortcutLocation locations, string programArguments = null)
        {
            var installHelpers = new ApplyReleasesImpl(rootAppDirectory);
            return installHelpers.GetShortcutsForExecutable(exeName, locations, programArguments);
        }

        /// <inheritdoc/>
        public void RemoveShortcutsForExecutable(string exeName, ShortcutLocation locations)
        {
            var installHelpers = new ApplyReleasesImpl(rootAppDirectory);
            installHelpers.RemoveShortcutsForExecutable(exeName, locations);
        }

        /// <inheritdoc/>
        public SemanticVersion CurrentlyInstalledVersion(string executable = null)
        {
            executable = executable ?? AssemblyRuntimeInfo.EntryExePath;

            if (!executable.StartsWith(rootAppDirectory, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            var appDirName = executable.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .FirstOrDefault(x => x.StartsWith("app-", StringComparison.OrdinalIgnoreCase));

            if (appDirName == null) return null;
            return appDirName.ToSemanticVersion();
        }

        public void SetProcessAppUserModelId()
        {
            var releases = Utility.LoadLocalReleases(Utility.LocalReleaseFileForAppDir(rootAppDirectory));
            var thisRelease = Utility.FindCurrentVersion(releases);

            var zf = new ZipPackage(Path.Combine(
                Utility.PackageDirectoryForAppDir(rootAppDirectory),
                thisRelease.Filename));

            var exeName = Path.GetFileName(AssemblyRuntimeInfo.EntryExePath);

            var appUserModelId = String.Format("com.squirrel.{0}.{1}", zf.Id.Replace(" ", ""), exeName.Replace(".exe", "").Replace(" ", ""));
            NativeMethods.SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        }

        public void KillAllExecutablesBelongingToPackage()
        {
            var installHelpers = new InstallHelperImpl(applicationName, rootAppDirectory);
            installHelpers.KillAllProcessesBelongingToPackage();
        }

        public string ApplicationName {
            get { return applicationName; }
        }

        public string RootAppDirectory {
            get { return rootAppDirectory; }
        }

        public bool IsInstalledApp {
            get { return Process.GetCurrentProcess().MainModule.FileName.StartsWith(RootAppDirectory, StringComparison.OrdinalIgnoreCase); }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var disp = Interlocked.Exchange(ref updateLock, null);
            if (disp != null) {
                disp.Dispose();
            }
        }

        static bool exiting = false;

        /// <summary>
        /// Terminates the current process immediately (with <see cref="Environment.Exit"/>) and 
        /// re-launches the latest version of the current (or target) executable. 
        /// </summary>
        /// <param name="exeToStart">The file *name* (not full path) of the exe to start, or null to re-launch 
        /// the current executable. </param>
        /// <param name="arguments">Arguments to start the exe with</param>
        /// <remarks>See <see cref="RestartAppWhenExited(string, string)"/> for a version which does not
        /// exit the current process immediately, but instead allows you to exit the current process
        /// however you'd like.</remarks>
        public static void RestartApp(string exeToStart = null, string arguments = null)
        {
            // NB: Here's how this method works:
            //
            // 1. We're going to pass the *name* of our EXE and the params to 
            //    Update.exe
            // 2. Update.exe is going to grab our PID (via getting its parent), 
            //    then wait for us to exit.
            // 3. We exit cleanly, dropping any single-instance mutexes or 
            //    whatever.
            // 4. Update.exe unblocks, then we launch the app again, possibly 
            //    launching a different version than we started with (this is why
            //    we take the app's *name* rather than a full path)

            exeToStart = exeToStart ?? Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
            var argsArg = arguments != null ?
                String.Format("-a \"{0}\"", arguments) : "";

            exiting = true;

            Process.Start(getUpdateExe(), String.Format("--processStartAndWait \"{0}\" {1}", exeToStart, argsArg));

            // NB: We have to give update.exe some time to grab our PID, but
            // we can't use WaitForInputIdle because we probably don't have
            // whatever WaitForInputIdle considers a message loop.
            Thread.Sleep(500);
            Environment.Exit(0);
        }

        /// <summary>
        /// Launch Update.exe and ask it to wait until this process exits before starting
        /// a new process. Used to re-start your app with the latest version after an update.
        /// </summary>
        /// <param name="exeToStart">The file *name* (not full path) of the exe to start, or null to re-launch 
        /// the current executable. </param>
        /// <param name="arguments">Arguments to start the exe with</param>
        /// <returns>The Update.exe process that is waiting for this process to exit</returns>
        public static async Task<Process> RestartAppWhenExited(string exeToStart = null, string arguments = null)
        {
            // NB: Here's how this method works:
            //
            // 1. We're going to pass the *name* of our EXE and the params to 
            //    Update.exe
            // 2. Update.exe is going to grab our PID (via getting its parent), 
            //    then wait for us to exit.
            // 3. Return control and new Process back to caller and allow them to Exit as desired.
            // 4. After our process exits, Update.exe unblocks, then we launch the app again, possibly 
            //    launching a different version than we started with (this is why
            //    we take the app's *name* rather than a full path)

            exeToStart = exeToStart ?? Path.GetFileName(AssemblyRuntimeInfo.EntryExePath);
            var argsArg = arguments != null ?
                String.Format("-a \"{0}\"", arguments) : "";

            exiting = true;

            var updateProcess = Process.Start(getUpdateExe(), String.Format("--processStartAndWait {0} {1}", exeToStart, argsArg));

            await Task.Delay(500).ConfigureAwait(false);

            return updateProcess;
        }

        private static string GetLocalAppDataDirectory(string assemblyLocation = null)
        {
            // if we're installed and running as update.exe in the app folder, the app directory root is one folder up
            if (AssemblyRuntimeInfo.IsSingleFile && Path.GetFileName(AssemblyRuntimeInfo.EntryExePath).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) {
                var oneFolderUpFromAppFolder = Path.Combine(Path.GetDirectoryName(AssemblyRuntimeInfo.EntryExePath), "..");
                return Path.GetFullPath(oneFolderUpFromAppFolder);
            }

            // if update exists above us, we're running from within a version directory, and the appdata folder is two above us
            if (File.Exists(Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "..", "Update.exe"))) {
                var twoFoldersUpFromAppFolder = Path.Combine(Path.GetDirectoryName(AssemblyRuntimeInfo.EntryExePath), "..\\..");
                return Path.GetFullPath(twoFoldersUpFromAppFolder);
            }

            // if neither of the above are true, we're probably not installed yet, so return the real appdata directory
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        ~UpdateManager()
        {
            if (updateLock != null && !exiting) {
                throw new Exception("You must dispose UpdateManager!");
            }
        }

        Task<IDisposable> acquireUpdateLock()
        {
            if (updateLock != null) return Task.FromResult(updateLock);

            return Task.Run(() => {
                var key = Utility.CalculateStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(rootAppDirectory)));

                IDisposable theLock;
                try {
                    theLock = ModeDetector.InUnitTestRunner() ?
                        Disposable.Create(() => { }) : new SingleGlobalInstance(key, TimeSpan.FromMilliseconds(2000));
                } catch (TimeoutException) {
                    throw new TimeoutException("Couldn't acquire update lock, another instance may be running updates");
                }

                var ret = Disposable.Create(() => {
                    theLock.Dispose();
                    updateLock = null;
                });

                updateLock = ret;
                return ret;
            });
        }

        /// <summary>
        /// Calculates the total percentage of a specific step that should report within a specific range.
        /// <para />
        /// If a step needs to report between 50 -> 75 %, this method should be used as CalculateProgress(percentage, 50, 75). 
        /// </summary>
        /// <param name="percentageOfCurrentStep">The percentage of the current step, a value between 0 and 100.</param>
        /// <param name="stepStartPercentage">The start percentage of the range the current step represents.</param>
        /// <param name="stepEndPercentage">The end percentage of the range the current step represents.</param>
        /// <returns>The calculated percentage that can be reported about the total progress.</returns>
        internal static int CalculateProgress(int percentageOfCurrentStep, int stepStartPercentage, int stepEndPercentage)
        {
            // Ensure we are between 0 and 100
            percentageOfCurrentStep = Math.Max(Math.Min(percentageOfCurrentStep, 100), 0);

            var range = stepEndPercentage - stepStartPercentage;
            var singleValue = range / 100d;
            var totalPercentage = (singleValue * percentageOfCurrentStep) + stepStartPercentage;

            return (int) totalPercentage;
        }

        static string getApplicationName()
        {
            var fi = new FileInfo(getUpdateExe());
            return fi.Directory.Name;
        }

        static string getUpdateExe()
        {
            var ourPath = AssemblyRuntimeInfo.EntryExePath;

            // Are we update.exe?
            if (ourPath != null &&
                Path.GetFileName(ourPath).Equals("update.exe", StringComparison.OrdinalIgnoreCase) &&
                ourPath.IndexOf("app-", StringComparison.OrdinalIgnoreCase) == -1 &&
                ourPath.IndexOf("SquirrelTemp", StringComparison.OrdinalIgnoreCase) == -1) {
                return Path.GetFullPath(ourPath);
            }

            var updateDotExe = Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "..\\Update.exe");
            var target = new FileInfo(updateDotExe);

            if (!target.Exists) throw new Exception("Update.exe not found, not a Squirrel-installed app?");
            return target.FullName;
        }
    }
}
