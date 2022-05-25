using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;
using Squirrel.Sources;
using NuGet.Versioning;
using System.Runtime.Versioning;

namespace Squirrel
{
    /// <inheritdoc cref="IUpdateManager"/>
    public partial class UpdateManager : IUpdateManager
    {
        /// <inheritdoc/>
        public bool IsInstalledApp => CurrentlyInstalledVersion() != null;

        /// <inheritdoc/>
        public virtual SemanticVersion CurrentlyInstalledVersion() => _config?.CurrentlyInstalledVersion;

        /// <inheritdoc/>
        public virtual string AppDirectory => _config.RootAppDir;

        /// <summary>The <see cref="AppDesc"/> describes the structure of the application on disk (eg. file/folder locations).</summary>
        public AppDesc Config => _config;

        /// <summary>The <see cref="IUpdateSource"/> responsible for retrieving updates from a package repository.</summary>
        public IUpdateSource Source => _source;

        private readonly IUpdateSource _source;
        private readonly AppDesc _config = AppDesc.GetCurrentPlatform();
        private readonly object _lockobj = new object();
        private IDisposable _updateLock;
        private bool _disposed;

        /// <summary>
        /// Create a new instance of <see cref="UpdateManager"/> to check for and install updates. 
        /// Do not forget to dispose this class!
        /// </summary>
        /// <param name="urlOrPath">
        /// The URL or local directory that contains application update files (.nupkg and RELEASES)
        /// </param>
        /// <param name="downloader">
        /// A custom downloader to use when retrieving files from an HTTP source.
        /// </param>
        public UpdateManager(string urlOrPath, IFileDownloader downloader = null) : this(CreateSource(urlOrPath, downloader))
        {
        }

        /// <summary>
        /// Create a new instance of <see cref="UpdateManager"/> to check for and install updates. 
        /// Do not forget to dispose this class!
        /// </summary>
        /// <param name="source">
        /// The source of your update packages. This can be a web server (<see cref="SimpleWebSource"/>),
        /// a local directory (<see cref="SimpleFileSource"/>), a GitHub repository (<see cref="GithubSource"/>),
        /// or a custom location.
        /// </param>
        public UpdateManager(IUpdateSource source)
        {
            _source = source;
        }

        [SupportedOSPlatform("windows")]
        internal UpdateManager(string urlOrPath, string appId, string localAppData = null, IFileDownloader downloader = null)
        {
            _source = CreateSource(urlOrPath, downloader);
            _config = new AppDescWindows(Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appId), appId);
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
            return await ApplyReleases(updateInfo, false, false, progress).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [SupportedOSPlatform("windows")]
        public async Task FullInstall(bool silentInstall = false, Action<int> progress = null)
        {
            var updateInfo = await CheckForUpdate(intention: UpdaterIntention.Install).ConfigureAwait(false);
            await DownloadReleases(updateInfo.ReleasesToApply).ConfigureAwait(false);
            await ApplyReleases(updateInfo, silentInstall, true, progress).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ReleaseEntry> UpdateApp(Action<int> progress = null)
        {
            progress = progress ?? (_ => { });
            this.Log().Info("Starting automatic update");

            bool ignoreDeltaUpdates = false;

            retry:
            var updateInfo = default(UpdateInfo);

            try {
                var localVersions = _config.GetVersions();
                var currentVersion = CurrentlyInstalledVersion();

                // 0 -> 10%
                updateInfo = await this.ErrorIfThrows(() => CheckForUpdate(ignoreDeltaUpdates, x => progress(CalculateProgress(x, 0, 10))),
                    "Failed to check for updates").ConfigureAwait(false);

                if (updateInfo == null || updateInfo.FutureReleaseEntry == null) {
                    this.Log().Info("No update available.");
                    progress(100);
                    return null;
                }

                if (currentVersion >= updateInfo.FutureReleaseEntry.Version) {
                    this.Log().Info($"Current version {currentVersion} is up to date with remote.");
                    progress(100);
                    return null;
                }

                if (localVersions.Any(v => v.Version == updateInfo.FutureReleaseEntry.Version)) {
                    this.Log().Info("Update available, it is already downloaded.");
                    progress(100);
                    return updateInfo.FutureReleaseEntry;
                }

                // 10 -> 50%
                await this.ErrorIfThrows(() =>
                        DownloadReleases(updateInfo.ReleasesToApply, x => progress(CalculateProgress(x, 10, 50))),
                    "Failed to download updates").ConfigureAwait(false);

                // 50 -> 100%
                await this.ErrorIfThrows(() =>
                        ApplyReleases(updateInfo, x => progress(CalculateProgress(x, 50, 100))),
                    "Failed to apply updates").ConfigureAwait(false);

                if (SquirrelRuntimeInfo.IsWindows) {
                    await CreateUninstallerRegistryEntry().ConfigureAwait(false);
                }
            } catch {
                if (ignoreDeltaUpdates == false) {
                    this.Log().Warn("Failed to apply delta updates. Retrying with full package.");
                    ignoreDeltaUpdates = true;
                    goto retry;
                }
                throw;
            }
            
            progress(100);
            return updateInfo.ReleasesToApply.Any() ? updateInfo.ReleasesToApply.MaxBy(x => x.Version).Last() : default;
        }

        /// <inheritdoc/>
        public void KillAllExecutablesBelongingToPackage()
        {
            PlatformUtil.KillProcessesInDirectory(_config.RootAppDir);
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
        /// however you'd like after cleaning up resources.</remarks>
        public static void RestartApp(string exeToStart = null, string arguments = null)
        {
            AppDesc.GetCurrentPlatform().StartRestartingProcess(exeToStart, arguments);
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
        public static Process RestartAppWhenExited(string exeToStart = null, string arguments = null)
        {
            var process = AppDesc.GetCurrentPlatform().StartRestartingProcess(exeToStart, arguments);
            return process;
        }

        private static IUpdateSource CreateSource(string urlOrPath, IFileDownloader urlDownloader = null)
        {
            if (String.IsNullOrWhiteSpace(urlOrPath)) {
                return null;
            }

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
                var key = Utility.CalculateStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(_config.RootAppDir)));

                IDisposable theLock;
                try {
                    theLock = ModeDetector.InUnitTestRunner() ? Disposable.Create(() => { }) : new SingleGlobalInstance(key, TimeSpan.FromMilliseconds(2000));
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
    }
}