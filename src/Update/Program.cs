using NuGet;
using Splat;
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

namespace Squirrel.Update
{
    enum UpdateAction {
        Unset = 0, Install, Uninstall, Download, Update, Releasify, Shortcut,
        Deshortcut, ProcessStart, UpdateSelf, CheckForUpdate
    }

    class Program : IEnableLogger
    {
        static StartupOption opt;

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
            try {
                opt = new StartupOption(args);
            } catch (Exception ex) {
                using (var logger = new SetupLogLogger(true, "OptionParsing") { Level = LogLevel.Info }) {
                    Locator.CurrentMutable.Register(() => logger, typeof(Splat.ILogger));
                    logger.Write($"Failed to parse command line options. {ex.Message}", LogLevel.Error);
                }
                throw;
            }

            // NB: Trying to delete the app directory while we have Setup.log
            // open will actually crash the uninstaller
            bool isUninstalling = opt.updateAction == UpdateAction.Uninstall;

            using (var logger = new SetupLogLogger(isUninstalling, opt.updateAction.ToString()) {Level = LogLevel.Info}) {
                Locator.CurrentMutable.Register(() => logger, typeof (Splat.ILogger));

                try {
                    return executeCommandLine(args);
                } catch (Exception ex) {
                    logger.Write("Finished with unhandled exception: " + ex, LogLevel.Fatal);
                    throw;
                }
            }
        }

        int executeCommandLine(string[] args)
        {
            var animatedGifWindowToken = new CancellationTokenSource();

#if !MONO
            // Uncomment to test Gifs
            /*
            var ps = new ProgressSource();
            int i = 0; var t = new Timer(_ => ps.Raise(i += 10), null, 0, 1000);
            AnimatedGifWindow.ShowWindow(TimeSpan.FromMilliseconds(0), animatedGifWindowToken.Token, ps);
            Thread.Sleep(10 * 60 * 1000);
            */
#endif

            using (Disposable.Create(() => animatedGifWindowToken.Cancel())) {

                this.Log().Info("Starting Squirrel Updater: " + String.Join(" ", args));

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
#if !MONO
                case UpdateAction.Install:
                    var progressSource = new ProgressSource();
                    if (!opt.silentInstall) {
                        AnimatedGifWindow.ShowWindow(TimeSpan.FromSeconds(4), animatedGifWindowToken.Token, progressSource);
                    }

                    Install(opt.silentInstall, progressSource, Path.GetFullPath(opt.target)).Wait();
                    animatedGifWindowToken.Cancel();
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
                    Shortcut(opt.target, opt.shortcutArgs, opt.processStartArgs, opt.setupIcon);
                    break;
                case UpdateAction.Deshortcut:
                    Deshortcut(opt.target, opt.shortcutArgs);
                    break;
                case UpdateAction.ProcessStart:
                    ProcessStart(opt.processStart, opt.processStartArgs, opt.shouldWait);
                    break;
#endif
                case UpdateAction.Releasify:
                    Releasify(opt.target, opt.releaseDir, opt.packagesDir, opt.bootstrapperExe, opt.backgroundGif, opt.signingParameters, opt.baseUrl, opt.setupIcon, !opt.noMsi, opt.packageAs64Bit, opt.frameworkVersion, !opt.noDelta);
                    break;
                }
            }
            this.Log().Info("Finished Squirrel Updater");
            return 0;
        }

        public async Task Install(bool silentInstall, ProgressSource progressSource, string sourceDirectory = null)
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

            using (var mgr = new UpdateManager(sourceDirectory, ourAppName)) {
                this.Log().Info("About to install to: " + mgr.RootAppDirectory);
                if (Directory.Exists(mgr.RootAppDirectory)) {
                    this.Log().Warn("Install path {0} already exists, burning it to the ground", mgr.RootAppDirectory);

                    mgr.KillAllExecutablesBelongingToPackage();
                    await Task.Delay(500);

                    await this.ErrorIfThrows(() => Utility.DeleteDirectory(mgr.RootAppDirectory),
                        "Failed to remove existing directory on full install, is the app still running???");

                    this.ErrorIfThrows(() => Utility.Retry(() => Directory.CreateDirectory(mgr.RootAppDirectory), 3),
                        "Couldn't recreate app directory, perhaps Antivirus is blocking it");
                }

                Directory.CreateDirectory(mgr.RootAppDirectory);

                var updateTarget = Path.Combine(mgr.RootAppDirectory, "Update.exe");
                this.ErrorIfThrows(() => File.Copy(Assembly.GetExecutingAssembly().Location, updateTarget, true),
                    "Failed to copy Update.exe to " + updateTarget);

                await mgr.FullInstall(silentInstall, progressSource.Raise);

                await this.ErrorIfThrows(() => mgr.CreateUninstallerRegistryEntry(),
                    "Failed to create uninstaller registry entry");
            }
        }

        public async Task Update(string updateUrl, string appName = null)
        {
            appName = appName ?? getAppNameFromDirectory();

            this.Log().Info("Starting update, downloading from " + updateUrl);

            using (var mgr = new UpdateManager(updateUrl, appName)) {
                bool ignoreDeltaUpdates = false;
                this.Log().Info("About to update to: " + mgr.RootAppDirectory);

            retry:
                try {
                    var updateInfo = await mgr.CheckForUpdate(intention: UpdaterIntention.Update, ignoreDeltaUpdates: ignoreDeltaUpdates, progress: x => Console.WriteLine(x / 3));
                    await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(33 + x / 3));
                    await mgr.ApplyReleases(updateInfo, x => Console.WriteLine(66 + x / 3));
                } catch (Exception ex) {
                    if (ignoreDeltaUpdates) {
                        this.Log().ErrorException("Really couldn't apply updates!", ex);
                        throw;
                    }

                    this.Log().WarnException("Failed to apply updates, falling back to full updates", ex);
                    ignoreDeltaUpdates = true;
                    goto retry;
                }

                var updateTarget = Path.Combine(mgr.RootAppDirectory, "Update.exe");

                await this.ErrorIfThrows(() =>
                    mgr.CreateUninstallerRegistryEntry(),
                    "Failed to create uninstaller registry entry");
            }
        }

        public async Task UpdateSelf()
        {
            waitForParentToExit();
            var src = Assembly.GetExecutingAssembly().Location;
            var updateDotExeForOurPackage = Path.Combine(
                Path.GetDirectoryName(src),
                "..", "Update.exe");

            await Task.Run(() => {
                File.Copy(src, updateDotExeForOurPackage, true);
            });
        }

        public async Task<string> Download(string updateUrl, string appName = null)
        {
            appName = appName ?? getAppNameFromDirectory();

            this.Log().Info("Fetching update information, downloading from " + updateUrl);
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

        public async Task<string> CheckForUpdate(string updateUrl, string appName = null)
        {
            appName = appName ?? getAppNameFromDirectory();

            this.Log().Info("Fetching update information, downloading from " + updateUrl);
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

        public async Task Uninstall(string appName = null)
        {
            this.Log().Info("Starting uninstall for app: " + appName);

            appName = appName ?? getAppNameFromDirectory();
            using (var mgr = new UpdateManager("", appName)) {
                await mgr.FullUninstall();
                mgr.RemoveUninstallerRegistryEntry();
            }
        }

        public void Releasify(string package, string targetDir = null, string packagesDir = null, string bootstrapperExe = null, string backgroundGif = null, string signingOpts = null, string baseUrl = null, string setupIcon = null, bool generateMsi = true, bool packageAs64Bit = false, string frameworkVersion = null, bool generateDeltas = true)
        {
            ensureConsole();

            if (baseUrl != null) {
                if (!Utility.IsHttpUrl(baseUrl)) {
                    throw new Exception(string.Format("Invalid --baseUrl '{0}'. A base URL must start with http or https and be a valid URI.", baseUrl));
                }

                if (!baseUrl.EndsWith("/")) {
                    baseUrl += "/";
                }
            }

            targetDir = targetDir ?? Path.Combine(".", "Releases");
            packagesDir = packagesDir ?? ".";
            bootstrapperExe = bootstrapperExe ?? Path.Combine(".", "Setup.exe");

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
            var previousReleases = new List<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                previousReleases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));
            }

            foreach (var file in toProcess) {
                this.Log().Info("Creating release package: " + file.FullName);

                var rp = new ReleasePackage(file.FullName);
                rp.CreateReleasePackage(Path.Combine(di.FullName, rp.SuggestedReleaseFileName), packagesDir, contentsPostProcessHook: pkgPath => {
                    new DirectoryInfo(pkgPath)
                                    .GetAllFilesRecursively("*.exe")
                                    .Where(x => !x.Name.ToLowerInvariant().Contains("squirrel.exe"))
                                    .Where(x => Utility.IsFileTopLevelInPackage(x.FullName, pkgPath))
                                    .Where(x => Utility.ExecutableUsesWin32Subsystem(x.FullName))
                                    .ToList()
                                    .ForEachAsync(x => createExecutableStubForExe(x.FullName))
                                    .Wait();

                    if (signingOpts == null) return;

                    new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                        .Where(x => Utility.FileIsLikelyPEImage(x.Name))
                        .ForEachAsync(async x => {
                            if (isPEFileSigned(x.FullName)) {
                                this.Log().Info("{0} is already signed, skipping", x.FullName);
                                return;
                            }

                            this.Log().Info("About to sign {0}", x.FullName);
                            await signPEFile(x.FullName, signingOpts);
                        }, 1)
                        .Wait();
                });

                processed.Add(rp.ReleasePackageFile);

                var prev = ReleaseEntry.GetPreviousRelease(previousReleases, rp, targetDir);
                if (prev != null && generateDeltas) {
                    var deltaBuilder = new DeltaPackageBuilder(null);

                    var dp = deltaBuilder.CreateDeltaPackage(prev, rp,
                        Path.Combine(di.FullName, rp.SuggestedReleaseFileName.Replace("full", "delta")));
                    processed.Insert(0, dp.InputPackageFile);
                }
            }

            foreach (var file in toProcess) { File.Delete(file.FullName); }

            var newReleaseEntries = processed
                .Select(packageFilename => ReleaseEntry.GenerateFromFile(packageFilename, baseUrl))
                .ToList();
            var distinctPreviousReleases = previousReleases
                .Where(x => !newReleaseEntries.Select(e => e.Version).Contains(x.Version));
            var releaseEntries = distinctPreviousReleases.Concat(newReleaseEntries).ToList();

            ReleaseEntry.WriteReleaseFile(releaseEntries, releaseFilePath);

            var targetSetupExe = Path.Combine(di.FullName, "Setup.exe");
            var newestFullRelease = releaseEntries.MaxBy(x => x.Version).Where(x => !x.IsDelta).First();

            File.Copy(bootstrapperExe, targetSetupExe, true);
            var zipPath = createSetupEmbeddedZip(Path.Combine(di.FullName, newestFullRelease.Filename), di.FullName, backgroundGif, signingOpts, setupIcon).Result;

            var writeZipToSetup = Utility.FindHelperExecutable("WriteZipToSetup.exe");

            try {
                var arguments = String.Format("\"{0}\" \"{1}\" \"--set-required-framework\" \"{2}\"", targetSetupExe, zipPath, frameworkVersion);
                var result = Utility.InvokeProcessAsync(writeZipToSetup, arguments, CancellationToken.None).Result;
                if (result.Item1 != 0) throw new Exception("Failed to write Zip to Setup.exe!\n\n" + result.Item2);
            } catch (Exception ex) {
                this.Log().ErrorException("Failed to update Setup.exe with new Zip file", ex);
            } finally {
                File.Delete(zipPath);
            }

            Utility.Retry(() =>
                setPEVersionInfoAndIcon(targetSetupExe, new ZipPackage(package), setupIcon).Wait());

            if (signingOpts != null) {
                signPEFile(targetSetupExe, signingOpts).Wait();
            }

            if (generateMsi) {
                createMsiPackage(targetSetupExe, new ZipPackage(package), packageAs64Bit).Wait();

                if (signingOpts != null) {
                    signPEFile(targetSetupExe.Replace(".exe", ".msi"), signingOpts).Wait();
                }
            }
        }

        public void Shortcut(string exeName, string shortcutArgs, string processStartArgs, string icon)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            var appName = getAppNameFromDirectory();
            var defaultLocations = ShortcutLocation.StartMenu | ShortcutLocation.Desktop;
            var locations = parseShortcutLocations(shortcutArgs);

            using (var mgr = new UpdateManager("", appName)) {
                mgr.CreateShortcutsForExecutable(exeName, locations ?? defaultLocations, false, processStartArgs, icon);
            }
        }

        public void Deshortcut(string exeName, string shortcutArgs)
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

        public void ProcessStart(string exeName, string arguments, bool shouldWait)
        {
            if (String.IsNullOrWhiteSpace(exeName)) {
                ShowHelp();
                return;
            }

            // Find the latest installed version's app dir
            var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
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
            this.Log().Info("Want to launch '{0}'", targetExe);

            // Check for path canonicalization attacks
            if (!targetExe.FullName.StartsWith(latestAppDir, StringComparison.Ordinal)) {
                throw new ArgumentException();
            }

            if (!targetExe.Exists) {
                this.Log().Error("File {0} doesn't exist in current release", targetExe);
                throw new ArgumentException();
            }

            if (shouldWait) waitForParentToExit();

            try {
                this.Log().Info("About to launch: '{0}': {1}", targetExe.FullName, arguments ?? "");
                Process.Start(new ProcessStartInfo(targetExe.FullName, arguments ?? "") { WorkingDirectory = Path.GetDirectoryName(targetExe.FullName) });
            } catch (Exception ex) {
                this.Log().ErrorException("Failed to start process", ex);
            }
        }

        public void ShowHelp()
        {
            ensureConsole();
            opt.WriteOptionDescriptions();
        }

        void waitForParentToExit()
        {
            // Grab a handle the parent process
            var parentPid = NativeMethods.GetParentProcessId();
            var handle = default(IntPtr);

            // Wait for our parent to exit
            try {
                handle = NativeMethods.OpenProcess(ProcessAccess.Synchronize, false, parentPid);
                if (handle != IntPtr.Zero) {
                    this.Log().Info("About to wait for parent PID {0}", parentPid);
                    NativeMethods.WaitForSingleObject(handle, 0xFFFFFFFF /*INFINITE*/);
                } else {
                    this.Log().Info("Parent PID {0} no longer valid - ignoring", parentPid);
                }
            } finally {
                if (handle != IntPtr.Zero) NativeMethods.CloseHandle(handle);
            }
        }

        async Task<string> createSetupEmbeddedZip(string fullPackage, string releasesDir, string backgroundGif, string signingOpts, string setupIcon)
        {
            string tempPath;

            this.Log().Info("Building embedded zip file for Setup.exe");
            using (Utility.WithTempDirectory(out tempPath, null)) {
                this.ErrorIfThrows(() => {
                    File.Copy(Assembly.GetEntryAssembly().Location.Replace("-Mono.exe", ".exe"), Path.Combine(tempPath, "Update.exe"));
                    File.Copy(fullPackage, Path.Combine(tempPath, Path.GetFileName(fullPackage)));
                }, "Failed to write package files to temp dir: " + tempPath);

                if (!String.IsNullOrWhiteSpace(backgroundGif)) {
                    this.ErrorIfThrows(() => {
                        File.Copy(backgroundGif, Path.Combine(tempPath, "background.gif"));
                    }, "Failed to write animated GIF to temp dir: " + tempPath);
                }

                if (!String.IsNullOrWhiteSpace(setupIcon)) {
                    this.ErrorIfThrows(() => {
                        File.Copy(setupIcon, Path.Combine(tempPath, "setupIcon.ico"));
                    }, "Failed to write icon to temp dir: " + tempPath);
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

            var processResult = await Utility.InvokeProcessAsync(exe,
                String.Format("sign {0} \"{1}\"", signingOpts, exePath), CancellationToken.None);

            if (processResult.Item1 != 0) {
                var optsWithPasswordHidden = new Regex(@"/p\s+\w+").Replace(signingOpts, "/p ********");
                var msg = String.Format("Failed to sign, command invoked was: '{0} sign {1} {2}'",
                    exe, optsWithPasswordHidden, exePath);

                throw new Exception(msg);
            } else {
                Console.WriteLine(processResult.Item2);
            }
        }
        bool isPEFileSigned(string path)
        {
#if MONO
            return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
#else
            try {
                return AuthenticodeTools.IsTrusted(path);
            } catch (Exception ex) {
                this.Log().ErrorException("Failed to determine signing status for " + path, ex);
                return false;
            }
#endif
        }

        async Task createExecutableStubForExe(string fullName)
        {
            var exe = Utility.FindHelperExecutable(@"StubExecutable.exe");

            var target = Path.Combine(
                Path.GetDirectoryName(fullName),
                Path.GetFileNameWithoutExtension(fullName) + "_ExecutionStub.exe");

            await Utility.CopyToAsync(exe, target);

            await Utility.InvokeProcessAsync(
                Utility.FindHelperExecutable("WriteZipToSetup.exe"),
                String.Format("--copy-stub-resources \"{0}\" \"{1}\"", fullName, target),
                CancellationToken.None);
        }

        static async Task setPEVersionInfoAndIcon(string exePath, IPackage package, string iconPath = null)
        {
            var realExePath = Path.GetFullPath(exePath);
            var company = String.Join(",", package.Authors);
            var verStrings = new Dictionary<string, string>() {
                { "CompanyName", company },
                { "LegalCopyright", package.Copyright ?? "Copyright © " + DateTime.Now.Year.ToString() + " " + company },
                { "FileDescription", package.Summary ?? package.Description ?? "Installer for " + package.Id },
                { "ProductName", package.Description ?? package.Summary ?? package.Id },
            };

            var args = verStrings.Aggregate(new StringBuilder("\"" + realExePath + "\""), (acc, x) => { acc.AppendFormat(" --set-version-string \"{0}\" \"{1}\"", x.Key, x.Value); return acc; });
            args.AppendFormat(" --set-file-version {0} --set-product-version {0}", package.Version.ToString());
            if (iconPath != null) {
                args.AppendFormat(" --set-icon \"{0}\"", Path.GetFullPath(iconPath));
            }

            // Try to find rcedit.exe
            string exe = Utility.FindHelperExecutable("rcedit.exe");

            var processResult = await Utility.InvokeProcessAsync(exe, args.ToString(), CancellationToken.None);

            if (processResult.Item1 != 0) {
                var msg = String.Format(
                    "Failed to modify resources, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
                    exe, args, processResult.Item2);

                throw new Exception(msg);
            } else {
                Console.WriteLine(processResult.Item2);
            }
        }

        static async Task createMsiPackage(string setupExe, IPackage package, bool packageAs64Bit)
        {
            var pathToWix = pathToWixTools();
            var setupExeDir = Path.GetDirectoryName(setupExe);
            var company = String.Join(",", package.Authors);

            var culture = CultureInfo.GetCultureInfo(package.Language ?? "").TextInfo.ANSICodePage;


            var templateText = File.ReadAllText(Path.Combine(pathToWix, "template.wxs"));
            var templateData = new Dictionary<string, string> {
                { "Id", package.Id },
                { "Title", package.Title },
                { "Author", company },
                { "Version", Regex.Replace(package.Version.ToString(), @"-.*$", "") },
                { "Summary", package.Summary ?? package.Description ?? package.Id },
                { "Codepage", $"{culture}" },
                { "Platform", packageAs64Bit ? "x64" : "x86" },
                { "ProgramFilesFolder", packageAs64Bit ? "ProgramFiles64Folder" : "ProgramFilesFolder" },
                { "Win64YesNo", packageAs64Bit ? "yes" : "no" }
            };

            // NB: We need some GUIDs that are based on the package ID, but unique (i.e.
            // "Unique but consistent").
            for (int i=1; i <= 10; i++) {
                templateData[String.Format("IdAsGuid{0}", i)] = Utility.CreateGuidFromHash(String.Format("{0}:{1}", package.Id, i)).ToString();
            }

            var templateResult = CopStache.Render(templateText, templateData);

            var wxsTarget = Path.Combine(setupExeDir, "Setup.wxs");
            File.WriteAllText(wxsTarget, templateResult, Encoding.UTF8);

            var candleParams = String.Format("-nologo -ext WixNetFxExtension -out \"{0}\" \"{1}\"", wxsTarget.Replace(".wxs", ".wixobj"), wxsTarget);
            var processResult = await Utility.InvokeProcessAsync(
                Path.Combine(pathToWix, "candle.exe"), candleParams, CancellationToken.None, setupExeDir);

            if (processResult.Item1 != 0) {
                var msg = String.Format(
                    "Failed to compile WiX template, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
                    "candle.exe", candleParams, processResult.Item2);

                throw new Exception(msg);
            }

            var lightParams = String.Format("-ext WixNetFxExtension -sval -out \"{0}\" \"{1}\"", wxsTarget.Replace(".wxs", ".msi"), wxsTarget.Replace(".wxs", ".wixobj"));
            processResult = await Utility.InvokeProcessAsync(
                Path.Combine(pathToWix, "light.exe"), lightParams, CancellationToken.None, setupExeDir);

            if (processResult.Item1 != 0) {
                var msg = String.Format(
                    "Failed to link WiX template, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
                    "light.exe", lightParams, processResult.Item2);

                throw new Exception(msg);
            }

            var toDelete = new[] {
                wxsTarget,
                wxsTarget.Replace(".wxs", ".wixobj"),
                wxsTarget.Replace(".wxs", ".wixpdb"),
            };

            await Utility.ForEachAsync(toDelete, x => Utility.DeleteFileHarder(x));
        }

        static string pathToWixTools()
        {
            var ourPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Same Directory? (i.e. released)
            if (File.Exists(Path.Combine(ourPath, "candle.exe"))) {
                return ourPath;
            }

            // Debug Mode (i.e. in vendor)
            var debugPath = Path.Combine(ourPath, "..", "..", "..", "vendor", "wix", "candle.exe");
            if (File.Exists(debugPath)) {
                return Path.GetFullPath(debugPath);
            }

            throw new Exception("WiX tools can't be found");
        }

        static string getAppNameFromDirectory(string path = null)
        {
            path = path ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return (new DirectoryInfo(path)).Name;
        }

        static ShortcutLocation? parseShortcutLocations(string shortcutArgs)
        {
            var ret = default(ShortcutLocation?);

            if (!String.IsNullOrWhiteSpace(shortcutArgs)) {
                var args = shortcutArgs.Split(new[] { ',' });

                foreach (var arg in args) {
                    var location = (ShortcutLocation)(Enum.Parse(typeof(ShortcutLocation), arg, false));
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

    class SetupLogLogger : Splat.ILogger, IDisposable
    {
        TextWriter inner;
        readonly object gate = 42;
        public Splat.LogLevel Level { get; set; }

        public SetupLogLogger(bool saveInTemp, string commandSuffix = null)
        {
            for (int i=0; i < 10; i++) {
                try {
                    var dir = saveInTemp ?
                        Path.GetTempPath() :
                        Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
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
