using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Construction;
using Mono.Options;
using NuGet.Common;
using NuGet.Versioning;
using Squirrel.CommandLine;
using LogLevel = Squirrel.SimpleSplat.LogLevel;

namespace Squirrel.Tool
{
    class Program
    {
#pragma warning disable CS0436
        public static string SquirrelDisplayVersion => ThisAssembly.AssemblyInformationalVersion + (ThisAssembly.IsPublicRelease ? "" : " (prerelease)");
        public static NuGetVersion SquirrelNugetVersion => NuGetVersion.Parse(ThisAssembly.AssemblyInformationalVersion);
#pragma warning restore CS0436

        const string CLOWD_PACKAGE_NAME = "Clowd.Squirrel";

        private static ConsoleLogger _logger;

        static int Main(string[] inargs)
        {
            _logger = ConsoleLogger.RegisterLogger();
            try {
                return MainInner(inargs);
            } catch (Exception ex) {
                Console.WriteLine("csq error: " + ex.Message);
                return -1;
            }
        }

        static int MainInner(string[] inargs)
        {
            bool verbose = false;
            string explicitSolutionPath = null;
            string explicitSquirrelVersion = null;
            var toolOptions = new OptionSet() {
                { "csq-version=", v => explicitSquirrelVersion = v },
                { "csq-sln=", v => explicitSolutionPath = v },
                { "verbose", _ => verbose = true },
            };

            // we want to forward the --verbose argument to Squirrel, too.
            var verboseArgs = verbose ? new string[] { "--verbose" } : new string[0];
            string[] restArgs = toolOptions.Parse(inargs).Concat(verboseArgs).ToArray();

            if (verbose) {
                _logger.Level = LogLevel.Debug;
            }

            Console.WriteLine($"Squirrel Locator 'csq' {SquirrelDisplayVersion}");
            _logger.Write($"Entry EXE: {SquirrelRuntimeInfo.EntryExePath}", LogLevel.Debug);

            CheckForUpdates();

            var solutionDir = FindSolutionDirectory(explicitSolutionPath);
            var nugetPackagesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            var cacheDir = Path.GetFullPath(solutionDir == null ? ".squirrel" : Path.Combine(solutionDir, ".squirrel"));

            Dictionary<string, string> packageSearchPaths = new();
            packageSearchPaths.Add("nuget user profile cache", Path.Combine(nugetPackagesDir, CLOWD_PACKAGE_NAME.ToLower(), "{0}", "tools"));
            if (solutionDir != null)
                packageSearchPaths.Add("visual studio packages cache", Path.Combine(solutionDir, "packages", CLOWD_PACKAGE_NAME + ".{0}", "tools"));
            packageSearchPaths.Add("squirrel cache", Path.Combine(cacheDir, "{0}", "tools"));

            int runSquirrel(string version)
            {
                foreach (var kvp in packageSearchPaths) {
                    var path = String.Format(kvp.Value, version);
                    if (Directory.Exists(path)) {
                        _logger.Write($"Running {CLOWD_PACKAGE_NAME} {version} from {kvp.Key}", LogLevel.Info);
                        return RunCsqFromPath(path, restArgs);
                    }
                }

                // we did not find it locally on first pass, search for the package online
                var dl = new NugetDownloader(_logger);
                var package = dl.GetPackageMetadata(CLOWD_PACKAGE_NAME, version);

                // search one more time now that we've potentially resolved the nuget version
                foreach (var kvp in packageSearchPaths) {
                    var path = String.Format(kvp.Value, package.Identity.Version);
                    if (Directory.Exists(path)) {
                        _logger.Write($"Running {CLOWD_PACKAGE_NAME} {package.Identity.Version} from {kvp.Key}", LogLevel.Info);
                        return RunCsqFromPath(path, restArgs);
                    }
                }

                // let's try to download it from NuGet.org
                var versionDir = Path.Combine(cacheDir, package.Identity.Version.ToString());
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

                _logger.Write($"Downloading {package.Identity} from NuGet.", LogLevel.Info);

                var filePath = Path.Combine(versionDir, package.Identity + ".nupkg");
                using (var fs = File.Create(filePath))
                    dl.DownloadPackageToStream(package, fs);

                EasyZip.ExtractZipToDirectory(filePath, versionDir);

                var toolsPath = Path.Combine(versionDir, "tools");
                return RunCsqFromPath(toolsPath, restArgs);
            }

            if (explicitSquirrelVersion != null) {
                return runSquirrel(explicitSquirrelVersion);
            }

            if (solutionDir == null) {
                throw new Exception("Could not find '.sln'. Specify solution with '--csq-sln=', or specify version of squirrel to use with '--csq-version='.");
            }

            _logger.Write("Solution dir found at: " + solutionDir, LogLevel.Debug);

            // TODO actually read the SLN file rather than just searching for all .csproj files
            var dependencies = GetPackageVersionsFromDir(solutionDir, CLOWD_PACKAGE_NAME).Distinct().ToArray();

            if (dependencies.Length == 0) {
                throw new Exception("Clowd.Squirrel nuget package was not found installed in solution.");
            }

            if (dependencies.Length > 1) {
                throw new Exception($"Found multiple versions of Clowd.Squirrel installed in solution ({string.Join(", ", dependencies)}). " +
                                    "Please consolidate the following to a single version, or specify the version to use with '--csq-version='");
            }

            var targetVersion = dependencies.Single();

            return runSquirrel(targetVersion);
        }

        static void CheckForUpdates()
        {
            try {
                var myVer = SquirrelNugetVersion;
                var dl = new NugetDownloader(_logger);
                var package = dl.GetPackageMetadata("csq", (myVer.IsPrerelease || myVer.HasMetadata) ? "pre" : "latest");
                if (package.Identity.Version > myVer)
                    _logger.Write($"There is a new version of csq available ({package.Identity.Version})", LogLevel.Warn);
            } catch { ; }
        }

        static string FindSolutionDirectory(string slnArgument)
        {
            if (!String.IsNullOrWhiteSpace(slnArgument)) {
                if (File.Exists(slnArgument) && slnArgument.EndsWith(".sln", StringComparison.InvariantCultureIgnoreCase)) {
                    // we were given a sln file as argument
                    return Path.GetDirectoryName(Path.GetFullPath(slnArgument));
                }

                if (Directory.Exists(slnArgument) && Directory.EnumerateFiles(slnArgument, "*.sln").Any()) {
                    return Path.GetFullPath(slnArgument);
                }
            }

            // try to find the solution directory from cwd
            var cwd = Environment.CurrentDirectory;
            var slnSearchDirs = new string[] {
                cwd,
                Path.Combine(cwd, ".."),
                Path.Combine(cwd, "..", ".."),
            };

            return slnSearchDirs.FirstOrDefault(d => Directory.EnumerateFiles(d, "*.sln").Any());
        }

        static int RunCsqFromPath(string toolRootPath, string[] args)
        {
            // > v3.0.170
            if (File.Exists(Path.Combine(toolRootPath, "Squirrel.CommandLine.runtimeconfig.json"))) {
                var cliPath = Path.Combine(toolRootPath, "Squirrel.CommandLine.dll");
                var dnargs = new[] { cliPath }.Concat(args).ToArray();
                _logger.Write("running dotnet " + String.Join(" ", dnargs), LogLevel.Debug);
                return RunProcess("dotnet", dnargs);
            }

            // v3.0 - v3.0.170
            var toolDllPath = Path.Combine(toolRootPath, "csq.dll");
            if (File.Exists(toolDllPath)) {
                var dnargs = new[] { toolDllPath, "--csq-embedded" }.Concat(args).ToArray();
                _logger.Write("running dotnet " + String.Join(" ", dnargs), LogLevel.Debug);
                return RunProcess("dotnet", dnargs);
            }

            // < v3.0
            var toolExePath = Path.Combine(toolRootPath, "Squirrel.exe");
            if (File.Exists(toolExePath)) {
                if (!SquirrelRuntimeInfo.IsWindows)
                    throw new NotSupportedException(
                        $"Squirrel at '{toolRootPath}' does not support this operating system. Please update the package version to >= 3.0");
                _logger.Write("running " + toolExePath + " " + String.Join(" ", args), LogLevel.Debug);
                return RunProcess(toolExePath, args);
            }

            throw new Exception("Unable to locate Squirrel at: " + toolRootPath);
        }

        static int RunProcess(string path, string[] args)
        {
            var p = Process.Start(path, args);
            p.WaitForExit();
            return p.ExitCode;
        }

        static IEnumerable<string> GetPackageVersionsFromDir(string rootDir, string packageName)
        {
            // old-style framework packages.config
            foreach (var packagesFile in EnumerateFilesUntilSpecificDepth(rootDir, "packages.config", 3)) {
                using var xmlStream = File.OpenRead(packagesFile);
                using var xmlReader = new XmlTextReader(xmlStream);
                var xdoc = XDocument.Load(xmlReader);

                var sqel = xdoc.Root?.Elements().FirstOrDefault(e => e.Attribute("id")?.Value == packageName);
                var ver = sqel?.Attribute("version");
                if (ver == null) continue;

                _logger.Write($"{packageName} {ver.Value} referenced in {packagesFile}", LogLevel.Debug);

                if (ver.Value.Contains("*"))
                    throw new Exception(
                        $"Wildcard versions are not supported in packages.config. Remove wildcard or upgrade csproj format to use PackageReference.");

                yield return ver.Value;
            }

            // new-style csproj PackageReference
            foreach (var projFile in EnumerateFilesUntilSpecificDepth(rootDir, "*.csproj", 3)) {
                var proj = ProjectRootElement.Open(projFile);
                if (proj == null) continue;

                ProjectItemElement item = proj.Items.FirstOrDefault(i => i.ItemType == "PackageReference" && i.Include == packageName);
                if (item == null) continue;

                var version = item.Children.FirstOrDefault(x => x.ElementName == "Version") as ProjectMetadataElement;
                if (version?.Value == null) continue;

                _logger.Write($"{packageName} {version.Value} referenced in {projFile}", LogLevel.Debug);

                yield return version.Value;
            }
        }

        static IEnumerable<string> EnumerateFilesUntilSpecificDepth(string rootPath, string searchPattern, int maxDepth, int currentDepth = 0)
        {
            var files = Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.TopDirectoryOnly);
            foreach (var f in files) {
                yield return f;
            }

            if (currentDepth < maxDepth) {
                foreach (var dir in Directory.EnumerateDirectories(rootPath)) {
                    foreach (var file in EnumerateFilesUntilSpecificDepth(dir, searchPattern, maxDepth, currentDepth + 1)) {
                        yield return file;
                    }
                }
            }
        }
    }
}