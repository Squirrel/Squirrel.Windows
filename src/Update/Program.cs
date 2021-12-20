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
using System.Drawing;

namespace Squirrel.Update
{
    class Program : IEnableLogger
    {
        static StartupOption opt;
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
                var logp = new SetupLogLogger(true, UpdateAction.Unset) { Level = LogLevel.Info };
                logp.Write($"Failed to parse command line options. {ex.Message}", LogLevel.Error);
                throw;
            }

            // NB: Trying to delete the app directory while we have Setup.log
            // open will actually crash the uninstaller
            bool logToTemp =
                opt.updateAction == UpdateAction.Uninstall ||
                opt.updateAction == UpdateAction.Setup ||
                opt.updateAction == UpdateAction.Install;

            var logger = new SetupLogLogger(logToTemp, opt.updateAction) { Level = LogLevel.Info };
            SquirrelLocator.CurrentMutable.Register(() => logger, typeof(SimpleSplat.ILogger));

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
                Setup(opt.target, opt.silentInstall, opt.checkInstall).Wait();
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
                ProcessStart(opt.processStart, opt.processStartArgs, opt.shouldWait);
                break;
            }

            Log.Info("Finished Squirrel Updater");
            return 0;
        }

        static async Task Setup(string setupPath, bool silentInstall, bool checkInstall)
        {
            Log.Info($"Extracting bundled app data from '{setupPath}'.");
            var info = BundledSetupInfo.ReadFromFile(setupPath);

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
                var localadinstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), info.AppId);
                if (Directory.Exists(localadinstall)) {
                    Log.Info($"App install detected at '{localadinstall}', exiting...");
                    return;
                }

                // C:\ProgramData\$pkgName\$username
                var programdatainstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), info.AppId, Environment.UserName);
                if (Directory.Exists(programdatainstall)) {
                    Log.Info($"App install detected at '{programdatainstall}', exiting...");
                    return;
                }

                Log.Info("Installation for this user was not found. Beginning silent setup...");
                silentInstall = true;
            }

            using var _t = Utility.WithTempDirectory(out var tempFolder);

            // show splash screen
            SplashWindow splash = null;
            if (!silentInstall && info.SplashImageBytes?.Length > 0) {
                Log.Info($"Showing splash window");
                splash = new SplashWindow(
                    info.SetupIconBytes?.Length > 0 ? new Icon(new MemoryStream(info.SetupIconBytes)) : null,
                    (Bitmap) Image.FromStream(new MemoryStream(info.SplashImageBytes)));
                splash.Show();
                splash.SetProgressIndeterminate();
            }

            void showUserMsg(bool error, string message, string title)
            {
                if (!silentInstall) {
                    Log.Info("User shown message: " + message);
                    User32MessageBox.Show(splash?.Handle ?? IntPtr.Zero, message, info.AppFriendlyName + " - " + title, User32MessageBox.MessageBoxButtons.OK,
                        error ? User32MessageBox.MessageBoxIcon.Error : User32MessageBox.MessageBoxIcon.Information);
                } else {
                    Log.Info("User message suppressed (updater in silent mode): " + message);
                }
            }

            bool askUserQuestion(string message, string title)
            {
                if (!silentInstall) {
                    var result = User32MessageBox.Show(splash?.Handle ?? IntPtr.Zero, message, info.AppFriendlyName + " - " + title, User32MessageBox.MessageBoxButtons.OKCancel,
                        User32MessageBox.MessageBoxIcon.Question, User32MessageBox.MessageBoxResult.Cancel);
                    Log.Info("User prompted: '" + message + "' -- User answered " + result.ToString());
                    return User32MessageBox.MessageBoxResult.OK == result;
                } else {
                    Log.Info("User prompt suppressed (updater in silent mode): '" + message + "' -- Automatically answering Cancel.");
                    return false;
                }
            }

            var missingFrameworks = info.RequiredFrameworks
                .Select(f => Runtimes.GetRuntimeByName(f))
                .Where(f => f != null)
                .Where(f => !f.CheckIsInstalled().Result)
                .ToArray();

            // prompt user to install missing dependencies
            if (missingFrameworks.Any()) {
                string message = missingFrameworks.Length > 1
                    ? $"{info.AppFriendlyName} is missing the following system components: {String.Join(", ", missingFrameworks.Select(s => s.DisplayName))}. " +
                      $"Would you like to install these now?"
                    : $"{info.AppFriendlyName} requires {missingFrameworks.First().DisplayName} installed to continue, would you like to install it now?";

                if (!askUserQuestion(message, "Missing System Components")) {
                    return; // user cancelled install
                }

                bool rebootRequired = false;

                // iterate through each missing dependency and download/run the installer.
                foreach (var f in missingFrameworks) {
                    var localPath = Path.Combine(tempFolder, f.Id + ".exe");
                    await f.DownloadToFile(localPath, e => splash?.SetProgress((ulong) e.BytesReceived, (ulong) e.TotalBytesToReceive));
                    splash?.SetProgressIndeterminate();

                    // hide splash screen while the runtime installer is running so the user can see progress
                    splash?.Hide();
                    var exitcode = await f.InvokeInstaller(localPath, silentInstall);
                    splash?.Show();

                    if (exitcode == RuntimeInstallResult.RestartRequired) {
                        rebootRequired = true;
                        continue;
                    } else if (exitcode != RuntimeInstallResult.Success) {
                        string rtmsg = exitcode switch {
                            RuntimeInstallResult.UserCancelled => $"User cancelled install of {f.DisplayName}. Setup can not continue and will now exit.",
                            RuntimeInstallResult.AnotherInstallInProgress => "Another installation is already in progress. Complete that installation before proceeding with this install.",
                            RuntimeInstallResult.SystemDoesNotMeetRequirements => $"This computer does not meet the system requirements for {f.DisplayName}.",
                            _ => $"{f.DisplayName} installer exited with error code '{exitcode}'.",
                        };
                        showUserMsg(true, rtmsg, $"Error installing {f.DisplayName}");
                        return;
                    }
                }

                if (rebootRequired) {
                    // TODO: automatic restart setup after reboot
                    showUserMsg(false, $"A restart is required before Setup can continue.", "Restart required");
                    return;
                }
            }

            // setup package source directory
            Log.Info($"Starting package install from directory " + tempFolder);
            splash?.SetProgressIndeterminate();
            string packagePath = Path.Combine(tempFolder, info.BundledPackageName);
            File.WriteAllBytes(packagePath, info.BundledPackageBytes);
            var entry = ReleaseEntry.GenerateFromFile(packagePath);
            ReleaseEntry.WriteReleaseFile(new[] { entry }, Path.Combine(tempFolder, "RELEASES"));

            var progressSource = new ProgressSource();
            progressSource.Progress += (e, p) => {
                // post install hooks are about to be run (app will start)
                if (p >= 90) splash?.Close();
                else splash?.SetProgress((ulong) p, 90);
            };

            await Install(silentInstall, progressSource, tempFolder);
            splash?.Close();
        }

        static async Task Install(bool silentInstall, ProgressSource progressSource, string sourceDirectory = null)
        {
            sourceDirectory = sourceDirectory ?? AssemblyRuntimeInfo.BaseDirectory;
            var releasesPath = Path.Combine(sourceDirectory, "RELEASES");

            Log.Info("Starting install, writing to {0}", sourceDirectory);

            //if (!AssemblyRuntimeInfo.IsSingleFile) {
            //    // when doing a standard build, our executable has multiple files/dependencies.
            //    // we only get turned into a single exe upon publish - and this is required to install a package.
            //    // in the future, we may consider searching for a pre-published version, or perhaps copy our dependencies.
            //    throw new Exception("Cannot install a package from a debug build. Publish the Squirrel assembly and retrieve a single-file first.");
            //}

            if (!File.Exists(releasesPath)) {
                Log.Info("RELEASES doesn't exist, creating it at " + releasesPath);
                var nupkgs = (new DirectoryInfo(sourceDirectory)).GetFiles()
                    .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    .Select(x => ReleaseEntry.GenerateFromFile(x.FullName));

                ReleaseEntry.WriteReleaseFile(nupkgs, releasesPath);
            }

            var ourAppName = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesPath, Encoding.UTF8))
                .First().PackageName;

            using (var mgr = new UpdateManager(sourceDirectory, ourAppName)) {
                Log.Info("About to install to: " + mgr.RootAppDirectory);
                if (Directory.Exists(mgr.RootAppDirectory)) {
                    Log.Warn("Install path {0} already exists, burning it to the ground", mgr.RootAppDirectory);

                    mgr.KillAllExecutablesBelongingToPackage();
                    await Task.Delay(500);

                    await Log.ErrorIfThrows(() => Utility.Retry(() => Utility.DeleteDirectory(mgr.RootAppDirectory)),
                        "Failed to remove existing directory on full install, is the app still running???");

                    Log.ErrorIfThrows(() => Utility.Retry(() => Directory.CreateDirectory(mgr.RootAppDirectory), 3),
                        "Couldn't recreate app directory, perhaps Antivirus is blocking it");
                }

                Directory.CreateDirectory(mgr.RootAppDirectory);

                var updateTarget = Path.Combine(mgr.RootAppDirectory, "Update.exe");
                Log.ErrorIfThrows(() => File.Copy(AssemblyRuntimeInfo.EntryExePath, updateTarget, true),
                    "Failed to copy Update.exe to " + updateTarget);

                await mgr.FullInstall(silentInstall, progressSource.Raise);

                await Log.ErrorIfThrows(() => mgr.CreateUninstallerRegistryEntry(),
                    "Failed to create uninstaller registry entry");
            }
        }

        static async Task Update(string updateUrl, string appName = null)
        {
            appName = appName ?? getAppNameFromDirectory();

            Log.Info("Starting update, downloading from " + updateUrl);

            using (var mgr = new UpdateManager(updateUrl, appName)) {
                bool ignoreDeltaUpdates = false;
                Log.Info("About to update to: " + mgr.RootAppDirectory);

            retry:
                try {
                    // 3 % (3 stages)
                    var updateInfo = await mgr.CheckForUpdate(intention: UpdaterIntention.Update, ignoreDeltaUpdates: ignoreDeltaUpdates, progress: x => Console.WriteLine(UpdateManager.CalculateProgress(x, 0, 3)));

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

                var updateTarget = Path.Combine(mgr.RootAppDirectory, "Update.exe");

                await Log.ErrorIfThrows(() =>
                    mgr.CreateUninstallerRegistryEntry(),
                    "Failed to create uninstaller registry entry");
            }
        }

        static async Task UpdateSelf()
        {
            waitForParentToExit();
            var src = AssemblyRuntimeInfo.EntryExePath;
            var updateDotExeForOurPackage = Path.Combine(
                Path.GetDirectoryName(src),
                "..", "Update.exe");

            await Task.Run(() => {
                File.Copy(src, updateDotExeForOurPackage, true);
            });
        }

        static async Task<string> Download(string updateUrl, string appName = null)
        {
            appName = appName ?? getAppNameFromDirectory();

            Log.Info("Fetching update information, downloading from " + updateUrl);
            using (var mgr = new UpdateManager(updateUrl, appName)) {
                var updateInfo = await mgr.CheckForUpdate(intention: UpdaterIntention.Update, progress: x => Console.WriteLine(x / 3));
                await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(33 + x / 3));

                var releaseNotes = updateInfo.FetchReleaseNotes();

                var sanitizedUpdateInfo = new {
                    currentVersion = updateInfo.CurrentlyInstalledVersion.Version.ToString(),
                    futureVersion = updateInfo.FutureReleaseEntry.Version.ToString(),
                    releasesToApply = updateInfo.ReleasesToApply.Select(x => new {
                        version = x.Version.ToString(),
                        releaseNotes = releaseNotes.ContainsKey(x) ? releaseNotes[x] : "",
                    }).ToArray(),
                };

                return SimpleJson.SerializeObject(sanitizedUpdateInfo);
            }
        }

        static async Task<string> CheckForUpdate(string updateUrl, string appName = null)
        {
            appName = appName ?? getAppNameFromDirectory();

            Log.Info("Fetching update information, downloading from " + updateUrl);
            using (var mgr = new UpdateManager(updateUrl, appName)) {
                var updateInfo = await mgr.CheckForUpdate(intention: UpdaterIntention.Update, progress: x => Console.WriteLine(x));
                var releaseNotes = updateInfo.FetchReleaseNotes();

                var sanitizedUpdateInfo = new {
                    currentVersion = updateInfo.CurrentlyInstalledVersion.Version.ToString(),
                    futureVersion = updateInfo.FutureReleaseEntry.Version.ToString(),
                    releasesToApply = updateInfo.ReleasesToApply.Select(x => new {
                        version = x.Version.ToString(),
                        releaseNotes = releaseNotes.ContainsKey(x) ? releaseNotes[x] : "",
                    }).ToArray(),
                };

                return SimpleJson.SerializeObject(sanitizedUpdateInfo);
            }
        }

        static async Task Uninstall(string appName = null)
        {
            Log.Info("Starting uninstall for app: " + appName);

            appName = appName ?? getAppNameFromDirectory();
            using (var mgr = new UpdateManager("", appName)) {
                await mgr.FullUninstall();
                mgr.RemoveUninstallerRegistryEntry();

                // if this exe is in the app directory, starts a process that will wait 3 seconds and then delete this exe
                if (AssemblyRuntimeInfo.EntryExePath.StartsWith(mgr.RootAppDirectory, StringComparison.InvariantCultureIgnoreCase)) {
                    Process.Start(new ProcessStartInfo() {
                        Arguments = "/C choice /C Y /N /D Y /T 3 & Del \"" + AssemblyRuntimeInfo.EntryExePath + "\"",
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

            var appName = getAppNameFromDirectory();
            var defaultLocations = ShortcutLocation.StartMenu | ShortcutLocation.Desktop;
            var locations = parseShortcutLocations(shortcutArgs);

            using (var mgr = new UpdateManager("", appName)) {
                mgr.CreateShortcutsForExecutable(exeName, locations ?? defaultLocations, onlyUpdate, processStartArgs, icon);
            }
        }

        static void Deshortcut(string exeName, string shortcutArgs)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            var appName = getAppNameFromDirectory();
            var defaultLocations = ShortcutLocation.StartMenu | ShortcutLocation.Desktop;
            var locations = parseShortcutLocations(shortcutArgs);

            using (var mgr = new UpdateManager("", appName)) {
                mgr.RemoveShortcutsForExecutable(exeName, locations ?? defaultLocations);
            }
        }

        static void ProcessStart(string exeName, string arguments, bool shouldWait)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            // Find the latest installed version's app dir
            var appDir = AssemblyRuntimeInfo.BaseDirectory;
            var releases = ReleaseEntry.ParseReleaseFile(
                File.ReadAllText(Utility.LocalReleaseFileForAppDir(appDir), Encoding.UTF8));

            // NB: We add the hacked up version in here to handle a migration
            // issue, where versions of Squirrel pre PR #450 will not understand
            // prerelease tags, so it will end up writing the release name sans
            // tags. However, the RELEASES file _will_ have them, so we need to look
            // for directories that match both the real version, and the sanitized
            // version, giving priority to the former.
            var latestAppDir = releases
                .OrderByDescending(x => x.Version)
                .SelectMany(x => new[] {
                    Utility.AppDirForRelease(appDir, x),
                    Utility.AppDirForVersion(appDir, new SemanticVersion(x.Version.Version.Major, x.Version.Version.Minor, x.Version.Version.Build, ""))
                })
                .FirstOrDefault(x => Directory.Exists(x));

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
            if (AuthenticodeTools.IsTrusted(AssemblyRuntimeInfo.EntryExePath) && !AuthenticodeTools.IsTrusted(targetExe.FullName)) {
                Log.Error("File {0} is not trusted, and will not be run from a trusted context.", targetExe);
                throw new ArgumentException();
            }

            if (shouldWait) waitForParentToExit();

            try {
                Log.Info("About to launch: '{0}': {1}", targetExe.FullName, arguments ?? "");
                Process.Start(new ProcessStartInfo(targetExe.FullName, arguments ?? "") { WorkingDirectory = Path.GetDirectoryName(targetExe.FullName) });
            } catch (Exception ex) {
                Log.ErrorException("Failed to start process", ex);
            }
        }

        static void ShowHelp()
        {
            ensureConsole();
            opt.WriteOptionDescriptions();
        }

        static void waitForParentToExit()
        {
            // Grab a handle the parent process
            var parentPid = NativeMethods.GetParentProcessId();
            var handle = default(IntPtr);

            // Wait for our parent to exit
            try {
                handle = NativeMethods.OpenProcess(ProcessAccess.Synchronize, false, parentPid);
                if (handle != IntPtr.Zero) {
                    Log.Info("About to wait for parent PID {0}", parentPid);
                    NativeMethods.WaitForSingleObject(handle, 0xFFFFFFFF /*INFINITE*/);
                } else {
                    Log.Info("Parent PID {0} no longer valid - ignoring", parentPid);
                }
            } finally {
                if (handle != IntPtr.Zero) NativeMethods.CloseHandle(handle);
            }
        }

        static string getAppNameFromDirectory(string path = null)
        {
            path = path ?? AssemblyRuntimeInfo.BaseDirectory;
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

        static int consoleCreated = 0;
        static void ensureConsole()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;

            if (Interlocked.CompareExchange(ref consoleCreated, 1, 0) == 1) return;

            if (!NativeMethods.AttachConsole(-1)) {
                NativeMethods.AllocConsole();
            }

            NativeMethods.GetStdHandle(StandardHandles.STD_ERROR_HANDLE);
            NativeMethods.GetStdHandle(StandardHandles.STD_OUTPUT_HANDLE);
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
