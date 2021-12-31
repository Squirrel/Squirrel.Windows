using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel.NuGet;
using System.Reflection;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    public sealed partial class UpdateManager
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
            public async Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch)
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
                var iconFile = zp.GetLibFiles().FirstOrDefault(f => f.Path.EndsWith("app.ico", StringComparison.InvariantCultureIgnoreCase));
                if (iconFile != null) {
                    try {
                        var targetIco = Path.Combine(rootAppDirectory, "app.ico");
                        using (var iconStream = iconFile.GetStream())
                        using (var targetStream = File.Open(targetIco, FileMode.Create, FileAccess.Write))
                            await iconStream.CopyToAsync(targetStream).ConfigureAwait(false);
                        this.Log().Info($"File '{targetIco}' is being used for uninstall icon.");
                        key.SetValue("DisplayIcon", targetIco, RegistryValueKind.String);
                    } catch (Exception ex) {
                        this.Log().InfoException("Couldn't write uninstall icon, don't care", ex);
                    }
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

                var stringsToWrite = new[] {
                    new { Key = "DisplayName", Value = zp.Title ?? zp.Description ?? zp.Summary },
                    new { Key = "DisplayVersion", Value = zp.Version.ToString() },
                    new { Key = "InstallDate", Value = DateTime.Now.ToString("yyyyMMdd") },
                    new { Key = "InstallLocation", Value = rootAppDirectory },
                    new { Key = "Publisher", Value = String.Join(",", zp.Authors) },
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

                return key;
            }

            public void KillAllProcessesBelongingToPackage()
            {
                var ourExePath = AssemblyRuntimeInfo.EntryExePath;

                UnsafeUtility.EnumerateProcesses()
                    .Where(x => {
                        // Processes we can't query will have an empty process name, we can't kill them
                        // anyways
                        if (String.IsNullOrWhiteSpace(x.Item1)) return false;

                        // Files that aren't in our root app directory are untouched
                        if (!x.Item1.StartsWith(rootAppDirectory, StringComparison.OrdinalIgnoreCase)) return false;

                        // Never kill our own EXE
                        if (ourExePath != null && x.Item1.Equals(ourExePath, StringComparison.OrdinalIgnoreCase)) return false;

                        var name = Path.GetFileName(x.Item1).ToLowerInvariant();
                        if (name == "squirrel.exe" || name == "update.exe") return false;

                        return true;
                    })
                    .ForEach(x => {
                        try {
                            this.WarnIfThrows(() => Process.GetProcessById(x.Item2).Kill());
                        } catch { }
                    });
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
