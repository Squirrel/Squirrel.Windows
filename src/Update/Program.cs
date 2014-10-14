using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using Splat;
using Squirrel;

namespace Squirrel.Update
{
    enum UpdateAction {
        Unset = 0, Install, Uninstall, Download, Update, Releasify, Shortcut, 
        Deshortcut, ProcessStart, UpdateSelf,
    }

    class Program : IEnableLogger 
    {
        static OptionSet opts;

        public static int Main(string[] args)
        {
            var pg = new Program();
            try {
                return pg.main(args);
            } catch (Exception ex) {
                // NB: Normally this is a terrible idea but we want to make
                // sure Setup.exe above us gets the nonzero error code
                Console.Error.WriteLine(ex);
                return -1;
            }
        }

        int main(string[] args)
        {
            var animatedGifWindowToken = new CancellationTokenSource();

            // NB: Trying to delete the app directory while we have Setup.log 
            // open will actually crash the uninstaller
            bool isUninstalling = args.Any(x => x.Contains("uninstall"));

            // Uncomment to test Gifs
            //AnimatedGifWindow.ShowWindow(TimeSpan.FromMilliseconds(0), animatedGifWindowToken.Token);
            //Thread.Sleep(10 * 60 * 1000);

            using (Disposable.Create(() => animatedGifWindowToken.Cancel()))
            using (var logger = new SetupLogLogger(isUninstalling) { Level = Splat.LogLevel.Info }) {
                Splat.Locator.CurrentMutable.Register(() => logger, typeof(Splat.ILogger));

                this.Log().Info("Starting Squirrel Updater: " + String.Join(" ", args));

                if (args.Any(x => x.StartsWith("/squirrel", StringComparison.OrdinalIgnoreCase))) {
                    // NB: We're marked as Squirrel-aware, but we don't want to do
                    // anything in response to these events
                    return 0;
                }

                bool silentInstall = false;
                var updateAction = default(UpdateAction);

                string target = default(string);
                string releaseDir = default(string);
                string packagesDir = default(string);
                string bootstrapperExe = default(string);
                string backgroundGif = default(string);
                string signingParameters = default(string);
                string baseUrl = default(string);
                string processStart = default(string);
                string processStartArgs = default(string);
                string appName = default(string);

                opts = new OptionSet() {
                    "Usage: Squirrel.exe command [OPTS]",
                    "Manages Squirrel packages",
                    "",
                    "Commands",
                    { "install=", "Install the app whose package is in the specified directory", v => { updateAction = UpdateAction.Install; target = v; } },
                    { "uninstall", "Uninstall the app the same dir as Update.exe", v => updateAction = UpdateAction.Uninstall},
                    { "download=", "Download the releases specified by the URL and write new results to stdout as JSON", v => { updateAction = UpdateAction.Download; target = v; } },
                    { "update=", "Update the application to the latest remote version specified by URL", v => { updateAction = UpdateAction.Update; target = v; } },
                    { "releasify=", "Update or generate a releases directory with a given NuGet package", v => { updateAction = UpdateAction.Releasify; target = v; } },
                    { "createShortcut=", "Create a shortcut for the given executable name", v => { updateAction = UpdateAction.Shortcut; target = v; } },
                    { "removeShortcut=", "Remove a shortcut for the given executable name", v => { updateAction = UpdateAction.Deshortcut; target = v; } },
                    { "updateSelf=", "Copy the currently executing Update.exe into the default location", v => { updateAction =  UpdateAction.UpdateSelf; appName = v; } },
                    { "processStart=", "Start an executable in the latest version of the app package", v => { updateAction =  UpdateAction.ProcessStart; processStart = v; }, true},
                    "",
                    "Options:",
                    { "h|?|help", "Display Help and exit", _ => {} },
                    { "r=|releaseDir=", "Path to a release directory to use with releasify", v => releaseDir = v},
                    { "p=|packagesDir=", "Path to the NuGet Packages directory for C# apps", v => packagesDir = v},
                    { "bootstrapperExe=", "Path to the Setup.exe to use as a template", v => bootstrapperExe = v},
                    { "g=|loadingGif=", "Path to an animated GIF to be displayed during installation", v => backgroundGif = v},
                    { "n=|signWithParams=", "Sign the installer via SignTool.exe with the parameters given", v => signingParameters = v},
                    { "s|silent", "Silent install", _ => silentInstall = true},
                    { "b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true},
                    { "a=|process-start-args=", "Arguments that will be used when starting executable", v => processStartArgs = v, true},
                };

                opts.Parse(args);

                if (updateAction == UpdateAction.Unset) {
                    ShowHelp();
                    return -1;
                }

                switch (updateAction) {
                case UpdateAction.Install:
                    AnimatedGifWindow.ShowWindow(TimeSpan.FromSeconds(4), animatedGifWindowToken.Token);
                    Install(silentInstall, Path.GetFullPath(target)).Wait();
                    break;
                case UpdateAction.Uninstall:
                    Uninstall().Wait();
                    break;
                case UpdateAction.Download:
                    Console.WriteLine(Download(target).Result);
                    break;
                case UpdateAction.Update:
                    Update(target).Wait();
                    break;
                case UpdateAction.UpdateSelf:
                    UpdateSelf(appName).Wait();
                    break;
                case UpdateAction.Releasify:
                    Releasify(target, releaseDir, packagesDir, bootstrapperExe, backgroundGif, signingParameters, baseUrl);
                    break;
                case UpdateAction.Shortcut:
                    Shortcut(target);
                    break;
                case UpdateAction.Deshortcut:
                    Deshortcut(target);
                    break;
                case UpdateAction.ProcessStart:
                    ProcessStart(processStart, processStartArgs);
                    break;
                }
            }

            return 0;
        }

        public async Task Install(bool silentInstall, string sourceDirectory = null)
        {
            sourceDirectory = sourceDirectory ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var releasesPath = Path.Combine(sourceDirectory, "RELEASES");

            this.Log().Info("Starting install, writing to {0}", sourceDirectory);

            if (!File.Exists(releasesPath)) {
                this.Log().Info("RELEASES doesn't exist, creating it at " + releasesPath);
                var nupkgs = (new DirectoryInfo(sourceDirectory)).GetFiles()
                    .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    .Select(x => ReleaseEntry.GenerateFromFile(x.FullName));

                ReleaseEntry.WriteReleaseFile(nupkgs, releasesPath);
            }

            var ourAppName = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesPath, Encoding.UTF8))
                .First().PackageName;

            using (var mgr = new UpdateManager(sourceDirectory, ourAppName, FrameworkVersion.Net45)) {
                await mgr.FullInstall(silentInstall);
                var updateTarget = Path.Combine(mgr.RootAppDirectory, "Update.exe");

                this.ErrorIfThrows(() => File.Copy(Assembly.GetExecutingAssembly().Location, updateTarget, true),
                    "Failed to copy Update.exe to " + updateTarget);

                await this.ErrorIfThrows(() =>
                    mgr.CreateUninstallerRegistryEntry(String.Format("{0} --uninstall", updateTarget), "-s"),
                    "Failed to create uninstaller registry entry");
            }
        }

        public async Task Update(string updateUrl, string appName = null)
        {
            appName = appName ?? getAppNameFromDirectory();

            this.Log().Info("Starting update, downloading from " + updateUrl);
            using (var mgr = new UpdateManager(updateUrl, appName, FrameworkVersion.Net45)) {
                var updateInfo = await mgr.CheckForUpdate(progress: x => Console.WriteLine(x / 3));
                await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(33 + x / 3));
                await mgr.ApplyReleases(updateInfo, x => Console.WriteLine(66 + x / 3));

                var updateTarget = Path.Combine(mgr.RootAppDirectory, "Update.exe");

                await this.ErrorIfThrows(() =>
                    mgr.CreateUninstallerRegistryEntry(String.Format("{0} --uninstall", updateTarget), "-s"),
                    "Failed to create uninstaller registry entry");
            }
        }

        public async Task UpdateSelf(string appName)
        {
            var localAppDir = Environment.ExpandEnvironmentVariables("%LocalAppData%");
            var targetDir = new DirectoryInfo(
                Path.Combine(localAppDir, appName));

            waitForParentToExit();

            if (!targetDir.Exists) {
                throw new ArgumentException("Target app isn't installed!");
            }

            if (!targetDir.FullName.StartsWith(localAppDir, StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException();
            }

            var src = Assembly.GetExecutingAssembly().Location;
            if (targetDir.FullName.Equals(src, StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("Can't update yourself with yourself, that's silly");
            }

            await Task.Run(() => {
                File.Copy(
                    src,
                    Path.Combine(targetDir.FullName, "Update.exe"), 
                    true);
            });
        }

        public async Task<string> Download(string updateUrl, string appName = null)
        {
            ensureConsole();
            appName = appName ?? getAppNameFromDirectory();

            this.Log().Info("Fetching update information, downloading from " + updateUrl);
            using (var mgr = new UpdateManager(updateUrl, appName, FrameworkVersion.Net45)) {
                var updateInfo = await mgr.CheckForUpdate(progress: x => Console.WriteLine(x / 3));
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

        public async Task Uninstall(string appName = null)
        {
            this.Log().Info("Starting uninstall for app: " + appName);

            appName = appName ?? getAppNameFromDirectory();
            using (var mgr = new UpdateManager("", appName, FrameworkVersion.Net45)) {
                await mgr.FullUninstall();
                mgr.RemoveUninstallerRegistryEntry();
            }
        }

        public void Releasify(string package, string targetDir = null, string packagesDir = null, string bootstrapperExe = null, string backgroundGif = null, string signingOpts = null, string baseUrl = null)
        {
            if (baseUrl != null) {
                if (!Utility.IsHttpUrl(baseUrl)) {
                    throw new Exception(string.Format("Invalid --baseUrl '{0}'. A base URL must start with http or https and be a valid URI.", baseUrl));
                }

                if (!baseUrl.EndsWith("/")) {
                    baseUrl += "/";
                }
            }

            targetDir = targetDir ?? ".\\Releases";
            packagesDir = packagesDir ?? ".";
            bootstrapperExe = bootstrapperExe ?? ".\\Setup.exe";

            if (!Directory.Exists(targetDir)) {
                Directory.CreateDirectory(targetDir);
            }

            if (!File.Exists(bootstrapperExe)) {
                bootstrapperExe = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
                    "Setup.exe");
            }

            this.Log().Info("Bootstrapper EXE found at:" + bootstrapperExe);

            var di = new DirectoryInfo(targetDir);
            File.Copy(package, Path.Combine(di.FullName, Path.GetFileName(package)), true);

            var allNuGetFiles = di.EnumerateFiles()
                .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));

            var toProcess = allNuGetFiles.Where(x => !x.Name.Contains("-delta") && !x.Name.Contains("-full"));
            var processed = new List<string>();

            var releaseFilePath = Path.Combine(di.FullName, "RELEASES");
            var previousReleases = Enumerable.Empty<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                previousReleases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8));
            }

            foreach (var file in toProcess) {
                this.Log().Info("Creating release package: " + file.FullName);

                var rp = new ReleasePackage(file.FullName);
                rp.CreateReleasePackage(Path.Combine(di.FullName, rp.SuggestedReleaseFileName), packagesDir, contentsPostProcessHook: pkgPath => {
                    if (signingOpts == null) return;

                    new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                        .Where(x => x.Name.ToLowerInvariant().EndsWith(".exe"))
                        .ForEachAsync(x => signPEFile(x.FullName, signingOpts))
                        .Wait();
                });

                processed.Add(rp.ReleasePackageFile);

                var prev = ReleaseEntry.GetPreviousRelease(previousReleases, rp, targetDir);
                if (prev != null) {
                    var deltaBuilder = new DeltaPackageBuilder();

                    var dp = deltaBuilder.CreateDeltaPackage(prev, rp,
                        Path.Combine(di.FullName, rp.SuggestedReleaseFileName.Replace("full", "delta")));
                    processed.Insert(0, dp.InputPackageFile);
                }
            }

            foreach (var file in toProcess) { File.Delete(file.FullName); }

            var releaseEntries = previousReleases.Concat(processed.Select(packageFilename => ReleaseEntry.GenerateFromFile(packageFilename, baseUrl)));
            ReleaseEntry.WriteReleaseFile(releaseEntries, releaseFilePath);

            var targetSetupExe = Path.Combine(di.FullName, "Setup.exe");
            var newestFullRelease = releaseEntries.MaxBy(x => x.Version).Where(x => !x.IsDelta).First();

            File.Copy(bootstrapperExe, targetSetupExe, true);
            var zipPath = createSetupEmbeddedZip(Path.Combine(di.FullName, newestFullRelease.Filename), di.FullName, backgroundGif, signingOpts).Result;

            try {
                var zip = File.ReadAllBytes(zipPath);

                IntPtr handle = NativeMethods.BeginUpdateResource(targetSetupExe, false);
                if (handle == IntPtr.Zero) {
                    throw new Win32Exception();
                }

                if (!NativeMethods.UpdateResource(handle, "DATA", new IntPtr(131), 0x0409, zip, zip.Length)) {
                    throw new Win32Exception();
                }

                if (!NativeMethods.EndUpdateResource(handle, false)) {
                    throw new Win32Exception();
                }
            } catch (Exception ex) {
                this.Log().ErrorException("Failed to update Setup.exe with new Zip file", ex);
            } finally {
                File.Delete(zipPath);
            }

            if (signingOpts != null) {
                signPEFile(targetSetupExe, signingOpts).Wait();
            }
        }

        public void Shortcut(string exeName)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            var appName = getAppNameFromDirectory();
            using (var mgr = new UpdateManager("", appName, FrameworkVersion.Net45)) {
                mgr.CreateShortcutsForExecutable(exeName, ShortcutLocation.Desktop | ShortcutLocation.StartMenu, false);
            }
        }

        public void Deshortcut(string exeName)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            var appName = getAppNameFromDirectory();
            using (var mgr = new UpdateManager("", appName, FrameworkVersion.Net45)) {
                mgr.RemoveShortcutsForExecutable(exeName, ShortcutLocation.Desktop | ShortcutLocation.StartMenu);
            }
        }

        public void ProcessStart(string exeName, string arguments)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            waitForParentToExit();

            // Find the latest installed version's app dir
            var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var releases = ReleaseEntry.ParseReleaseFile(
                File.ReadAllText(Utility.LocalReleaseFileForAppDir(appDir), Encoding.UTF8));

            var latestAppDir = releases
                .OrderBy(x => x.Version)
                .Select(x => Utility.AppDirForRelease(appDir, x))
                .FirstOrDefault(x => Directory.Exists(x));

            // Check for the EXE name they want
            var targetExe = new FileInfo(Path.Combine(latestAppDir, exeName));
            this.Log().Info("Want to launch '{0}'", targetExe);

            // Check for path canonicalization attacks
            if (!targetExe.FullName.StartsWith(latestAppDir)) {
                throw new ArgumentException();
            }

            if (!targetExe.Exists) {
                this.Log().Error("File {0} doesn't exist in current release", targetExe);
                throw new ArgumentException();
            }

            try {
                this.Log().Info("About to launch: '{0}': {1}", targetExe.FullName, arguments ?? "");
                Process.Start(new ProcessStartInfo(targetExe.FullName, arguments ?? ""));
            } catch (Exception ex) {
                this.Log().ErrorException("Failed to start process", ex);
            }
        }

        public void ShowHelp()
        {
            ensureConsole();
            opts.WriteOptionDescriptions(Console.Out);
        }

        static void waitForParentToExit()
        {
            // Grab a handle the parent process
            var parentPid = NativeMethods.GetParentProcessId();
            var handle = default(IntPtr);

            // Wait for our parent to exit
            try {
                handle = NativeMethods.OpenProcess(ProcessAccess.Synchronize, false, parentPid);
                if (handle == IntPtr.Zero) throw new Win32Exception();

                NativeMethods.WaitForSingleObject(handle, 0xFFFFFFFF /*INFINITE*/);
            } finally {
                if (handle != IntPtr.Zero) NativeMethods.CloseHandle(handle);
            }
        }

        async Task<string> createSetupEmbeddedZip(string fullPackage, string releasesDir, string backgroundGif, string signingOpts)
        {
            string tempPath;

            this.Log().Info("Building embedded zip file for Setup.exe");
            using (Utility.WithTempDirectory(out tempPath)) {
                this.ErrorIfThrows(() => {
                    File.Copy(Assembly.GetEntryAssembly().Location, Path.Combine(tempPath, "Update.exe"));
                    File.Copy(fullPackage, Path.Combine(tempPath, Path.GetFileName(fullPackage)));
                }, "Failed to write package files to temp dir: " + tempPath);

                if (!String.IsNullOrWhiteSpace(backgroundGif)) {
                    this.ErrorIfThrows(() => {
                        File.Copy(backgroundGif, Path.Combine(tempPath, "background.gif"));
                    }, "Failed to write animated GIF to temp dir: " + tempPath);
                }

                var releases = new[] { ReleaseEntry.GenerateFromFile(fullPackage) };
                ReleaseEntry.WriteReleaseFile(releases, Path.Combine(tempPath, "RELEASES"));

                var target = Path.GetTempFileName();
                File.Delete(target);

                // Sign Update.exe so that virus scanners don't think we're
                // pulling one over on them
                if (signingOpts != null) {
                    var di = new DirectoryInfo(tempPath);

                    var files = di.EnumerateFiles()
                        .Where(x => x.Name.ToLowerInvariant().EndsWith(".exe"))
                        .Select(x => x.FullName);

                    await files.ForEachAsync(x => signPEFile(x, signingOpts));
                }

                this.ErrorIfThrows(() =>
                    ZipFile.CreateFromDirectory(tempPath, target, CompressionLevel.Optimal, false),
                    "Failed to create Zip file from directory: " + tempPath);

                return target;
            }
        }

        static async Task signPEFile(string exePath, string signingOpts)
        {
            // Try to find SignTool.exe
            var exe = @".\signtool.exe";
            if (!File.Exists(exe)) {
                exe = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "signtool.exe");

                // Run down PATH and hope for the best
                if (!File.Exists(exe)) exe = "signtool.exe";
            }

            int exitCode = await Utility.InvokeProcessAsync(exe,
                String.Format("sign {0} {1}", signingOpts, exePath));

            if (exitCode != 0) {
                var msg = String.Format(
                    "Failed to sign, command invoked was: '{0} sign {1} {2}'", 
                    exe, signingOpts, exePath);
                throw new Exception(msg);
            }
        }

        static string getAppNameFromDirectory(string path = null)
        {
            path = path ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return (new DirectoryInfo(path)).Name;
        }

        static int consoleCreated = 0;
        static void ensureConsole()
        {
            if (Interlocked.CompareExchange(ref consoleCreated, 1, 0) == 1) return;

            if (!NativeMethods.AttachConsole(-1)) {
                NativeMethods.AllocConsole();
            }

            NativeMethods.GetStdHandle(StandardHandles.STD_ERROR_HANDLE);
            NativeMethods.GetStdHandle(StandardHandles.STD_OUTPUT_HANDLE);
        }
    }

    class SetupLogLogger : Splat.ILogger, IDisposable
    {
        StreamWriter inner;
        readonly object gate = 42;
        public Splat.LogLevel Level { get; set; }

        public SetupLogLogger(bool saveInTemp)
        {
            var dir = saveInTemp ?
                Path.GetTempPath() : 
                Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            var file = Path.Combine(dir, "SquirrelSetup.log");
            if (File.Exists(file)) File.Delete(file);

            inner = new StreamWriter(file, false, Encoding.UTF8);
        }

        public void Write(string message, Splat.LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            lock (gate) inner.WriteLine(message);
        }

        public void Dispose()
        {
            lock(gate) inner.Dispose();
        }
    }
}