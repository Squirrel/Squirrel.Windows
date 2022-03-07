using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;
using Squirrel.Shell;
using Squirrel.Sources;

namespace Squirrel
{
    /// <inheritdoc cref="IUpdateManager"/>
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public partial class UpdateManager : IUpdateManager
    {
        /// <summary>The unique Id of the application.</summary>
        public string AppId => _applicationIdOverride ?? getInstalledApplicationName();

        /// <summary>The directory the app is (or will be) installed in.</summary>
        public string AppDirectory => Path.Combine(_localAppDataDirectoryOverride ?? GetLocalAppDataDirectory(), AppId);

        /// <summary>True if the current executable is inside the target <see cref="AppDirectory"/>.</summary>
        public bool IsInstalledApp => isUpdateExeAvailable() ? Utility.IsFileInDirectory(AssemblyRuntimeInfo.EntryExePath, AppDirectory) : false;

        /// <summary>The directory packages and temp files are stored in.</summary>
        protected string PackagesDirectory => Utility.PackageDirectoryForAppDir(AppDirectory);

        /// <summary>The application name provided in constructor, or null.</summary>
        protected readonly string _applicationIdOverride;

        /// <summary>The path to the local app data folder on this machine.</summary>
        protected readonly string _localAppDataDirectoryOverride;

        /// <summary>The <see cref="IUpdateSource"/> responsible for retrieving updates from a package repository.</summary>
        protected readonly IUpdateSource _updateSource;

        private readonly object _lockobj = new object();
        private IDisposable _updateLock;
        private bool _disposed;

        /// <summary>
        /// Create a new instance of <see cref="UpdateManager"/> to check for and install updates. 
        /// Do not forget to dispose this class! This constructor is just a shortcut for
        /// <see cref="UpdateManager(IUpdateSource, string, string)"/>, and will automatically create
        /// a <see cref="SimpleFileSource"/> or a <see cref="SimpleWebSource"/> depending on 
        /// whether 'urlOrPath' is a filepath or a URL, respectively.
        /// </summary>
        /// <param name="urlOrPath">
        /// The URL where your update packages or stored, or a local package repository directory.
        /// </param>
        /// <param name="applicationIdOverride">
        /// The Id of your application should correspond with the 
        /// appdata directory name, and the Id used with Squirrel releasify/pack.
        /// If left null/empty, UpdateManger will attempt to determine the current application Id  
        /// from the installed app location, or throw if the app is not currently installed during certain 
        /// operations.
        /// </param>
        /// <param name="localAppDataDirectoryOverride">
        /// Provide a custom location for the system LocalAppData, it will be used 
        /// instead of <see cref="Environment.SpecialFolder.LocalApplicationData"/>.
        /// </param>
        /// <param name="urlDownloader">
        /// A custom file downloader, for using non-standard package sources or adding proxy configurations. 
        /// </param>
        public UpdateManager(
            string urlOrPath,
            string applicationIdOverride = null,
            string localAppDataDirectoryOverride = null,
            IFileDownloader urlDownloader = null)
            : this(CreateSourceFromString(urlOrPath, urlDownloader), applicationIdOverride, localAppDataDirectoryOverride)
        { }

        /// <summary>
        /// Create a new instance of <see cref="UpdateManager"/> to check for and install updates. 
        /// Do not forget to dispose this class!
        /// </summary>
        /// <param name="updateSource">
        /// The source of your update packages. This can be a web server (<see cref="SimpleWebSource"/>),
        /// a local directory (<see cref="SimpleFileSource"/>), a GitHub repository (<see cref="GithubSource"/>),
        /// or a custom location.
        /// </param>
        /// <param name="applicationIdOverride">
        /// The Id of your application should correspond with the 
        /// appdata directory name, and the Id used with Squirrel releasify/pack.
        /// If left null/empty, UpdateManger will attempt to determine the current application Id  
        /// from the installed app location, or throw if the app is not currently installed during certain 
        /// operations.
        /// </param>
        /// <param name="localAppDataDirectoryOverride">
        /// Provide a custom location for the system LocalAppData, it will be used 
        /// instead of <see cref="Environment.SpecialFolder.LocalApplicationData"/>.
        /// </param>
        public UpdateManager(
            IUpdateSource updateSource, 
            string applicationIdOverride = null, 
            string localAppDataDirectoryOverride = null)
        {
            _updateSource = updateSource;
            _applicationIdOverride = applicationIdOverride;
            _localAppDataDirectoryOverride = localAppDataDirectoryOverride;
        }

        internal UpdateManager() { }

        /// <summary>Clean up UpdateManager resources</summary>
        ~UpdateManager()
        {
            Dispose();
        }

        /// <inheritdoc/>
        public async Task<string> ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null)
        {
            var applyReleases = new ApplyReleasesImpl(AppDirectory);
            await acquireUpdateLock().ConfigureAwait(false);

            return await applyReleases.ApplyReleases(updateInfo, false, false, progress).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FullInstall(bool silentInstall = false, Action<int> progress = null)
        {
            var updateInfo = await CheckForUpdate(intention: UpdaterIntention.Install).ConfigureAwait(false);
            await DownloadReleases(updateInfo.ReleasesToApply).ConfigureAwait(false);

            var applyReleases = new ApplyReleasesImpl(AppDirectory);
            await acquireUpdateLock().ConfigureAwait(false);

            await applyReleases.ApplyReleases(updateInfo, silentInstall, true, progress).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FullUninstall()
        {
            var applyReleases = new ApplyReleasesImpl(AppDirectory);
            await acquireUpdateLock().ConfigureAwait(false);

            this.KillAllExecutablesBelongingToPackage();
            await applyReleases.FullUninstall().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch)
        {
            var installHelpers = new InstallHelperImpl(AppId, AppDirectory);
            return installHelpers.CreateUninstallerRegistryEntry(uninstallCmd, quietSwitch);
        }

        /// <inheritdoc/>
        public Task<RegistryKey> CreateUninstallerRegistryEntry()
        {
            var installHelpers = new InstallHelperImpl(AppId, AppDirectory);
            return installHelpers.CreateUninstallerRegistryEntry();
        }

        /// <inheritdoc/>
        public void RemoveUninstallerRegistryEntry()
        {
            var installHelpers = new InstallHelperImpl(AppId, AppDirectory);
            installHelpers.RemoveUninstallerRegistryEntry();
        }

        /// <inheritdoc/>
        public void CreateShortcutsForExecutable(string exeName, ShortcutLocation locations, bool updateOnly, string programArguments = null, string icon = null)
        {
            var installHelpers = new ApplyReleasesImpl(AppDirectory);
            installHelpers.CreateShortcutsForExecutable(exeName, locations, updateOnly, programArguments, icon);
        }

        /// <inheritdoc/>
        public void RemoveShortcutsForExecutable(string exeName, ShortcutLocation locations)
        {
            var installHelpers = new ApplyReleasesImpl(AppDirectory);
            installHelpers.RemoveShortcutsForExecutable(exeName, locations);
        }

        /// <inheritdoc/>
        public SemanticVersion CurrentlyInstalledVersion(string executable = null)
        {
            try {
                executable = executable ?? AssemblyRuntimeInfo.EntryExePath;

                if (!Utility.IsFileInDirectory(executable, AppDirectory))
                    return null;

                var appDirName = executable.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .FirstOrDefault(x => x.StartsWith("app-", StringComparison.OrdinalIgnoreCase));

                if (appDirName == null) return null;
                return new SemanticVersion(appDirName.Substring(4));
            } catch (InvalidOperationException) {
                // app is not installed, see getUpdateExe()
                return null;
            }
        }

        /// <inheritdoc/>
        public void SetProcessAppUserModelId()
        {
            var releases = Utility.LoadLocalReleases(Utility.LocalReleaseFileForAppDir(AppDirectory));
            var thisRelease = Utility.FindCurrentVersion(releases);

            var zf = new ZipPackage(Path.Combine(
                Utility.PackageDirectoryForAppDir(AppDirectory),
                thisRelease.Filename));

            var exeName = Path.GetFileName(AssemblyRuntimeInfo.EntryExePath);
            var appUserModelId = Utility.GetAppUserModelId(zf.Id, exeName);
            NativeMethods.SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        }

        /// <inheritdoc/>
        public void KillAllExecutablesBelongingToPackage()
        {
            var installHelpers = new InstallHelperImpl(AppId, AppDirectory);
            installHelpers.KillAllProcessesBelongingToPackage();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (_lockobj) {
                var disp = Interlocked.Exchange(ref _updateLock, null);
                if (disp != null) {
                    disp.Dispose();
                }
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

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

            exeToStart = exeToStart ?? Path.GetFileName(AssemblyRuntimeInfo.EntryExePath);
            var argsArg = arguments != null ?
                String.Format("-a \"{0}\"", arguments) : "";

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

            var updateProcess = Process.Start(getUpdateExe(), String.Format("--processStartAndWait {0} {1}", exeToStart, argsArg));

            await Task.Delay(500).ConfigureAwait(false);

            return updateProcess;
        }

        internal Dictionary<ShortcutLocation, ShellLink> GetShortcutsForExecutable(string exeName, ShortcutLocation locations, string programArguments = null)
        {
            var installHelpers = new ApplyReleasesImpl(AppDirectory);
            return installHelpers.GetShortcutsForExecutable(exeName, locations, programArguments);
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

        private static IUpdateSource CreateSourceFromString(string urlOrPath, IFileDownloader urlDownloader)
        {
            if (Utility.IsHttpUrl(urlOrPath)) {
                return new SimpleWebSource(urlOrPath, urlDownloader ?? Utility.CreateDefaultDownloader());
            } else {
                return new SimpleFileSource(new DirectoryInfo(urlOrPath));
            }
        }

        private Task<IDisposable> acquireUpdateLock()
        {
            lock (_lockobj) {
                if (_disposed) throw new ObjectDisposedException(nameof(UpdateManager));
                if (_updateLock != null) return Task.FromResult(_updateLock);
            }

            return Task.Run(() => {
                var key = Utility.CalculateStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(AppDirectory)));

                IDisposable theLock;
                try {
                    theLock = ModeDetector.InUnitTestRunner() ?
                        Disposable.Create(() => { }) : new SingleGlobalInstance(key, TimeSpan.FromMilliseconds(2000));
                } catch (TimeoutException) {
                    throw new TimeoutException("Couldn't acquire update lock, another instance may be running updates");
                }

                var ret = Disposable.Create(() => {
                    theLock.Dispose();
                    _updateLock = null;
                });

                _updateLock = ret;
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

        private static string getInstalledApplicationName()
        {
            var fi = new FileInfo(getUpdateExe());
            return fi.Directory.Name;
        }

        private static bool isUpdateExeAvailable()
        {
            try {
                getUpdateExe();
                return true;
            } catch {
                return false;
            }
        }

        private static string getUpdateExe()
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

            if (!target.Exists)
                throw new InvalidOperationException(
                    "This operation is only valid in an installed application (Update.exe was not found).\n" +
                    "Check 'IsInstalledApp' or 'CurrentlyInstalledVersion()' before calling this function.");
            return target.FullName;
        }
    }
}
