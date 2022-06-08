using Squirrel.SimpleSplat;
using Squirrel.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.NuGet;
using Squirrel.Lib;
using Squirrel.Sources;
using static Squirrel.Runtimes.RuntimeInstallResult;

namespace Squirrel.Update
{
    class Program : IEnableLogger
    {
        private static StartupOption opt;
        private static SetupLogLogger _log;
        
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        [STAThread]
        public static int Main(string[] args)
        {
            try {
                return main(args);
            } catch (Exception ex) {
                // NB: Normally this is a terrible idea but we want to make
                // sure Setup.exe above us gets the nonzero error code
                Console.Error.WriteLine(ex);
                return -1;
            }
        }

        static int main(string[] args)
        {
            try {
                opt = new StartupOption(args);
            } catch (Exception ex) {
                _log = new SetupLogLogger(UpdateAction.Unset);
                _log.Write($"Failed to parse command line options. {ex.Message}", LogLevel.Error);
                throw;
            }

            var logger = new SetupLogLogger(opt.updateAction);
            SquirrelLocator.CurrentMutable.Register(() => logger, typeof(ILogger));

            try {
                return executeCommandLine(args);
            } catch (Exception ex) {
                logger.Write("Finished with unhandled exception: " + ex, LogLevel.Fatal);
                throw;
            }
        }

        static int executeCommandLine(string[] args)
        {
            Log.Info("Starting Squirrel Updater: " + String.Join(" ", args));
            Log.Info("Updater location is: " + SquirrelRuntimeInfo.EntryExePath);

            // if the app runs this exe, we do not want to be locking the app directory
            Environment.CurrentDirectory = SquirrelRuntimeInfo.BaseDirectory;

            if (args.Any(x => x.StartsWith("/squirrel", StringComparison.OrdinalIgnoreCase))) {
                // NB: We're marked as Squirrel-aware, but we don't want to do
                // anything in response to these events
                return 0;
            }

            if (opt.updateAction == UpdateAction.Unset) {
                ShowHelp();
                return -1;
            }

            switch (opt.updateAction) {
            case UpdateAction.Setup:
                try {
                    Setup(opt.target, opt.setupOffset, opt.silentInstall, opt.checkInstall).Wait();
                } catch (Exception ex) when (!opt.silentInstall && !opt.checkInstall) {
                    // when not in silent mode, we should endeavor to show a message.
                    // we will also exit code 0, so Setup.exe does not think
                    // it was an outright crash and show another error dialog.
                    Log.FatalException("Encountered fatal unhandled exception.", ex);
                    Windows.User32MessageBox.Show(
                        IntPtr.Zero,
                        "Setup encountered fatal error: " + ex.Message + Environment.NewLine
                        + $"There may be more detailed information in '{_log.LogFilePath}'.",
                        "Setup Error",
                        Windows.User32MessageBox.MessageBoxButtons.OK,
                        Windows.User32MessageBox.MessageBoxIcon.Error);
                }

                break;
            case UpdateAction.Install:
                var progressSource = new ProgressSource();
                Install(opt.silentInstall, progressSource, Path.GetFullPath(opt.target)).Wait();
                break;
            case UpdateAction.Uninstall:
                Uninstall().Wait();
                break;
            case UpdateAction.Download:
                Console.WriteLine(Download(opt.target).Result);
                break;
            case UpdateAction.Update:
                Update(opt.target).Wait();
                break;
            case UpdateAction.CheckForUpdate:
                Console.WriteLine(CheckForUpdate(opt.target).Result);
                break;
            case UpdateAction.UpdateSelf:
                UpdateSelf().Wait();
                break;
            case UpdateAction.Shortcut:
                Shortcut(opt.target, opt.shortcutArgs, opt.processStartArgs, opt.icon, opt.onlyUpdateShortcuts);
                break;
            case UpdateAction.Deshortcut:
                Deshortcut(opt.target, opt.shortcutArgs);
                break;
            case UpdateAction.ProcessStart:
                ProcessStart(opt.processStart, opt.processStartArgs, opt.shouldWait, opt.forceLatest);
                break;
            }

            Log.Info("Finished Squirrel Updater");
            return 0;
        }

        static async Task Setup(string setupPath, long setupBundleOffset, bool silentInstall, bool checkInstall)
        {
            Log.Info($"Extracting bundled app data from '{setupPath}'.");

            using var fsFull = File.OpenRead(setupPath);
            using var fs = new SubStream(fsFull, setupBundleOffset);

            var zp = new ZipPackage(fs, true);
            var appname = zp.ProductName;

            if (checkInstall) {
                // CS: migrated from MachineInstaller.cpp
                // here we are being run by the MSI deployment tool. We should check if the app 
                // is installed. if it is, we exit. If not, we install it silently. The goal is
                // to ensure that all users of a system will always have this app installed.
                Log.Info($"Has --checkInstall argument, verifying machine-wide installation");

                // NB: Users often get into the sitch where they install the MSI, then try to
                // install the standalone package on top of that. In previous versions we tried
                // to detect if the app was properly installed, but now we're taking the much 
                // more conservative approach, that if the package dir exists in any way, we're
                // bailing out

                // C:\Users\Username\AppData\Local\$pkgName
                var localadinstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), zp.Id);
                if (Directory.Exists(localadinstall)) {
                    Log.Info($"App install detected at '{localadinstall}', exiting...");
                    return;
                }

                // C:\ProgramData\$pkgName\$username
                var programdatainstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), zp.Id, Environment.UserName);
                if (Directory.Exists(programdatainstall)) {
                    Log.Info($"App install detected at '{programdatainstall}', exiting...");
                    return;
                }

                Log.Info("Installation for this user was not found. Beginning silent setup...");
                silentInstall = true;
            }

            using var _t = Utility.GetTempDirectory(out var tempFolder);
            using ISplashWindow splash = new ComposedWindow(appname, silentInstall, zp.SetupIconBytes, zp.SetupSplashBytes);

            // verify that this package can be installed on this cpu architecture
            if (SquirrelRuntimeInfo.IsPackageCompatibleWithCurrentOS(zp.MachineArchitecture) == false) {
                splash.ShowErrorDialog("Incompatible System",
                    $"The current operating system uses the {SquirrelRuntimeInfo.SystemArchitecture} cpu architecture, " +
                    $"but this package is for {zp.MachineArchitecture} and cannot be installed on this computer.");
                return;
            }

            var missingFrameworks = getMissingRuntimes(zp);

            // prompt user to install missing dependencies
            if (missingFrameworks.Any()) {
                string message = missingFrameworks.Length > 1
                    ? $"{appname} is missing the following system components: {String.Join(", ", missingFrameworks.Select(s => s.DisplayName))}. " +
                      $"Would you like to install these now?"
                    : $"{appname} requires {missingFrameworks.First().DisplayName} installed to continue, would you like to install it now?";

                if (!splash.ShowQuestionDialog("Missing System Components", message)) {
                    return; // user cancelled install
                }

                var success = await installMissingRuntimes(splash, missingFrameworks, silentInstall, tempFolder);
                if (!success) {
                    return;
                }
            }

            splash.SetMessage("Extracting package...");
            Log.Info($"Starting package install from directory " + tempFolder);
            splash.SetProgressIndeterminate();

            // copy package to directory
            string packagePath = Path.Combine(tempFolder, zp.FullReleaseFilename);
            fs.Position = 0;
            using (var writeStream = File.Open(packagePath, FileMode.Create, FileAccess.ReadWrite))
                fs.CopyTo(writeStream);

            // create RELEASES file for UpdateManager to read
            var entry = ReleaseEntry.GenerateFromFile(packagePath);
            ReleaseEntry.WriteReleaseFile(new[] { entry }, Path.Combine(tempFolder, "RELEASES"));

            var progressSource = new ProgressSource();
            progressSource.Progress += (e, p) => {
                // post install hooks are about to be run (app will start)
                if (p >= 90) splash.Hide();
                else splash.SetProgress((ulong) p, 90);
            };

            splash.SetMessage(null);
            await Install(silentInstall, progressSource, tempFolder);
        }

        static async Task Install(bool silentInstall, ProgressSource progressSource, string sourceDirectory = null)
        {
            sourceDirectory = sourceDirectory ?? SquirrelRuntimeInfo.BaseDirectory;
            var releasesPath = Path.Combine(sourceDirectory, "RELEASES");
            var sourceDi = new DirectoryInfo(sourceDirectory);

            Log.Info("Starting install, writing to {0}", sourceDirectory);

            //if (!AssemblyRuntimeInfo.IsSingleFile) {
            //    // when doing a standard build, our executable has multiple files/dependencies.
            //    // we only get turned into a single exe upon publish - and this is required to install a package.
            //    // in the future, we may consider searching for a pre-published version, or perhaps copy our dependencies.
            //    throw new Exception("Cannot install a package from a debug build. Publish the Squirrel assembly and retrieve a single-file first.");
            //}

            if (!File.Exists(releasesPath)) {
                Log.Info("RELEASES doesn't exist, creating it at " + releasesPath);
                var nupkgs = sourceDi.GetFiles()
                    .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    .Select(x => ReleaseEntry.GenerateFromFile(x.FullName));

                ReleaseEntry.WriteReleaseFile(nupkgs, releasesPath);
            }

            var ourAppName = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesPath, Encoding.UTF8))
                .First().PackageName;

            var rootAppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ourAppName);
            var appDesc = new AppDescWindows(rootAppDir, ourAppName, true);
            using var mgr = new UpdateManager(new SimpleFileSource(sourceDi), appDesc);

            Log.Info("About to install to: " + rootAppDir);
            if (Directory.Exists(rootAppDir)) {
                Log.Warn("Install path {0} already exists, burning it to the ground", rootAppDir);

                mgr.KillAllExecutablesBelongingToPackage();
                await Task.Delay(500);

                Log.ErrorIfThrows(() => Utility.Retry(() => Utility.DeleteFileOrDirectoryHard(rootAppDir, throwOnFailure: true, renameFirst: true)),
                    "Failed to remove existing directory on full install, is the app still running???");

                Log.ErrorIfThrows(() => Utility.Retry(() => Directory.CreateDirectory(rootAppDir), 3),
                    "Couldn't recreate app directory, perhaps Antivirus is blocking it");
            }

            Directory.CreateDirectory(rootAppDir);

            var updateTarget = Path.Combine(rootAppDir, "Update.exe");
            Log.ErrorIfThrows(() => File.Copy(SquirrelRuntimeInfo.EntryExePath, updateTarget, true),
                "Failed to copy Update.exe to " + updateTarget);

            await mgr.FullInstall(silentInstall, progressSource.Raise);

            using var rk = await Log.ErrorIfThrows(() => mgr.CreateUninstallerRegistryEntry(),
                "Failed to create uninstaller registry entry");
        }

        static async Task Update(string updateUrl)
        {
            Log.Info("Starting update, downloading from " + updateUrl);

            using var mgr = new UpdateManager(updateUrl);
            bool ignoreDeltaUpdates = false;
            Log.Info("About to update to: " + mgr.Config.RootAppDir);

            retry:
            try {
                // 3 % (3 stages)
                var updateInfo = await mgr.CheckForUpdate(intention: UpdaterIntention.Update, ignoreDeltaUpdates: ignoreDeltaUpdates,
                    progress: x => Console.WriteLine(UpdateManager.CalculateProgress(x, 0, 3)));

                // 3 - 30 %
                await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(UpdateManager.CalculateProgress(x, 3, 30)));

                // 30 - 100 %
                await mgr.ApplyReleases(updateInfo, x => Console.WriteLine(UpdateManager.CalculateProgress(x, 30, 100)));
            } catch (Exception ex) {
                if (ignoreDeltaUpdates) {
                    Log.ErrorException("Really couldn't apply updates!", ex);
                    throw;
                }

                Log.WarnException("Failed to apply updates, falling back to full updates", ex);
                ignoreDeltaUpdates = true;
                goto retry;
            }

            using var rk = await Log.ErrorIfThrows(() =>
                    mgr.CreateUninstallerRegistryEntry(),
                "Failed to create uninstaller registry entry");
        }

        static async Task UpdateSelf()
        {
            PlatformUtil.WaitForParentProcessToExit();
            var src = SquirrelRuntimeInfo.EntryExePath;
            var updateDotExeForOurPackage = Path.Combine(
                Path.GetDirectoryName(src),
                "..", "Update.exe");

            await Task.Run(() => {
                File.Copy(src, updateDotExeForOurPackage, true);
            });
        }

        static async Task<string> Download(string updateUrl)
        {
            Log.Info("Fetching update information, downloading from " + updateUrl);
            using var mgr = new UpdateManager(updateUrl);
            var updateInfo = await mgr.CheckForUpdate(intention: UpdaterIntention.Update, progress: x => Console.WriteLine(x / 3));
            await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(33 + x / 3));

            var releaseNotes = updateInfo.FetchReleaseNotes();

            var sanitizedUpdateInfo = new {
                currentVersion = updateInfo.LatestLocalReleaseEntry.Version.ToString(),
                futureVersion = updateInfo.FutureReleaseEntry.Version.ToString(),
                releasesToApply = updateInfo.ReleasesToApply.Select(x => new {
                    version = x.Version.ToString(),
                    releaseNotes = releaseNotes.ContainsKey(x) ? releaseNotes[x] : "",
                }).ToArray(),
            };

            return SimpleJson.SerializeObject(sanitizedUpdateInfo);
        }

        static async Task<string> CheckForUpdate(string updateUrl)
        {
            Log.Info("Fetching update information, downloading from " + updateUrl);
            using var mgr = new UpdateManager(updateUrl);
            var updateInfo = await mgr.CheckForUpdate(intention: UpdaterIntention.Update, progress: x => Console.WriteLine(x));
            var releaseNotes = updateInfo.FetchReleaseNotes();

            var sanitizedUpdateInfo = new {
                currentVersion = updateInfo.LatestLocalReleaseEntry.Version.ToString(),
                futureVersion = updateInfo.FutureReleaseEntry.Version.ToString(),
                releasesToApply = updateInfo.ReleasesToApply.Select(x => new {
                    version = x.Version.ToString(),
                    releaseNotes = releaseNotes.ContainsKey(x) ? releaseNotes[x] : "",
                }).ToArray(),
            };

            return SimpleJson.SerializeObject(sanitizedUpdateInfo);
        }

        static async Task Uninstall(string appName = null)
        {
            Log.Info("Starting uninstall for app: " + appName);

            using (var mgr = new UpdateManager()) {
                await mgr.FullUninstall();
                mgr.RemoveUninstallerRegistryEntry();

                // if this exe is in the app directory, starts a process that will wait 3 seconds and then delete this exe
                if (Utility.IsFileInDirectory(SquirrelRuntimeInfo.EntryExePath, mgr.Config.RootAppDir)) {
                    Process.Start(new ProcessStartInfo() {
                        Arguments = "/C choice /C Y /N /D Y /T 3 & Del \"" + SquirrelRuntimeInfo.EntryExePath + "\"",
                        WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, FileName = "cmd.exe"
                    });
                }
            }
        }

        static void Shortcut(string exeName, string shortcutArgs, string processStartArgs, string icon, bool onlyUpdate)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            var defaultLocations = ShortcutLocation.StartMenu | ShortcutLocation.Desktop;
            var locations = parseShortcutLocations(shortcutArgs);

            using var mgr = new UpdateManager();
            mgr.CreateShortcutsForExecutable(exeName, locations ?? defaultLocations, onlyUpdate, processStartArgs, icon);
        }

        static void Deshortcut(string exeName, string shortcutArgs)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            var defaultLocations = ShortcutLocation.StartMenu | ShortcutLocation.Desktop;
            var locations = parseShortcutLocations(shortcutArgs);

            using var mgr = new UpdateManager();
            mgr.RemoveShortcutsForExecutable(exeName, locations ?? defaultLocations);
        }

        static void ProcessStart(string exeName, string arguments, bool shouldWait, bool forceLatest)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            if (shouldWait) PlatformUtil.WaitForParentProcessToExit();

            var config = new AppDescWindows();
            var latestAppDir = config.UpdateAndRetrieveCurrentFolder(forceLatest);

            // Check for the EXE name they want
            var targetExe = new FileInfo(Path.Combine(latestAppDir, exeName.Replace("%20", " ")));
            Log.Info("Want to launch '{0}'", targetExe);

            // Check for path canonicalization attacks
            if (!targetExe.FullName.StartsWith(latestAppDir, StringComparison.Ordinal)) {
                throw new ArgumentException();
            }

            if (!targetExe.Exists) {
                Log.Error("File {0} doesn't exist in current release", targetExe);
                throw new ArgumentException();
            }

            // Do not run an unsigned EXE if we ourselves are signed. 
            // Maybe TODO: check that it's signed with the same certificate as us?
            if (AuthenticodeTools.IsTrusted(SquirrelRuntimeInfo.EntryExePath) && !AuthenticodeTools.IsTrusted(targetExe.FullName)) {
                Log.Error("File {0} is not trusted, and will not be run from a trusted context.", targetExe);
                throw new ArgumentException();
            }

            // check that the app we're about to run has all of it's dependencies installed
            var manifest = Utility.ReadManifestFromVersionDir(latestAppDir);
            if (manifest != null && manifest.RuntimeDependencies?.Any() == true) {
                var missingFrameworks = getMissingRuntimes(manifest);
                if (missingFrameworks.Any()) {
                    Log.Info("App has missing dependencies: " + String.Join(", ", missingFrameworks.Select(s => s.DisplayName)));
                    var answer = Windows.User32MessageBox.Show(IntPtr.Zero, getMissingRuntimesMessage(manifest.ProductName, missingFrameworks),
                        "Cannot start " + manifest.ProductName, Windows.User32MessageBox.MessageBoxButtons.OKCancel);

                    if (answer != Windows.User32MessageBox.MessageBoxResult.OK) {
                        Log.Info("User did not want to install dependencies. Aborting.");
                        return;
                    }

                    using var splash = new ComposedWindow(manifest.ProductName, false, null, null);
                    splash.Show();
                    using var _ = Utility.GetTempDirectory(out var tempFolder);
                    var success = installMissingRuntimes(splash, missingFrameworks, false, tempFolder).Result;
                    if (!success) return;
                }
            }

            // launch the EXE
            try {
                Log.Info("About to launch: '{0}': {1}", targetExe.FullName, arguments ?? "");
                Process.Start(new ProcessStartInfo(targetExe.FullName, arguments ?? "") { WorkingDirectory = Path.GetDirectoryName(targetExe.FullName) });
            } catch (Exception ex) {
                Log.ErrorException("Failed to start process", ex);
            }
        }

        static void ShowHelp()
        {
            PlatformUtil.AttachConsole();
            opt.WriteOptionDescriptions();
        }

        static string getMissingRuntimesMessage(string appname, Runtimes.RuntimeInfo[] missingFrameworks)
        {
            return missingFrameworks.Length > 1
                ? $"{appname} is missing the following system components: {String.Join(", ", missingFrameworks.Select(s => s.DisplayName))}. " +
                  $"Would you like to install these now?"
                : $"{appname} requires {missingFrameworks.First().DisplayName} installed to continue, would you like to install it now?";
        }

        static Runtimes.RuntimeInfo[] getMissingRuntimes(NuspecManifest manifest)
        {
            if (manifest.RuntimeDependencies == null || !manifest.RuntimeDependencies.Any())
                return new Runtimes.RuntimeInfo[0];

            return manifest.RuntimeDependencies
                .Select(f => Runtimes.GetRuntimeByName(f))
                .Where(f => f != null)
                .Where(f => !f.CheckIsInstalled().Result)
                .ToArray();
        }

        static async Task<bool> installMissingRuntimes(ISplashWindow splash, Runtimes.RuntimeInfo[] missingFrameworks, bool silentInstall, string tempFolder)
        {
            bool rebootRequired = false;

            // iterate through each missing dependency and download/run the installer.
            foreach (var f in missingFrameworks) {
                var localPath = Path.Combine(tempFolder, f.Id + ".exe");
                splash.SetMessage($"Downloading {f.DisplayName}...");
                await f.DownloadToFile(localPath, e => splash.SetProgress((ulong) e, 100));
                splash.SetProgressIndeterminate();

                // hide splash screen while the runtime installer is running so the user can see progress
                splash.Hide();
                var exitcode = await f.InvokeInstaller(localPath, silentInstall);
                splash.Show();

                if (exitcode == RestartRequired) {
                    rebootRequired = true;
                    continue;
                } else if (exitcode != InstallSuccess) {
                    string rtmsg = exitcode switch {
                        UserCancelled => $"User cancelled install of {f.DisplayName}. Setup can not continue and will now exit.",
                        AnotherInstallInProgress =>
                            "Another installation is already in progress. Complete that installation before proceeding with this install.",
                        SystemDoesNotMeetRequirements => $"This computer does not meet the system requirements for {f.DisplayName}.",
                        _ => $"{f.DisplayName} installer exited with error code '{exitcode}'.",
                    };
                    splash.ShowErrorDialog($"Error installing {f.DisplayName}", rtmsg);
                    return false;
                }
            }

            if (rebootRequired) {
                // TODO: automatic restart setup after reboot
                splash.ShowInfoDialog("Restart required", $"A restart is required before Setup can continue.");
                return false;
            }

            return true;
        }

        static string getAppNameFromDirectory(string path = null)
        {
            path = path ?? SquirrelRuntimeInfo.BaseDirectory;
            return (new DirectoryInfo(path)).Name;
        }

        static ShortcutLocation? parseShortcutLocations(string shortcutArgs)
        {
            var ret = default(ShortcutLocation?);

            if (!String.IsNullOrWhiteSpace(shortcutArgs)) {
                var args = shortcutArgs.Split(new[] { ',' });

                foreach (var arg in args) {
                    var location = (ShortcutLocation) (Enum.Parse(typeof(ShortcutLocation), arg, false));
                    if (ret.HasValue) {
                        ret |= location;
                    } else {
                        ret = location;
                    }
                }
            }

            return ret;
        }
    }

    public class ProgressSource
    {
        public event EventHandler<int> Progress;

        public void Raise(int i)
        {
            if (Progress != null)
                Progress.Invoke(this, i);
        }
    }
}