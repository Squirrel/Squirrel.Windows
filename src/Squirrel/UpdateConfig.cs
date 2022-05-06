using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Versioning;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    /// <summary> Describes how the application will be installed / updated on the given system. </summary>
    public class UpdateConfig
    {
        /// <summary> The unique application Id. This is used in various app paths. </summary>
        public virtual string AppId { get; }

        public virtual string RootAppDir { get; }

        public virtual string PackagesDir { get; }

        public virtual string TempDir { get; }

        public virtual string VersionStagingDir { get; }

        public virtual string CurrentVersionDir { get; }

        public virtual string UpdateExePath { get; }

        public virtual string ReleasesFilePath => Path.Combine(PackagesDir, "RELEASES");

        public virtual string BetaIdFilePath => Path.Combine(PackagesDir, ".betaId");

        public virtual SemanticVersion CurrentlyInstalledVersion => GetCurrentlyInstalledVersion();

        private static IFullLogger Log() => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(UpdateConfig));

        public UpdateConfig(string applicationIdOverride, string localAppDataDirOverride)
        {
            UpdateExePath = GetUpdateExe();
            AppId = applicationIdOverride ?? GetInstalledApplicationName(UpdateExePath);
            if (AppId != null) {
                RootAppDir = Path.Combine(localAppDataDirOverride ?? GetLocalAppDataDirectory(), AppId);
                CurrentVersionDir = Path.Combine(RootAppDir, "current");
                PackagesDir = Path.Combine(RootAppDir, "packages");
                VersionStagingDir = Path.Combine(RootAppDir, "packages");
                TempDir = Path.Combine(PackagesDir, "Temp");
            }
        }

        internal List<(string PackagePath, SemanticVersion PackageVersion, bool IsDelta)> GetLocalPackages()
        {
            var query = from x in Directory.EnumerateFiles(PackagesDir, "*.nupkg")
                        let re = ReleaseEntry.ParseEntryFileName(x)
                        where re.Version != null
                        select (x, re.Version, re.IsDelta);
            return query.ToList();
        }

        internal string GetVersionStagingPath(SemanticVersion version)
        {
            return Path.Combine(VersionStagingDir, "app-" + version);
        }

        internal string UpdateAndRetrieveCurrentFolder(bool force)
        {
            try {
                var releases = GetVersions();
                var latestVer = releases.OrderByDescending(m => m.Version).First();
                var currentVer = releases.FirstOrDefault(f => f.IsCurrent);

                // if the latest ver is already current, or it does not support
                // being in a current directory.
                if (latestVer.IsCurrent) {
                    Log().Info($"Current directory already pointing to latest version.");
                    return latestVer.DirectoryPath;
                }

                if (force) {
                    Log().Info($"Killing running processes in '{RootAppDir}'.");
                    Utility.KillProcessesInDirectory(RootAppDir);
                }

                // 'current' does exist, and it's wrong, so lets get rid of it
                if (currentVer != default) {
                    string legacyVersionDir = GetVersionStagingPath(currentVer.Version);
                    Log().Info($"Moving '{currentVer.DirectoryPath}' to '{legacyVersionDir}'.");
                    Utility.Retry(() => Directory.Move(currentVer.DirectoryPath, legacyVersionDir));
                }

                // this directory does not support being named 'current'
                if (latestVer.Manifest == null) {
                    Log().Info($"Cannot promote {latestVer.Version} as current as it has no manifest");
                    return latestVer.DirectoryPath;
                }

                // 'current' doesn't exist right now, lets move the latest version
                var latestDir = CurrentVersionDir;
                Log().Info($"Moving '{latestVer.DirectoryPath}' to '{latestDir}'.");
                Utility.Retry(() => Directory.Move(latestVer.DirectoryPath, latestDir));

                Log().Info("Running app in: " + latestDir);
                return latestDir;
            } catch (Exception e) {
                var releases = GetVersions();
                string fallback = releases.OrderByDescending(m => m.Version).First().DirectoryPath;
                var currentVer = releases.FirstOrDefault(f => f.IsCurrent);
                if (currentVer != default && Directory.Exists(currentVer.DirectoryPath)) {
                    fallback = currentVer.DirectoryPath;
                }
                Log().WarnException("Unable to update 'current' directory", e);
                Log().Info("Running app in: " + fallback);
                return fallback;
            }
        }

        internal VersionDirInfo GetLatestVersion()
        {
            return GetLatestVersion(GetVersions());
        }

        internal VersionDirInfo GetLatestVersion(IEnumerable<VersionDirInfo> versions)
        {
            return versions.OrderByDescending(r => r.Version).First();
        }

        internal VersionDirInfo GetVersionInfoFromDirectory(string d)
        {
            bool isCurrent = Utility.FullPathEquals(d, CurrentVersionDir);
            var directoryName = Path.GetFileName(d);
            bool isExecuting = Utility.IsFileInDirectory(SquirrelRuntimeInfo.EntryExePath, d);
            var manifest = Utility.ReadManifestFromVersionDir(d);
            if (manifest != null) {
                return new(manifest, manifest.Version, d, isCurrent, isExecuting);
            } else if (Utility.PathPartStartsWith(directoryName, "app-") && NuGetVersion.TryParse(directoryName.Substring(4), out var ver)) {
                return new(null, ver, d, isCurrent, isExecuting);
            }
            return null;
        }

        internal record VersionDirInfo(IPackage Manifest, SemanticVersion Version, string DirectoryPath, bool IsCurrent, bool IsExecuting);
        internal VersionDirInfo[] GetVersions()
        {
            return Directory.EnumerateDirectories(RootAppDir, "app-*", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateDirectories(VersionStagingDir, "app-*", SearchOption.TopDirectoryOnly))
                .Concat(new[] { CurrentVersionDir })
                .Select(Utility.NormalizePath)
                .Distinct(SquirrelRuntimeInfo.PathStringComparer)
                .Select(GetVersionInfoFromDirectory)
                .Where(d => d != null)
                .ToArray();
        }

        private static string GetInstalledApplicationName(string updateExePath)
        {
            if (updateExePath == null)
                return null;
            var fi = new FileInfo(updateExePath);
            return fi.Directory.Name;
        }

        private static string GetUpdateExe()
        {
            var ourPath = SquirrelRuntimeInfo.EntryExePath;

            // Are we update.exe?
            if (ourPath != null &&
                Path.GetFileName(ourPath).Equals("update.exe", StringComparison.OrdinalIgnoreCase) &&
                ourPath.IndexOf("app-", StringComparison.OrdinalIgnoreCase) == -1 &&
                ourPath.IndexOf("SquirrelTemp", StringComparison.OrdinalIgnoreCase) == -1) {
                return Path.GetFullPath(ourPath);
            }

            var updateDotExe = Path.Combine(SquirrelRuntimeInfo.BaseDirectory, "..\\Update.exe");
            var target = new FileInfo(updateDotExe);

            if (!target.Exists)
                return null;
            return target.FullName;
        }

        private static string GetLocalAppDataDirectory()
        {
            // if we're installed and running as update.exe in the app folder, the app directory root is one folder up
            if (SquirrelRuntimeInfo.IsSingleFile && Path.GetFileName(SquirrelRuntimeInfo.EntryExePath).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) {
                var oneFolderUpFromAppFolder = Path.Combine(Path.GetDirectoryName(SquirrelRuntimeInfo.EntryExePath), "..");
                return Path.GetFullPath(oneFolderUpFromAppFolder);
            }

            // if update exists above us, we're running from within a version directory, and the appdata folder is two above us
            if (File.Exists(Path.Combine(SquirrelRuntimeInfo.BaseDirectory, "..", "Update.exe"))) {
                var twoFoldersUpFromAppFolder = Path.Combine(Path.GetDirectoryName(SquirrelRuntimeInfo.EntryExePath), "..\\..");
                return Path.GetFullPath(twoFoldersUpFromAppFolder);
            }

            // if neither of the above are true, we're probably not installed yet, so return the real appdata directory
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        private SemanticVersion GetCurrentlyInstalledVersion(string executable = null)
        {
            if (String.IsNullOrEmpty(RootAppDir) || String.IsNullOrEmpty(UpdateExePath))
                return null;

            executable = Path.GetFullPath(executable ?? SquirrelRuntimeInfo.EntryExePath);

            // check if the application to check is in the correct application directory
            if (!Utility.IsFileInDirectory(executable, RootAppDir))
                return null;

            // check if Update.exe exists in the expected relative location
            var baseDir = Path.GetDirectoryName(executable);
            if (!File.Exists(Path.Combine(baseDir, "..\\Update.exe")))
                return null;

            // if a 'my version' file exists, use that instead.
            var manifest = Utility.ReadManifestFromVersionDir(baseDir);
            if (manifest != null) {
                return manifest.Version;
            }

            var exePathWithoutAppDir = executable.Substring(RootAppDir.Length);
            var appDirName = exePathWithoutAppDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .FirstOrDefault(x => x.StartsWith("app-", StringComparison.OrdinalIgnoreCase));

            // check if we are inside an 'app-{ver}' directory and extract version
            if (appDirName == null)
                return null;

            return NuGetVersion.Parse(appDirName.Substring(4));
        }
    }
}
