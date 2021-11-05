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

namespace Squirrel.Update
{
    class Program : IEnableLogger
    {
        static StartupOption opt;
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

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
                using (var logger = new SetupLogLogger(true, "OptionParsing") { Level = LogLevel.Info }) {
                    SquirrelLocator.CurrentMutable.Register(() => logger, typeof(Squirrel.SimpleSplat.ILogger));
                    logger.Write($"Failed to parse command line options. {ex.Message}", LogLevel.Error);
                }
                throw;
            }

            // NB: Trying to delete the app directory while we have Setup.log
            // open will actually crash the uninstaller
            bool isUninstalling = opt.updateAction == UpdateAction.Uninstall;

            using (var logger = new SetupLogLogger(isUninstalling, opt.updateAction.ToString()) { Level = LogLevel.Info }) {
                SquirrelLocator.CurrentMutable.Register(() => logger, typeof(SimpleSplat.ILogger));

                try {
                    return executeCommandLine(args);
                } catch (Exception ex) {
                    logger.Write("Finished with unhandled exception: " + ex, LogLevel.Fatal);
                    throw;
                }
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

        static async Task Install(bool silentInstall, ProgressSource progressSource, string sourceDirectory = null)
        {
            sourceDirectory = sourceDirectory ?? AssemblyRuntimeInfo.BaseDirectory;
            var releasesPath = Path.Combine(sourceDirectory, "RELEASES");

            Log.Info("Starting install, writing to {0}", sourceDirectory);

            if (!AssemblyRuntimeInfo.IsSingleFile) {
                // when doing a standard build, our executable has multiple files/dependencies.
                // we only get turned into a single exe upon publish - and this is required to install a package.
                // in the future, we may consider searching for a pre-published version, or perhaps copy our dependencies.
                throw new Exception("Cannot install a package from a debug build. Publish the Squirrel assembly and retrieve a single-file first.");
            }

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

                    await Log.ErrorIfThrows(() => Utility.DeleteDirectory(mgr.RootAppDirectory),
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

            // CS: We maintain a junction in the app folder named "latest" that points to the latest version.
            // Running exe's from within this "latest" folder means trayicon pins and taskbar pin settings
            // will not require any special handling. Additionally, doing it on ProcessStart (instead of during update)
            // allows us to support the following:
            // - App V1 is running
            // - App V2 is downloaded / installed 
            // - Someone clicks an application shortcut while V1 is still running, and updating the junction will fail
            // - Because the junction updated failed, we will execute V1 exe's again (so we don't have two different versions running at the same time)
            // - Next time the application is fully exited and run again, the junction can be updated and V2 will be executed

            Log.Info("Updating latestver junction to '{0}'", latestAppDir);
            var latestDir = Path.Combine(appDir, "latestver");
            if (JunctionPoint.Exists(latestDir) && JunctionPoint.GetTarget(latestDir) != latestAppDir) {
                // delete an existing junction. If this fails, we can ignore and continue to run the old version
                try {
                    JunctionPoint.Delete(latestDir);
                } catch {
                    Log.Warn("Unable to remove junction, is the app running?");
                }
            }
            if (!JunctionPoint.Exists(latestDir)) {
                try { JunctionPoint.Create(latestDir, latestAppDir, true); } catch { }
            }

            // Check for the EXE name they want
            var targetExe = new FileInfo(Path.Combine(latestDir, exeName.Replace("%20", " ")));
            Log.Info("Want to launch '{0}'", targetExe);

            // Check for path canonicalization attacks
            if (!targetExe.FullName.StartsWith(latestDir, StringComparison.Ordinal)) {
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

    class SetupLogLogger : SimpleSplat.ILogger, IDisposable
    {
        TextWriter inner;
        readonly object gate = 42;
        public SimpleSplat.LogLevel Level { get; set; }

        public SetupLogLogger(bool saveInTemp, string commandSuffix = null)
        {
            for (int i = 0; i < 10; i++) {
                try {
                    var dir = saveInTemp ?
                        Path.GetTempPath() :
                        AssemblyRuntimeInfo.BaseDirectory;
                    var fileName = commandSuffix == null ? String.Format($"Squirrel.{i}.log", i) : String.Format($"Squirrel-{commandSuffix}.{i}.log", i);
                    var file = Path.Combine(dir, fileName.Replace(".0.log", ".log"));
                    var str = File.Open(file, FileMode.Append, FileAccess.Write, FileShare.Read);
                    inner = new StreamWriter(str, Encoding.UTF8, 4096, false) { AutoFlush = true };
                    return;
                } catch (Exception ex) {
                    // Didn't work? Keep going
                    Console.Error.WriteLine("Couldn't open log file, trying new file: " + ex.ToString());
                }
            }

            inner = Console.Error;
        }

        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            lock (gate) inner.WriteLine($"[{DateTime.Now.ToString("dd/MM/yy HH:mm:ss")}] {logLevel.ToString().ToLower()}: {message}");
        }

        public void Dispose()
        {
            lock (gate) {
                inner.Flush();
                inner.Dispose();
            }
        }
    }
}
