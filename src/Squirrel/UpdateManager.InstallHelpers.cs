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
        internal class InstallHelperImpl : IEnableLogger
        {
            readonly string applicationName;
            readonly string rootAppDirectory;

            public InstallHelperImpl(string applicationName, string rootAppDirectory)
            {
                this.applicationName = applicationName;
                this.rootAppDirectory = rootAppDirectory;
            }

            const string uninstallRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            public Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch)
            {
                this.Log().Info($"Writing uninstaller registry entry");
                var releaseContent = File.ReadAllText(Path.Combine(rootAppDirectory, "packages", "RELEASES"), Encoding.UTF8);
                var releases = ReleaseEntry.ParseReleaseFile(releaseContent);
                var latest = releases.Where(x => !x.IsDelta).OrderByDescending(x => x.Version).First();

                var pkgPath = Path.Combine(rootAppDirectory, "packages", latest.Filename);
                var zp = new ZipPackage(pkgPath);

                // NB: Sometimes the Uninstall key doesn't exist
                using (var parentKey =
                    RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                        .CreateSubKey("Uninstall", RegistryKeyPermissionCheck.ReadWriteSubTree)) {; }

                var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .CreateSubKey(uninstallRegSubKey + "\\" + applicationName, RegistryKeyPermissionCheck.ReadWriteSubTree);

                // we will try to find an "app.ico" from the package, write it to the local app dir, and then 
                // use it for the uninstaller icon. If an app.ico does not exist, it will use a SquirrelAwareApp exe icon instead.
                try {
                    if (zp.AppIconBytes != null) {
                        var targetIco = Path.Combine(rootAppDirectory, "app.ico");
                        File.WriteAllBytes(targetIco, zp.AppIconBytes);
                        this.Log().Info($"File '{targetIco}' is being used for uninstall icon.");
                        key.SetValue("DisplayIcon", targetIco, RegistryValueKind.String);
                    } else {
                        // DisplayIcon can be a path to an exe instead of an ico if an icon was not provided.
                        var appDir = new DirectoryInfo(Utility.AppDirForRelease(rootAppDirectory, latest));
                        var appIconExe = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(appDir.FullName).FirstOrDefault()
                            ?? appDir.GetFiles("*.exe").Select(x => x.FullName).FirstOrDefault();
                        if (appIconExe != null) {
                            this.Log().Info($"There was no icon found, will use '{appIconExe}' for uninstall icon.");
                            key.SetValue("DisplayIcon", appIconExe, RegistryValueKind.String);
                        }
                    }
                } catch (Exception ex) {
                    this.Log().InfoException("Couldn't write uninstall icon, don't care", ex);
                }

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

            public void KillAllProcessesBelongingToPackage()
            {
                Utility.KillProcessesInDirectory(rootAppDirectory);
            }

            public Task<RegistryKey> CreateUninstallerRegistryEntry()
            {
                var updateDotExe = Path.Combine(rootAppDirectory, "Update.exe");
                return CreateUninstallerRegistryEntry(String.Format("\"{0}\" --uninstall", updateDotExe), "-s");
            }

            public void RemoveUninstallerRegistryEntry()
            {
                var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .OpenSubKey(uninstallRegSubKey, true);
                key.DeleteSubKeyTree(applicationName, false);
            }
        }
    }
}
