using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using Mono.Options;
using Squirrel;
using Squirrel.Json;
using Squirrel.Lib;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;
using SquirrelCli.Sources;

namespace SquirrelCli
{
    class Program : IEnableLogger
    {
        public static int Main(string[] args)
        {
            //var pg = new Program();
            var commands = new CommandSet {
                { "releasify", "Take an existing nuget package and turn it into a Squirrel release", new ReleasifyOptions(), Releasify },
                { "pack", "Creates a nuget package from a folder and releasifies it in a single step", new PackOptions(), Pack },
                { "b2-down", "Download recent releases from BackBlaze B2", new SyncBackblazeOptions(), o => new BackblazeRepository(o).DownloadRecentPackages().Wait() },
                { "b2-up", "Upload releases to BackBlaze B2", new SyncBackblazeOptions(), o => new BackblazeRepository(o).UploadMissingPackages().Wait() },
                { "http-down", "Download recent releases from an HTTP source", new SyncHttpOptions(), o => new SimpleWebRepository(o).DownloadRecentPackages().Wait() },
                //{ "http-up", "sync", new SyncHttpOptions(), o => new SimpleWebRepository(o).UploadMissingPackages().Wait() },
                { "github-down", "Download recent releases from GitHub", new SyncGithubOptions(), o => new GitHubRepository(o).DownloadRecentPackages().Wait() },
                //{ "github-up", "sync", new SyncGithubOptions(), o => new GitHubRepository(o).UploadMissingPackages().Wait() },
            };

            var logger = new ConsoleLogger();

            try {
                // check for help argument
                bool help = false;
                new OptionSet() { { "h|?|help", _ => help = true }, }.Parse(args);
                if (help) {
                    commands.WriteHelp();
                    return -1;
                } else {
                    // parse cli and run command
                    SquirrelLocator.CurrentMutable.Register(() => logger, typeof(Squirrel.SimpleSplat.ILogger));
                    commands.Execute(args);
                }
                return 0;
            } catch (Exception ex) {
                Console.WriteLine();
                logger.Write(ex.ToString(), LogLevel.Error);
                Console.WriteLine();
                commands.WriteHelp();
                return -1;
            }
        }

        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        static string[] VendorDirs => new string[] {
            Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor")
        };
        static string BootstrapperPath => Utility.FindHelperExecutable("Setup.exe", throwWhenNotFound: true);
        static string UpdatePath => Utility.FindHelperExecutable("Update.exe", throwWhenNotFound: true);
        static string NugetPath => Utility.FindHelperExecutable("NuGet.exe", VendorDirs, throwWhenNotFound: true);

        static void Pack(PackOptions options)
        {
            using (Utility.WithTempDirectory(out var tmpDir)) {
                string nuspec = $@"
<?xml version=""1.0"" encoding=""utf-8""?>
<package>
  <metadata>
    <id>{options.packName}</id>
    <title>{options.packName}</title>
    <description>{options.packName}</description>
    <authors>{options.packAuthors}</authors>
    <version>0</version>
  </metadata>
  <files>
    <file src=""**"" target=""lib\app\"" exclude=""*.pdb;*.nupkg;*.vshost.*""/>
  </files>
</package>
".Trim();
                var nuspecPath = Path.Combine(tmpDir, options.packName + ".nuspec");
                File.WriteAllText(nuspecPath, nuspec);

                var args = $"pack \"{nuspecPath}\" -BasePath \"{options.packDirectory}\" -OutputDirectory \"{tmpDir}\" -Version {options.packVersion}";

                Log.Info($"Packing '{options.packDirectory}' into nupkg.");
                var res = Utility.InvokeProcessAsync(NugetPath, args, CancellationToken.None).Result;

                if (res.Item1 != 0)
                    throw new Exception($"Failed nuget pack (exit {res.Item1}): \r\n " + res.Item2);

                var nupkgPath = Directory.EnumerateFiles(tmpDir).Where(f => f.EndsWith(".nupkg")).FirstOrDefault();
                if (nupkgPath == null)
                    throw new Exception($"Failed to generate nupkg, unspecified error");

                options.package = nupkgPath;
                Releasify(options);
            }
        }

        static void Releasify(ReleasifyOptions options)
        {
            var targetDir = options.releaseDir ?? Path.Combine(".", "Releases");
            if (!Directory.Exists(targetDir)) {
                Directory.CreateDirectory(targetDir);
            }

            var frameworkVersion = options.framework;
            var signingOpts = options.signParams;
            var package = options.package;
            var baseUrl = options.baseUrl;
            var generateDeltas = !options.noDelta;
            var backgroundGif = options.splashImage;
            var setupIcon = options.iconPath;

            // validate that the provided "frameworkVersion" is supported by Setup.exe
            if (!String.IsNullOrWhiteSpace(frameworkVersion)) {
                var chkFrameworkResult = Utility.InvokeProcessAsync(BootstrapperPath, "--checkFramework " + frameworkVersion, CancellationToken.None).Result;
                if (chkFrameworkResult.Item1 != 0) {
                    throw new ArgumentException($"Unsupported FrameworkVersion: '{frameworkVersion}'. {chkFrameworkResult.Item2}");
                }
            }

            // copy input package to target output directory
            if (!package.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("package must be packed with nuget and end in '.nupkg'");
            var di = new DirectoryInfo(targetDir);
            File.Copy(package, Path.Combine(di.FullName, Path.GetFileName(package)), true);

            var allNuGetFiles = di.EnumerateFiles()
                .Where(x => x.Name.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase));

            var toProcess = allNuGetFiles.Where(x => !x.Name.Contains("-delta") && !x.Name.Contains("-full"));
            var processed = new List<string>();

            var releaseFilePath = Path.Combine(di.FullName, "RELEASES");
            var previousReleases = new List<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                previousReleases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));
            }

            foreach (var file in toProcess) {
                Log.Info("Creating release package: " + file.FullName);

                var rp = new ReleasePackage(file.FullName);
                rp.CreateReleasePackage(Path.Combine(di.FullName, rp.SuggestedReleaseFileName), contentsPostProcessHook: pkgPath => {

                    // create sub executable for all exe's in this package (except Squirrel!)
                    new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                        .Where(x => x.Name.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase))
                        .Where(x => !x.Name.Contains("squirrel.exe", StringComparison.InvariantCultureIgnoreCase))
                        .Where(x => Utility.IsFileTopLevelInPackage(x.FullName, pkgPath))
                        .Where(x => Utility.ExecutableUsesWin32Subsystem(x.FullName))
                        .ForEachAsync(x => createExecutableStubForExe(x.FullName))
                        .Wait();

                    // copy myself into the package so Squirrel can also be updated
                    // how we find the lib dir is a huge hack here, but 'ReleasePackage' verifies there can only be one of these so it should be fine.
                    var re = new Regex(@"lib[\\\/][^\\\/]*[\\\/]?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                    var libDir = Directory
                        .EnumerateDirectories(pkgPath, "*", SearchOption.AllDirectories)
                        .Where(d => re.IsMatch(d))
                        .OrderBy(d => d.Length)
                        .FirstOrDefault();
                    File.Copy(UpdatePath, Path.Combine(libDir, "Squirrel.exe"));

                    // sign all exe's in this package
                    if (signingOpts == null) return;
                    new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                        .Where(x => Utility.FileIsLikelyPEImage(x.Name))
                        .ForEachAsync(async x => {
                            if (isPEFileSigned(x.FullName)) {
                                Log.Info("{0} is already signed, skipping", x.FullName);
                                return;
                            }

                            Log.Info("About to sign {0}", x.FullName);
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
            var newestFullRelease = Squirrel.EnumerableExtensions.MaxBy(releaseEntries, x => x.Version).Where(x => !x.IsDelta).First();

            File.Copy(BootstrapperPath, targetSetupExe, true);
            var zipPath = createSetupEmbeddedZip(Path.Combine(di.FullName, newestFullRelease.Filename), di.FullName, signingOpts, setupIcon).Result;

            var writeZipToSetup = Utility.FindHelperExecutable("WriteZipToSetup.exe");

            try {
                string arguments = $"\"{targetSetupExe}\" \"{zipPath}\"";
                if (!String.IsNullOrWhiteSpace(frameworkVersion)) {
                    arguments += $" --set-required-framework \"{frameworkVersion}\"";
                }
                if (!String.IsNullOrWhiteSpace(backgroundGif)) {
                    arguments += $" --set-splash \"{Path.GetFullPath(backgroundGif)}\"";
                }

                var result = Utility.InvokeProcessAsync(writeZipToSetup, arguments, CancellationToken.None).Result;
                if (result.Item1 != 0) throw new Exception("Failed to write Zip to Setup.exe!\n\n" + result.Item2);
            } catch (Exception ex) {
                Log.ErrorException("Failed to update Setup.exe with new Zip file", ex);
                throw;
            } finally {
                File.Delete(zipPath);
            }

            Utility.Retry(() =>
                setPEVersionInfoAndIcon(targetSetupExe, new ZipPackage(package), setupIcon).Wait());

            if (signingOpts != null) {
                signPEFile(targetSetupExe, signingOpts).Wait();
            }

            //if (generateMsi) {
            //    createMsiPackage(targetSetupExe, new ZipPackage(package), packageAs64Bit).Wait();

            //    if (signingOpts != null) {
            //        signPEFile(targetSetupExe.Replace(".exe", ".msi"), signingOpts).Wait();
            //    }
            //}
        }

        static async Task<string> createSetupEmbeddedZip(string fullPackage, string releasesDir, string signingOpts, string setupIcon)
        {
            string tempPath;

            Log.Info("Building embedded zip file for Setup.exe");
            using (Utility.WithTempDirectory(out tempPath, null)) {
                Log.ErrorIfThrows(() => {
                    File.Copy(UpdatePath, Path.Combine(tempPath, "Update.exe"));
                    File.Copy(fullPackage, Path.Combine(tempPath, Path.GetFileName(fullPackage)));
                }, "Failed to write package files to temp dir: " + tempPath);

                if (!String.IsNullOrWhiteSpace(setupIcon)) {
                    Log.ErrorIfThrows(() => {
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
                        .Where(x => x.Name.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase))
                        .Select(x => x.FullName);

                    await files.ForEachAsync(x => signPEFile(x, signingOpts));
                }

                Log.ErrorIfThrows(() =>
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
                exe = Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "signtool.exe");

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
        static bool isPEFileSigned(string path)
        {
            try {
                return AuthenticodeTools.IsTrusted(path);
            } catch (Exception ex) {
                Log.ErrorException("Failed to determine signing status for " + path, ex);
                return false;
            }
        }

        static async Task createExecutableStubForExe(string fullName)
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
    }

    class ConsoleLogger : Squirrel.SimpleSplat.ILogger
    {
        readonly object gate = 42;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            lock (gate) {
                string lvl = logLevel.ToString().Substring(0, 4).ToUpper();
                if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal) {
                    Utility.ConsoleWriteWithColor($"[{lvl}] {message}\r\n", ConsoleColor.Red);
                } else if (logLevel == LogLevel.Warn) {
                    Utility.ConsoleWriteWithColor($"[{lvl}] {message}\r\n", ConsoleColor.Yellow);
                } else {
                    Console.WriteLine($"[{lvl}] {message}");
                }
            }
        }
    }
}
