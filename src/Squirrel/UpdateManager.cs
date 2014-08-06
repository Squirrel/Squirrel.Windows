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

        public Task<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates, Action<int> progress = null)
        {
            var checkForUpdate = new CheckForUpdates(rootAppDirectory);
            return checkForUpdate.CheckForUpdate(Utility.LocalReleaseFileForAppDir(rootAppDirectory), updateUrlOrPath, ignoreDeltaUpdates, progress, urlDownloader);
        }

        public Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null)
        {
            throw new NotImplementedException();
        }

        public Task ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null)
        {
            throw new NotImplementedException();
        }

        public async Task FullUninstall()
        {
            await acquireUpdateLock();
            await fullUninstall();
        }

        IEnumerable<DirectoryInfo> getReleases()
        {
            var rootDirectory = new DirectoryInfo(rootAppDirectory);

            if (!rootDirectory.Exists) return Enumerable.Empty<DirectoryInfo>();

            return rootDirectory.GetDirectories()
                .Where(x => x.Name.StartsWith("app-", StringComparison.InvariantCultureIgnoreCase));
        }

        IEnumerable<DirectoryInfo> getOldReleases(Version version)
        {
            return getReleases()
                .Where(x => x.Name.ToVersion() < version)
                .ToArray();
        }

        async Task fullUninstall(Version version = null)
        {
            version = version ?? new Version(255, 255, 255, 255);
            this.Log().Info("Uninstalling version '{0}'", version);


            // find all the old releases (and this one)
            var directoriesToDelete = getOldReleases(version)
                .Concat(new [] { getDirectoryForRelease(version) })
                .Where(d => d.Exists)
                .Select(d => d.FullName);

            await directoriesToDelete.ForEachAsync(x => Utility.DeleteDirectoryWithFallbackToNextReboot(x));

            if (!getReleases().Any()) {
                await Utility.DeleteDirectoryWithFallbackToNextReboot(rootAppDirectory);
            }
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

        DirectoryInfo getDirectoryForRelease(Version releaseVersion)
        {
            return new DirectoryInfo(Path.Combine(rootAppDirectory, "app-" + releaseVersion));
        }
    }
}
