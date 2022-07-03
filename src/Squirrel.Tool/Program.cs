using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Construction;
using Mono.Options;
using NuGet.Versioning;

namespace Squirrel.Tool
{
    class Program
    {
        private static bool Verbose { get; set; }

        static int Main(string[] inargs)
        {
            try {
                return MainInner(inargs);
            } catch (Exception ex) {
                Console.WriteLine("csq error: " + ex.Message);
                return -1;
            }
        }

        static int MainInner(string[] inargs)
        {
            string explicitSquirrelPath = null;
            string explicitSolutionPath = null;
            bool useEmbedded = false;

            var toolOptions = new OptionSet() {
                { "q|csq-embedded", _ => useEmbedded = true },
                { "csq-path=", v => explicitSquirrelPath = v },
                { "csq-sln=", v => explicitSolutionPath = v },
                { "csq-verbose", _ => Verbose = true },
            };

            var restArgs = toolOptions.Parse(inargs).ToArray();

            Write(SquirrelRuntimeInfo.SquirrelDisplayVersion, true);

            // explicitly told to execute embedded version
            if (useEmbedded) {
                Write("using embedded (--csq-embedded)", true);
                return CommandLine.SquirrelHost.Main(restArgs);
            }

            // explicitly told to use specific version at this directory
            if (explicitSquirrelPath != null) {
                return RunCsqFromPath(explicitSquirrelPath, restArgs);
            }

            // try to find the solution directory from cwd
            string slnDir;
            if (File.Exists(explicitSolutionPath) && explicitSolutionPath.EndsWith(".sln", StringComparison.InvariantCultureIgnoreCase)) {
                slnDir = Path.GetDirectoryName(Path.GetFullPath(explicitSolutionPath));
            } else {
                var cwd = Environment.CurrentDirectory;
                var slnSearchDirs = new string[] {
                    cwd,
                    Path.Combine(cwd, ".."),
                    Path.Combine(cwd, "..", ".."),
                };

                slnDir = slnSearchDirs.FirstOrDefault(d => Directory.EnumerateFiles(d, "*.sln").Any());
                if (slnDir == null) {
                    throw new Exception("Could not find '.sln'. Specify solution file with '--csq-sln=', provide " +
                                        "Squirrel tools path with '--csq-path=' argument, or use embedded version with '--csq-embedded'.");
                }
            }

            slnDir = Path.GetFullPath(slnDir);
            Write("solution dir " + slnDir, true);

            const string packageName = "Clowd.Squirrel";
            var dependencies = GetPackageVersionsFromDir(slnDir, packageName).Distinct().ToArray();

            if (dependencies.Length == 0) {
                throw new Exception("Clowd.Squirrel nuget package was not found in solution.");
            }

            if (dependencies.Length > 1) {
                throw new Exception("Found multiple versions of Clowd.Squirrel installed in solution. " +
                                    "Please consolidate the following to a single version: " + string.Join(", ", dependencies));
            }

            var targetVersion = dependencies.Single();
            var toolsDir = GetToolPathFromUserCache(targetVersion, packageName);

            var localpath = Path.Combine(slnDir, "packages", packageName + "." + targetVersion, "tools");
            if (Directory.Exists(localpath))
                toolsDir = localpath;

            if (!Directory.Exists(toolsDir)) {
                throw new Exception($"Unable to find Squirrel tools for '{targetVersion}'. " +
                                    $"Please specify path to tools directory with '--csq-path=' argument, " +
                                    $"or use embedded version with '--csq-embedded'.");
            }

            return RunCsqFromPath(toolsDir, restArgs);
        }

        static string GetToolPathFromUserCache(string targetVersion, string packageName)
        {
            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            var toolRootPath = Path.Combine(packages, packageName.ToLower(), targetVersion, "tools");
            if (Directory.Exists(toolRootPath))
                return toolRootPath;

            // resolve wildcards. we should probably rely on the dotnet tooling for this in the future
            // so we can be more certain we are using precisely the same version as dotnet.
            if (targetVersion.Contains("*")) {
                Write($"Project version is '{targetVersion}'. Attempting to resolve wildcard...", false);
                var packageDir = Path.Combine(packages, packageName.ToLower());
                var vdir = Directory.EnumerateDirectories(packageDir, targetVersion, SearchOption.TopDirectoryOnly)
                    .Select(d => new DirectoryInfo(d).Name)
                    .Select(NuGetVersion.Parse)
                    .Max();

                if (vdir != null) {
                    return Path.Combine(packageDir, vdir.OriginalVersion, "tools");
                }
            }

            return null;
        }

        static int RunCsqFromPath(string toolRootPath, string[] args)
        {
            var dllName = "csq.dll";
            var exeName = "Squirrel.exe";
            var toolDllPath = Path.Combine(toolRootPath, dllName);
            var toolExePath = Path.Combine(toolRootPath, exeName);

            Process p;

            if (File.Exists(toolDllPath)) {
                var dnargs = new[] { toolDllPath, "--csq-embedded" }.Concat(args).ToArray();
                Write("running dotnet " + String.Join(" ", dnargs), true);
                p = Process.Start("dotnet", dnargs);
            } else if (File.Exists(toolExePath)) {
                if (!OperatingSystem.IsWindows())
                    throw new NotSupportedException(
                        $"Squirrel at '{toolRootPath}' does not support this operating system. Please update the package version to >= 3.0");
                Write("running " + toolExePath + " " + String.Join(" ", args), true);
                p = Process.Start(toolExePath, args);
            } else {
                throw new Exception("Unable to locate Squirrel at: " + toolRootPath);
            }

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

                Write($"{packageName} {ver.Value} referenced in {packagesFile}", true);

                if (ver.Value.Contains("*"))
                    throw new Exception(
                        "Wildcard versions are not supported in packages.config. Remove wildcard or upgrade csproj format to use PackageReference.");

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

                Write($"{packageName} {version.Value} referenced in {projFile}", true);

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

        static void Write(string message, bool isDebugMessage)
        {
            if (Verbose || !isDebugMessage)
                Console.WriteLine("csq: " + message);
        }
    }
}