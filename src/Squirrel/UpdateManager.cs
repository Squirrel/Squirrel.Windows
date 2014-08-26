using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

        public async Task ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null)
        {
            var applyReleases = new ApplyReleasesImpl(rootAppDirectory);
            await acquireUpdateLock();

            await applyReleases.ApplyReleases(updateInfo, false, progress);
        }

        public async Task FullInstall(bool silentInstall = false)
        {
            var updateInfo = await CheckForUpdate();
            await DownloadReleases(updateInfo.ReleasesToApply);

            var applyReleases = new ApplyReleasesImpl(rootAppDirectory);
            await acquireUpdateLock();

            await applyReleases.ApplyReleases(updateInfo, silentInstall);
        }

        public async Task FullUninstall()
        {
            var applyReleases = new ApplyReleasesImpl(rootAppDirectory);
            await acquireUpdateLock();

            await applyReleases.FullUninstall();
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
                // TODO: We'll bring this back later
                var key = Utility.CalculateStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(rootAppDirectory)));
                var theLock = Disposable.Create(() => { });

                /*
                IDisposable theLock;
                try {
                    theLock = RxApp.InUnitTestRunner() ?
                        Disposable.Empty : new SingleGlobalInstance(key, 2000);
                } catch (TimeoutException) {
                    throw new TimeoutException("Couldn't acquire update lock, another instance may be running updates");
                }
                */

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