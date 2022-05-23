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
        private const string EMBEDDED_FLAG = "--csq-embedded";

        static int Main(string[] args)
        {
            try {
                // explicitly told to execute embedded version
                if (args.Contains(EMBEDDED_FLAG)) {
                    return SquirrelHost.Main(args.Except(new[] { EMBEDDED_FLAG }).ToArray());
                }

                Console.WriteLine($"Squirrel Locator {SquirrelRuntimeInfo.SquirrelDisplayVersion}");
                
                var packageName = "Clowd.Squirrel";
                var dependencies = GetPackageVersionsFromCurrentDir(packageName).Distinct().ToArray();
                
                if (dependencies.Length == 0) {
                    Console.WriteLine("Clowd.Squirrel is not installed in the current working dir/project. (Using bundled Squirrel)");
                    return SquirrelHost.Main(args);
                }

                if (dependencies.Length > 1) {
                    throw new Exception("Found multiple versions of Clowd.Squirrel installed in current working dir/project. " +
                                        "Please consolidate to a single version: " + string.Join(", ", dependencies));
                }

                var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

                var targetVersion = dependencies.First();
                var dllName = "csq.dll";
                var exeName = "Squirrel.exe";
                var toolRootPath = Path.Combine(packages, packageName.ToLower(), targetVersion, "tools");
                var toolDllPath = Path.Combine(toolRootPath, dllName);
                var toolExePath = Path.Combine(toolRootPath, exeName);

                Process p;

                if (File.Exists(toolDllPath)) {
                    var dnargs = new[] { toolDllPath, EMBEDDED_FLAG }.Concat(args);
                    Console.WriteLine("Running: dotnet " + String.Join(" ", dnargs));
                    p = Process.Start("dotnet", dnargs);
                } else if (File.Exists(toolExePath)) {
                    if (!SquirrelRuntimeInfo.IsWindows)
                        throw new NotSupportedException($"The installed version {targetVersion} does not support this operating system. Please update.");
                    Console.WriteLine("Running: " + toolExePath + " " + String.Join(" ", args));
                    p = Process.Start(toolExePath, args);
                } else {
                    throw new Exception("Unable to locate Squirrel " + targetVersion + " at: " + toolRootPath);
                }

                p.WaitForExit();
                return p.ExitCode;
            } catch (Exception ex) {
                Console.WriteLine(ex);
                return -1;
            }
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