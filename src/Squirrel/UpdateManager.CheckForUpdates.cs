using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
    public sealed partial class UpdateManager
    {
        internal class CheckForUpdateImpl : IEnableLogger
        {
            readonly string rootAppDirectory;

            // TODO: rip this out
            readonly FrameworkVersion appFrameworkVersion = FrameworkVersion.Net45;

            public CheckForUpdateImpl(string rootAppDirectory)
            {
                this.rootAppDirectory = rootAppDirectory;
            }

            public async Task<UpdateInfo> CheckForUpdate(
                string localReleaseFile,
                string updateUrlOrPath,
                bool ignoreDeltaUpdates = false, 
                Action<int> progress = null,
                IFileDownloader urlDownloader = null)
            {
                progress = progress ?? (_ => { });

                var localReleases = Enumerable.Empty<ReleaseEntry>();

                bool shouldInitialize = false;
                try {
                    localReleases = Utility.LoadLocalReleases(localReleaseFile);
                } catch (Exception ex) {
                    // Something has gone pear-shaped, let's start from scratch
                    this.Log().WarnException("Failed to load local releases, starting from scratch", ex);
                    shouldInitialize = true;
                }

                if (shouldInitialize) await initializeClientAppDirectory();

                string releaseFile;

                // Fetch the remote RELEASES file, whether it's a local dir or an 
                // HTTP URL
                if (Utility.IsHttpUrl(updateUrlOrPath)) {
                    if (updateUrlOrPath.EndsWith("/"))
                        updateUrlOrPath = updateUrlOrPath.Substring(0, updateUrlOrPath.Length - 1);

                    this.Log().Info("Downloading RELEASES file from {0}", updateUrlOrPath);

                    try {
                        var data = await urlDownloader.DownloadUrl(String.Format("{0}/{1}", updateUrlOrPath, "RELEASES"));
                        releaseFile = Encoding.UTF8.GetString(data);
                    } catch (WebException ex) {
                        this.Log().InfoException("Download resulted in WebException (returning blank release list)", ex);
                        releaseFile = String.Empty;
                    }

                    progress(33);
                } else {
                    this.Log().Info("Reading RELEASES file from {0}", updateUrlOrPath);

                    if (!Directory.Exists(updateUrlOrPath)) {
                        var message = String.Format(
                            "The directory {0} does not exist, something is probably broken with your application", 
                            updateUrlOrPath);

                        throw new Exception(message);
                    }

                    var fi = new FileInfo(Path.Combine(updateUrlOrPath, "RELEASES"));
                    if (!fi.Exists) {
                        var message = String.Format(
                            "The file {0} does not exist, something is probably broken with your application", 
                            fi.FullName);

                        this.Log().Warn(message);

                        var packages = (new DirectoryInfo(updateUrlOrPath)).GetFiles("*.nupkg");
                        if (packages.Length == 0) {
                            throw new Exception(message);
                        }

                        // NB: Create a new RELEASES file since we've got a directory of packages
                        ReleaseEntry.WriteReleaseFile(
                            packages.Select(x => ReleaseEntry.GenerateFromFile(x.FullName)), fi.FullName);
                    }

                    releaseFile = File.ReadAllText(fi.FullName, Encoding.UTF8);
                    progress(33);
                }

                var ret = default(UpdateInfo);
                var remoteReleases = ReleaseEntry.ParseReleaseFile(releaseFile);
                progress(66);

                if (remoteReleases.Any()) {
                    ret = determineUpdateInfo(localReleases, remoteReleases, ignoreDeltaUpdates);
                }

                progress(100);
                return ret;
            }

            async Task initializeClientAppDirectory()
            {
                // On bootstrap, we won't have any of our directories, create them
                var pkgDir = Path.Combine(rootAppDirectory, "packages");
                if (Directory.Exists(pkgDir)) {
                    await Utility.DeleteDirectory(pkgDir);
                }

                Directory.CreateDirectory(pkgDir);
            }

            UpdateInfo determineUpdateInfo(IEnumerable<ReleaseEntry> localReleases, IEnumerable<ReleaseEntry> remoteReleases, bool ignoreDeltaUpdates)
            {
                var packageDirectory = Utility.PackageDirectoryForAppDir(rootAppDirectory);
                localReleases = localReleases ?? Enumerable.Empty<ReleaseEntry>();

                if (remoteReleases == null) {
                    this.Log().Warn("Release information couldn't be determined due to remote corrupt RELEASES file");
                    throw new Exception("Corrupt remote RELEASES file");
                }

                if (localReleases.Count() == remoteReleases.Count()) {
                    this.Log().Info("No updates, remote and local are the same");

                    var latestFullRelease = Utility.FindCurrentVersion(remoteReleases);
                    var currentRelease = Utility.FindCurrentVersion(localReleases);

                    var info = UpdateInfo.Create(currentRelease, new[] {latestFullRelease}, packageDirectory, appFrameworkVersion);
                    return info;
                }

                if (ignoreDeltaUpdates) {
                    remoteReleases = remoteReleases.Where(x => !x.IsDelta);
                }

                if (!localReleases.Any()) {
                    this.Log().Warn("First run or local directory is corrupt, starting from scratch");

                    var latestFullRelease = Utility.FindCurrentVersion(remoteReleases);
                    return UpdateInfo.Create(Utility.FindCurrentVersion(localReleases), new[] {latestFullRelease}, packageDirectory, appFrameworkVersion);
                }

                if (localReleases.Max(x => x.Version) > remoteReleases.Max(x => x.Version)) {
                    this.Log().Warn("hwhat, local version is greater than remote version");

                    var latestFullRelease = Utility.FindCurrentVersion(remoteReleases);
                    return UpdateInfo.Create(Utility.FindCurrentVersion(localReleases), new[] {latestFullRelease}, packageDirectory, appFrameworkVersion);
                }

                return UpdateInfo.Create(Utility.FindCurrentVersion(localReleases), remoteReleases, packageDirectory, appFrameworkVersion);
            }
        }
    }
}
