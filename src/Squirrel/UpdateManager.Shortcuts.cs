using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;
using Squirrel.Shell;

namespace Squirrel
{
    public partial class UpdateManager
    {
        /// <inheritdoc/>
        public void CreateShortcutsForExecutable(string exeName, ShortcutLocation locations, bool updateOnly, string programArguments = null, string icon = null)
        {
            var appDir = AppDirectory;
            var releases = Utility.GetAppVersionDirectories(appDir).OrderByDescending(r => r.Version).ToArray();
            var version = releases.FirstOrDefault(r => r.IsExecuting) ?? releases.First();
            var exePath = Path.Combine(version.DirectoryPath, exeName);
            FileVersionInfo info = File.Exists(exePath) ? FileVersionInfo.GetVersionInfo(exePath) : null;

            foreach (var f in (ShortcutLocation[]) Enum.GetValues(typeof(ShortcutLocation))) {
                if (!locations.HasFlag(f)) continue;

                var file = linkTargetForVersionInfo(f, version.Manifest, info, exeName);
                var fileExists = File.Exists(file);

                // NB: If we've already installed the app, but the shortcut
                // is no longer there, we have to assume that the user didn't
                // want it there and explicitly deleted it, so we shouldn't
                // annoy them by recreating it.
                if (!fileExists && updateOnly) {
                    this.Log().Warn("Wanted to update shortcut {0} but it appears user deleted it", file);
                    continue;
                }

                this.Log().Info("Creating shortcut for {0} => {1}", exeName, file);

                this.ErrorIfThrows(() => Utility.Retry(() => {
                    File.Delete(file);

                    var target = Path.Combine(appDir, exeName); // stub exe
                    using var sl = new ShellLink {
                        Target = target,
                        IconPath = icon ?? target,
                        IconIndex = 0,
                        WorkingDirectory = appDir,
                        Description = version.Manifest.ProductDescription,
                    };

                    if (!String.IsNullOrWhiteSpace(programArguments)) {
                        sl.Arguments += programArguments;
                    }

                    //var appUserModelId = Utility.GetAppUserModelId(zf.Id, exeName);
                    //var toastActivatorCLSID = Utility.CreateGuidFromHash(appUserModelId).ToString();
                    //sl.SetAppUserModelId(appUserModelId);
                    //sl.SetToastActivatorCLSID(toastActivatorCLSID);

                    this.Log().Info("About to save shortcut: {0} (target {1}, workingDir {2}, args {3})", file, sl.Target, sl.WorkingDirectory, sl.Arguments);
                    if (ModeDetector.InUnitTestRunner() == false) sl.Save(file);
                }), "Can't write shortcut: " + file);
            }

        }

        /// <inheritdoc/>
        public void RemoveShortcutsForExecutable(string exeName, ShortcutLocation locations)
        {
            foreach (var lnk in EnumerateShortcutLocations(exeName, locations)) {
                if (File.Exists(lnk)) {
                    this.Log().Info("Removing shortcut: " + lnk);
                    Utility.DeleteFileOrDirectoryHardOrGiveUp(lnk);
                }
            }
        }

        /// <inheritdoc/>
        protected void RemoveAllShortcutsForPackage()
        {
            this.Log().Info("Searching system and deleting all shortcuts for this package.");
            var appDir = AppDirectory;
            var searchPaths = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar"),
            };

            foreach (var rootPath in searchPaths) {
                foreach (var shortcutPath in Directory.EnumerateFiles(rootPath, "*.lnk", SearchOption.AllDirectories)) {
                    try {
                        using var lnk = new ShellLink(shortcutPath);
                        if (Utility.IsFileInDirectory(lnk.Target, appDir)) {
                            this.Log().Info("Deleting shortcut: " + shortcutPath);
                            File.Delete(shortcutPath);
                        }
                    } catch { }
                }
            }
        }

        /// <inheritdoc/>
        protected IEnumerable<string> EnumerateShortcutLocations(string exeName, ShortcutLocation locations)
        {
            var releases = Utility.GetAppVersionDirectories(AppDirectory).OrderByDescending(r => r.Version).ToArray();
            var version = releases.FirstOrDefault(r => r.IsExecuting) ?? releases.First();
            var exePath = Path.Combine(version.DirectoryPath, exeName);
            FileVersionInfo info = File.Exists(exePath) ? FileVersionInfo.GetVersionInfo(exePath) : null;

            foreach (var f in (ShortcutLocation[]) Enum.GetValues(typeof(ShortcutLocation))) {
                if (!locations.HasFlag(f)) continue;
                var location = linkTargetForVersionInfo(f, version.Manifest, info, exeName);
                yield return location;
            }
        }

        string linkTargetForVersionInfo(ShortcutLocation location, IPackage package, FileVersionInfo versionInfo, string exeName)
        {
            var possibleProductNames = new[] {
                versionInfo?.ProductName,
                package.ProductName,
                versionInfo?.FileDescription,
                Path.GetFileNameWithoutExtension(exeName),
            };

            var possibleCompanyNames = new[] {
                package.ProductCompany,
            };

            var pkgName = possibleProductNames.First(x => !String.IsNullOrWhiteSpace(x));
            var prodCompany = possibleCompanyNames.First(x => !String.IsNullOrWhiteSpace(x));

            return getLinkTarget(location, pkgName, prodCompany);
        }

        string getLinkTarget(ShortcutLocation location, string title, string productCompany, bool createDirectoryIfNecessary = true)
        {
            var dir = default(string);

            switch (location) {
            case ShortcutLocation.Desktop:
                dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                break;
            case ShortcutLocation.StartMenu:
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", productCompany);
                break;
            case ShortcutLocation.StartMenuRoot:
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                break;
            case ShortcutLocation.Startup:
                dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                break;
            case ShortcutLocation.AppRoot:
                dir = AppDirectory;
                break;
            }

            if (createDirectoryIfNecessary && !Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            return Path.Combine(dir, title + ".lnk");
        }
    }
}
