using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using Squirrel.CommandLine;

namespace Squirrel.Tool
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Contains("--csq-embedded-only")) {
                return SquirrelHost.Main(args);
            }
            
            Console.WriteLine($"Squirrel Locator {SquirrelRuntimeInfo.SquirrelDisplayVersion}");

            var packageName = "Clowd.Squirrel";
            var dependencies = GetPackageVersionsFromCurrentDir(packageName).Distinct().ToArray();

            if (dependencies.Length == 0) {
                Console.WriteLine("Clowd.Squirrel package was not found to be installed in the current working dir/project.");
                Console.WriteLine($"Using bundled Squirrel {SquirrelRuntimeInfo.SquirrelDisplayVersion}");
                return SquirrelHost.Main(args);
            }

            if (dependencies.Length > 1) {
                throw new Exception("Found multiple versions of Clowd.Squirrel installed in current working dir/project. " +
                                    "Please consolidate to a single version: " + string.Join(", ", dependencies));
            }

            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

            var targetVersion = dependencies.First();
            Console.WriteLine("Attempting to locate Squirrel " + targetVersion + " (installed in current working dir)");

            var dllName = "csq.dll";
            var exeName = "Squirrel.exe";
            var toolDllPath = Path.Combine(packages, packageName.ToLower(), targetVersion, "tools", dllName);
            var toolExePath = Path.Combine(packages, packageName.ToLower(), targetVersion, "tools", exeName);

            Process p;

            if (File.Exists(toolDllPath)) {
                Console.WriteLine("Running at: " + toolDllPath);
                p = Process.Start("dotnet", new[] { dllName, "--csq-embedded-only" }.Concat(args));
            } else if (File.Exists(toolExePath)) {
                if (!SquirrelRuntimeInfo.IsWindows)
                    throw new NotSupportedException($"The installed version {targetVersion} does not support this operating system. Please update.");
                Console.WriteLine("Running at: " + toolExePath);
                p = Process.Start(toolExePath, args);
            } else {
                throw new Exception("Unable to locate Squirrel " + targetVersion);
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