using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using Mono.Options;
using Squirrel.CommandLine;

namespace Squirrel.Tool
{
    class Program
    {
        private const string EMBEDDED_FLAG = "--csq-embedded";

        static int Main(string[] inargs)
        {
            try {
                bool useEmbedded = false;
                string explicitSquirrelPath = null;

                var toolOptions = new OptionSet() {
                    { "csq-embedded", _ => useEmbedded = true },
                    { "csq-path=", v => explicitSquirrelPath = v },
                };

                var restArgs = toolOptions.Parse(inargs).ToArray();

                // explicitly told to execute embedded version
                if (useEmbedded) {
                    return SquirrelHost.Main(restArgs);
                }

                // explicitly told to use specific version at this directory
                if (explicitSquirrelPath != null) {
                    return RunCsqFromPath(explicitSquirrelPath, restArgs);
                }

                Console.WriteLine($"Squirrel Locator {SquirrelRuntimeInfo.SquirrelDisplayVersion}");

                var packageName = "Clowd.Squirrel";
                var dependencies = GetPackageVersionsFromCurrentDir(packageName).Distinct().ToArray();

                if (dependencies.Length == 0) {
                    Console.WriteLine("Clowd.Squirrel is not installed in the current working dir/project. (Using bundled Squirrel)");
                    return SquirrelHost.Main(restArgs);
                }

                if (dependencies.Length > 1) {
                    throw new Exception("Found multiple versions of Clowd.Squirrel installed in current working dir/project. " +
                                        "Please consolidate to a single version: " + string.Join(", ", dependencies));
                }

                var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

                var targetVersion = dependencies.First();


                var toolRootPath = Path.Combine(packages, packageName.ToLower(), targetVersion, "tools");

                // resolve wildcards. we should probably rely on the dotnet tooling for this in the future
                // so we can be more certain we are using precisely the same version as dotnet.
                if (targetVersion.Contains("*")) {
                    var vdir = Directory.EnumerateDirectories(Path.Combine(packages, packageName.ToLower()),
                        targetVersion, SearchOption.TopDirectoryOnly).FirstOrDefault();

                    if (vdir != null)
                        toolRootPath = Path.Combine(vdir, "tools");
                }

                return RunCsqFromPath(toolRootPath, restArgs);
            } catch (Exception ex) {
                Console.WriteLine(ex);
                return -1;
            }
        }

        static int RunCsqFromPath(string toolRootPath, string[] args)
        {
            var dllName = "csq.dll";
            var exeName = "Squirrel.exe";
            var toolDllPath = Path.Combine(toolRootPath, dllName);
            var toolExePath = Path.Combine(toolRootPath, exeName);

            Process p;

            if (File.Exists(toolDllPath)) {
                var dnargs = new[] { toolDllPath, EMBEDDED_FLAG }.Concat(args);
                Console.WriteLine("Running: dotnet " + String.Join(" ", dnargs));
                p = Process.Start("dotnet", dnargs);
            } else if (File.Exists(toolExePath)) {
                if (!SquirrelRuntimeInfo.IsWindows)
                    throw new NotSupportedException($"Squirrel at '{toolRootPath}' does not support this operating system. Please update the package.");
                Console.WriteLine("Running: " + toolExePath + " " + String.Join(" ", args));
                p = Process.Start(toolExePath, args);
            } else {
                throw new Exception("Unable to locate Squirrel at: " + toolRootPath);
            }

            p.WaitForExit();
            return p.ExitCode;
        }

        static IEnumerable<string> GetPackageVersionsFromCurrentDir(string packageName)
        {
            foreach (var projFile in Directory.EnumerateFiles(Environment.CurrentDirectory, "*.csproj", SearchOption.AllDirectories)) {
                var proj = ProjectRootElement.Open(projFile);
                if (proj == null) continue;

                ProjectItemElement item = proj.Items.FirstOrDefault(i => i.ItemType == "PackageReference" && i.Include == packageName);
                if (item == null) continue;

                var version = item.Children.FirstOrDefault(x => x.ElementName == "Version") as ProjectMetadataElement;
                if (version == null) continue;

                yield return version.Value;
            }
        }
    }
}