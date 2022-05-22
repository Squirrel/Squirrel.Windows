using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Squirrel.Tool
{
    class Program
    {
        static void Main(string[] args)
        {
            var packageName = "Clowd.Squirrel";
            // var dependencies = Directory.EnumerateFiles(Environment.CurrentDirectory, "*.sln", SearchOption.TopDirectoryOnly)
            //     .SelectMany(GetProjectsFromSln)
            //     .Distinct()
            //     .SelectMany(proj => GetSquirrelVersionsFromProject(packageName, proj))
            //     .Distinct()
            //     .ToArray();

            var dependencies = GetSquirrelVersionsFromProject(packageName);

            if (dependencies.Length == 0)
                throw new Exception("Clowd.Squirrel package was not found to be installed in the current solution.");

            if (dependencies.Length > 1)
                throw new Exception("Found multiple versions of Clowd.Squirrel installed in current solution. " +
                                    "Please consolidate to a single version: " + string.Join(", ", dependencies));

            var toolExecutable = SquirrelRuntimeInfo.SystemOsName switch {
                "windows" => "Squirrel.exe",
                "osx" => "SquirrelMac",
                _ => throw new NotSupportedException("OS not supported: " + SquirrelRuntimeInfo.SystemOsName),
            };
            
            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            var toolPath = Path.Combine(packages, packageName.ToLower(), dependencies.First(), "tools", toolExecutable);

            Process.Start(toolPath, args);
        }

        // static string[] GetProjectsFromSln(string solutionFile)
        // {
        //     var result = ProcessUtil.InvokeProcess("dotnet", new[] { "sln", solutionFile, "list" }, null, CancellationToken.None);
        //     var proj = result.StdOutput
        //         .Split('\r', '\n')
        //         .Select(s => s.TrimEnd())
        //         .Where(s => s.EndsWith(".csproj", StringComparison.InvariantCultureIgnoreCase))
        //         .Select(s => s.Trim())
        //         .ToArray();
        //
        //     return proj;
        // }

        static string[] GetSquirrelVersionsFromProject(string packageName)
        {
            //dotnet list "$PSScriptRoot\src\Clowd\Clowd.csproj" package
            var result = ProcessUtil.InvokeProcess("dotnet", new[] { "list", "package" }, null, CancellationToken.None);
            Console.WriteLine(result.StdOutput);
            
            var escapedName = Regex.Escape(packageName);
            var matches = Regex.Matches(result.StdOutput, $@"(?m){escapedName}.*\s(\d{{1,3}}\.\d{{1,3}}\.\d.*?)$");

            if (matches.Count == 0)
                return new string[0];

            var outp = matches.Select(m => m.Groups[1].Value.Trim()).Distinct().ToArray();
            Console.WriteLine(String.Join(", ", outp));
            Console.WriteLine(String.Join(", ", outp));
            Console.WriteLine(String.Join(", ", outp));
            return outp;
        }
    }
}