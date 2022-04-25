using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
        public Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch)
        {
            var rootAppDirectory = AppDirectory;
            var applicationName = AppId;

            this.Log().Info($"Writing uninstaller registry entry");
            var releaseContent = File.ReadAllText(Path.Combine(rootAppDirectory, "packages", "RELEASES"), Encoding.UTF8);
            var releases = ReleaseEntry.ParseReleaseFile(releaseContent);
            var latest = Utility.FindCurrentVersion(releases);
            var pkgPath = Path.Combine(Utility.PackageDirectoryForAppDir(rootAppDirectory), latest.Filename);
            var zp = new ZipPackage(pkgPath);

            // NB: Sometimes the Uninstall key doesn't exist
            using (var parentKey =
                RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .CreateSubKey("Uninstall", RegistryKeyPermissionCheck.ReadWriteSubTree)) {; }

            var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                .CreateSubKey(uninstallRegSubKey + "\\" + applicationName, RegistryKeyPermissionCheck.ReadWriteSubTree);

            // we will try to find an "app.ico" from the package, write it to the local app dir, and then 
            // use it for the uninstaller icon. If an app.ico does not exist, it will use a SquirrelAwareApp exe icon instead.

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
                new { Key = "EstimatedSize", Value = (int)estimatedInstallInKb },
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

            return Task.FromResult(key);
        }

        /// <inheritdoc/>
        public void KillAllExecutablesBelongingToPackage()
        {
            Utility.KillProcessesInDirectory(AppDirectory);
        }

        /// <inheritdoc/>
        public Task<RegistryKey> CreateUninstallerRegistryEntry()
        {
            var updateDotExe = Path.Combine(AppDirectory, "Update.exe");
            return CreateUninstallerRegistryEntry(String.Format("\"{0}\" --uninstall", updateDotExe), "-s");
        }

        /// <inheritdoc/>
        public void RemoveUninstallerRegistryEntry()
        {
            var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default).OpenSubKey(uninstallRegSubKey, true);
            key.DeleteSubKeyTree(AppId, false);
        }
    }
}
