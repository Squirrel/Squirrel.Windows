using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    public partial class UpdateManager
    {
        const string uninstallRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <inheritdoc/>
        [SupportedOSPlatform("windows")]
        public Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch)
        {
            var rootAppDirectory = _config.RootAppDir;
            var applicationName = _config.AppId;

            this.Log().Info($"Writing uninstaller registry entry");

            var releaseContent = File.ReadAllText(_config.ReleasesFilePath, Encoding.UTF8);
            var releases = ReleaseEntry.ParseReleaseFile(releaseContent);
            var latest = Utility.FindCurrentVersion(releases);
            var pkgPath = Path.Combine(_config.PackagesDir, latest.Filename);
            var zp = new ZipPackage(pkgPath);

            // NB: Sometimes the Uninstall key doesn't exist
            using var parentKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                .CreateSubKey("Uninstall", RegistryKeyPermissionCheck.ReadWriteSubTree);

            using var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                .CreateSubKey(uninstallRegSubKey + "\\" + applicationName, RegistryKeyPermissionCheck.ReadWriteSubTree);

            var targetIco = Path.Combine(rootAppDirectory, "app.ico");
            if (File.Exists(targetIco))
                key.SetValue("DisplayIcon", targetIco, RegistryValueKind.String);

            var stringsToWrite = new[] {
                new { Key = "DisplayName", Value = zp.ProductName },
                new { Key = "DisplayVersion", Value = zp.Version.ToString() },
                new { Key = "InstallDate", Value = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) },
                new { Key = "InstallLocation", Value = rootAppDirectory },
                new { Key = "Publisher", Value = zp.ProductCompany },
                new { Key = "QuietUninstallString", Value = String.Format("{0} {1}", uninstallCmd, quietSwitch) },
                new { Key = "UninstallString", Value = uninstallCmd },
                new { Key = "URLUpdateInfo", Value = zp.ProjectUrl != null ? zp.ProjectUrl.ToString() : "", }
            };

            // CS: very rough estimate of installed size. based on a few assumptions:
            // - zip generally achieves a ~62% compression ratio on this kind of data
            // - we usually keep 2 copies of the extracted app (the current, and previous version)
            // - we keep a copy of the compressed package, as well as the extracted package on disk for the latest version
            var compressedSizeInKb = new FileInfo(pkgPath).Length / 1024;
            var estimatedInstallInKb = compressedSizeInKb + (compressedSizeInKb / 0.38d * 2);

            var dwordsToWrite = new[] {
                new { Key = "EstimatedSize", Value = (int) estimatedInstallInKb },
                new { Key = "NoModify", Value = 1 },
                new { Key = "NoRepair", Value = 1 },
                new { Key = "Language", Value = 0x0409 },
            };

            foreach (var kvp in stringsToWrite) {
                key.SetValue(kvp.Key, kvp.Value, RegistryValueKind.String);
            }

            foreach (var kvp in dwordsToWrite) {
                key.SetValue(kvp.Key, kvp.Value, RegistryValueKind.DWord);
            }

            key.Flush();

            return Task.FromResult(key);
        }

        /// <inheritdoc/>
        [SupportedOSPlatform("windows")]
        public Task<RegistryKey> CreateUninstallerRegistryEntry()
        {
            var updateDotExe = Path.Combine(_config.RootAppDir, "Update.exe");
            return CreateUninstallerRegistryEntry(String.Format("\"{0}\" --uninstall", updateDotExe), "-s");
        }

        /// <inheritdoc/>
        [SupportedOSPlatform("windows")]
        public void RemoveUninstallerRegistryEntry()
        {
            this.Log().Info("Removing uninstall registry entry");
            var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default).OpenSubKey(uninstallRegSubKey, true);
            key.DeleteSubKeyTree(_config.AppId, false);
        }
    }
}