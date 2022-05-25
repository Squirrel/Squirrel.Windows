using System;
using System.IO;
using System.Threading.Tasks;
using Squirrel.Sources;

namespace Squirrel.Tests.TestHelpers
{
    class UpdateManagerTestImpl : UpdateManager
    {
        protected UpdateManagerTestImpl(IUpdateSource source, AppDesc config) : base(source, config)
        {
        }

        public static UpdateManagerTestImpl FromLocalPackageTempDir(string updatePackageDir, string appId, string installTempDir)
        {
            return FromLocalPackageTempDir(new DirectoryInfo(updatePackageDir), appId, installTempDir);
        }
        
        private static UpdateManagerTestImpl FromLocalPackageTempDir(DirectoryInfo updatePackageDir, string appId, string installTempDir)
        {
            var appPath = Path.Combine(installTempDir, appId);
            Directory.CreateDirectory(appPath);
            var desc = new AppDescWindows(appPath, appId);
            return new UpdateManagerTestImpl(new SimpleFileSource(updatePackageDir), desc);
        }

        public static UpdateManagerTestImpl FromFakeWebSource(string packageUrl, string appId, string installTempDir, IFileDownloader downloader = null)
        {
            var appPath = Path.Combine(installTempDir, appId);
            Directory.CreateDirectory(appPath);
            var desc = new AppDescWindows(appPath, appId);
            return new UpdateManagerTestImpl(new SimpleWebSource(packageUrl, downloader), desc);
        }
        
        public Task<string> ApplyReleasesPublic(UpdateInfo updateInfo, bool silentInstall, bool attemptingFullInstall, Action<int> progress = null)
        {
            return this.ApplyReleases(updateInfo, silentInstall, attemptingFullInstall, progress);
        }
    }
}