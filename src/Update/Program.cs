﻿using System;
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
        Unset = 0, Install, Uninstall, Download, Update, Releasify,
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

                opts = new OptionSet() {
                    "Usage: Update.exe command [OPTS]",
                    "Manages Squirrel packages",
                    "",
                    "Commands",
                    { "install=", "Install the app whose package is in the specified directory", v => { updateAction = UpdateAction.Install; target = v; } },
                    { "uninstall", "Uninstall the app the same dir as Update.exe", v => updateAction = UpdateAction.Uninstall},
                    { "download=", "Download the releases specified by the URL and write new results to stdout as JSON", v => { updateAction = UpdateAction.Download; target = v; } },
                    { "update=", "Update the application to the latest remote version specified by URL", v => { updateAction = UpdateAction.Update; target = v; } },
                    { "releasify=", "Update or generate a releases directory with a given NuGet package", v => { updateAction = UpdateAction.Releasify; target = v; } },
                    "",
                    "Options:",
                    { "h|?|help", "Display Help and exit", _ => ShowHelp() },
                    { "r=|releaseDir=", "Path to a release directory to use with releasify", v => releaseDir = v},
                    { "p=|packagesDir=", "Path to the NuGet Packages directory for C# apps", v => packagesDir = v},
                    { "bootstrapperExe=", "Path to the Setup.exe to use as a template", v => bootstrapperExe = v},
                    { "g=|loadingGif=", "Path to an animated GIF to be displayed during installation", v => backgroundGif = v},
                    { "s|silent", "Silent install", _ => silentInstall = true},
                };

                opts.Parse(args);

                if (updateAction == UpdateAction.Unset) {
                    ShowHelp();
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
                case UpdateAction.Releasify:
                    Releasify(target, releaseDir, packagesDir, bootstrapperExe, backgroundGif);
                    break;
                }
            
                animatedGifWindowToken.Cancel();
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
            }
            
            // TODO: Update our installer entry
        }

        public async Task<string> Download(string updateUrl, string appName = null)
        {
            ensureConsole();
            appName = appName ?? getAppNameFromDirectory();

            this.Log().Info("Fetching update information, downloading from " + updateUrl);
            using (var mgr = new UpdateManager(updateUrl, appName, FrameworkVersion.Net45)) {
                var updateInfo = await mgr.CheckForUpdate(progress: x => Console.WriteLine(x / 3));
                await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(33 + x / 3));

                return SimpleJson.SerializeObject(updateInfo);
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

        public void Releasify(string package, string targetDir = null, string packagesDir = null, string bootstrapperExe = null, string backgroundGif = null)
        {
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

            var releaseFilePath = Path.Combine(di.FullName, "RELEASES");
            var previousReleases = Enumerable.Empty<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                previousReleases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8));
            }

            foreach (var file in toProcess) {
                this.Log().Info("Creating release package: " + file.FullName);

                var rp = new ReleasePackage(file.FullName);
                rp.CreateReleasePackage(Path.Combine(di.FullName, rp.SuggestedReleaseFileName), packagesDir);

                var prev = ReleaseEntry.GetPreviousRelease(previousReleases, rp, targetDir);
                if (prev != null) {
                    var deltaBuilder = new DeltaPackageBuilder();

                    deltaBuilder.CreateDeltaPackage(prev, rp,
                        Path.Combine(di.FullName, rp.SuggestedReleaseFileName.Replace("full", "delta")));
                }
            }

            foreach (var file in toProcess) { File.Delete(file.FullName); }

            var releaseEntries = allNuGetFiles.Select(x => ReleaseEntry.GenerateFromFile(x.FullName));
            ReleaseEntry.WriteReleaseFile(releaseEntries, releaseFilePath);

            var targetSetupExe = Path.Combine(di.FullName, "Setup.exe");
            var newestFullRelease = releaseEntries.MaxBy(x => x.Version).Where(x => !x.IsDelta).First();

            File.Copy(bootstrapperExe, targetSetupExe, true);
            var zipPath = createSetupEmbeddedZip(Path.Combine(di.FullName, newestFullRelease.Filename), di.FullName, backgroundGif);

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
        }


        public void ShowHelp()
        {
            ensureConsole();
            opts.WriteOptionDescriptions(Console.Out);
        }

        string createSetupEmbeddedZip(string fullPackage, string releasesDir, string backgroundGif)
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

                this.ErrorIfThrows(() =>
                    ZipFile.CreateFromDirectory(tempPath, target, CompressionLevel.Optimal, false),
                    "Failed to create Zip file from directory: " + tempPath);

                return target;
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

    enum StandardHandles : int {
        STD_INPUT_HANDLE = -10,
        STD_OUTPUT_HANDLE = -11,
        STD_ERROR_HANDLE = -12,
    }

    static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "GetStdHandle")]
        public static extern IntPtr GetStdHandle(StandardHandles nStdHandle);

        [DllImport("kernel32.dll", EntryPoint = "AllocConsole")]
        [return: MarshalAs(UnmanagedType.Bool)] 
        public static extern bool AllocConsole();
 
        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int pid);

        [DllImport("Kernel32.dll", SetLastError=true)]
        public static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

        [DllImport("Kernel32.dll", SetLastError=true)]
        public static extern bool UpdateResource(IntPtr handle, string pType, IntPtr pName, short language, [MarshalAs(UnmanagedType.LPArray)] byte[] pData, int dwSize);

        [DllImport("Kernel32.dll", SetLastError=true)]
        public static extern bool EndUpdateResource(IntPtr handle, bool discard);
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