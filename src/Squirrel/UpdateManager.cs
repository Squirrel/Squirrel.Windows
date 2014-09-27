using System;
using System.Collections.Generic;
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
using NuGet;
using Splat;

namespace Squirrel
{
    public sealed partial class UpdateManager : IUpdateManager, IEnableLogger
    {
        readonly string rootAppDirectory;
        readonly string applicationName;
        readonly IFileDownloader urlDownloader;
        readonly string updateUrlOrPath;
        readonly FrameworkVersion appFrameworkVersion;

        IDisposable updateLock;

        public UpdateManager(string urlOrPath, 
            string applicationName,
            FrameworkVersion appFrameworkVersion,
            string rootDirectory = null,
            IFileDownloader urlDownloader = null)
        {
            Contract.Requires(!String.IsNullOrEmpty(urlOrPath));
            Contract.Requires(!String.IsNullOrEmpty(applicationName));

            updateUrlOrPath = urlOrPath;
            this.applicationName = applicationName;
            this.appFrameworkVersion = appFrameworkVersion;

            this.rootAppDirectory = Path.Combine(rootDirectory ?? getLocalAppDataDirectory(), applicationName);

            this.urlDownloader = urlDownloader ?? new FileDownloader();
        }

        public async Task<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates = false, Action<int> progress = null)
        {
            var checkForUpdate = new CheckForUpdateImpl(rootAppDirectory);

            await acquireUpdateLock();
            return await checkForUpdate.CheckForUpdate(Utility.LocalReleaseFileForAppDir(rootAppDirectory), updateUrlOrPath, ignoreDeltaUpdates, progress, urlDownloader);
        }

        public async Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null)
        {
            var downloadReleases = new DownloadReleasesImpl(rootAppDirectory);
            await acquireUpdateLock();

            await downloadReleases.DownloadReleases(updateUrlOrPath, releasesToDownload, progress, urlDownloader);
        }

        public async Task<string> ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null)
        {
            var applyReleases = new ApplyReleasesImpl(applicationName, rootAppDirectory);
            await acquireUpdateLock();

            return await applyReleases.ApplyReleases(updateInfo, false, false, progress);
        }

        public async Task FullInstall(bool silentInstall = false)
        {
            var updateInfo = await CheckForUpdate();
            await DownloadReleases(updateInfo.ReleasesToApply);

            var applyReleases = new ApplyReleasesImpl(applicationName, rootAppDirectory);
            await acquireUpdateLock();

            await applyReleases.ApplyReleases(updateInfo, silentInstall, true);
        }

        public async Task FullUninstall()
        {
            var applyReleases = new ApplyReleasesImpl(applicationName, rootAppDirectory);
            await acquireUpdateLock();

            await applyReleases.FullUninstall();
        }

        public Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch)
        {
            var installHelpers = new InstallHelperImpl(applicationName, rootAppDirectory);
            return installHelpers.CreateUninstallerRegistryEntry(uninstallCmd, quietSwitch);
        }

        public Task<RegistryKey> CreateUninstallerRegistryEntry()
        {
            var installHelpers = new InstallHelperImpl(applicationName, rootAppDirectory);
            return installHelpers.CreateUninstallerRegistryEntry();
        }

        public void RemoveUninstallerRegistryEntry()
        {
            var installHelpers = new InstallHelperImpl(applicationName, rootAppDirectory);
            installHelpers.RemoveUninstallerRegistryEntry();
        }

        public void CreateShortcutsForExecutable(string exeName, ShortcutLocation locations, bool updateOnly)
        {
            var installHelpers = new ApplyReleasesImpl(applicationName, rootAppDirectory);
            installHelpers.CreateShortcutsForExecutable(exeName, locations, updateOnly);
        }

        public void RemoveShortcutsForExecutable(string exeName, ShortcutLocation locations)
        {
            var installHelpers = new ApplyReleasesImpl(applicationName, rootAppDirectory);
            installHelpers.RemoveShortcutsForExecutable(exeName, locations);
        }

        public Version CurrentlyInstalledVersion(string executable = null)
        {
            executable = executable ??
                Path.GetDirectoryName(typeof(UpdateManager).Assembly.Location);

            if (!executable.StartsWith(rootAppDirectory, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            var appDirName = executable.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .FirstOrDefault(x => x.StartsWith("app-", StringComparison.OrdinalIgnoreCase));

            if (appDirName == null) return null;
            return appDirName.ToVersion();
        }

        public string RootAppDirectory {
            get { return rootAppDirectory; }
        }

        public void Dispose()
        {
            var disp = Interlocked.Exchange(ref updateLock, null);
            if (disp != null) {
                disp.Dispose();
            }
        }

        ~UpdateManager()
        {
            if (updateLock != null) {
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
                        Disposable.Create(() => {}) : new SingleGlobalInstance(key, TimeSpan.FromMilliseconds(2000));
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

        static string getLocalAppDataDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
    }
}