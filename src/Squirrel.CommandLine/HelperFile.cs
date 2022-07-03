using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    internal class HelperFile
    {
        private static List<string> _searchPaths = new List<string>();
        protected static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(HelperFile));

        static HelperFile()
        {
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "wix");
            
#if DEBUG
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "build", "publish");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "build", "Release", "squirrel", "tools");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor", "wix");
#endif
        }

        public static void AddSearchPath(params string[] pathParts)
        {
            AddSearchPath(Path.Combine(pathParts));
        }

        public static void AddSearchPath(string path)
        {
            if (Directory.Exists(path))
                _searchPaths.Insert(0, path);
        }

        // protected static string FindAny(params string[] names)
        // {
        //     var findCommand = SquirrelRuntimeInfo.IsWindows ? "where" : "which";
        //
        //     // first search the usual places
        //     foreach (var n in names) {
        //         var helper = FindHelperFile(n, throwWhenNotFound: false);
        //         if (helper != null)
        //             return helper;
        //     }
        //     
        //     // then see if there is something on the path
        //     foreach (var n in names) {
        //         var result = ProcessUtil.InvokeProcess(findCommand, new[] { n }, null, CancellationToken.None);
        //         if (result.ExitCode == 0) {
        //             return n;
        //         }
        //     }
        //
        //     throw new Exception($"Could not find any of {String.Join(", ", names)}.");
        // }

        protected static string FindHelperFile(string toFind, Func<string, bool> predicate = null, bool throwWhenNotFound = true)
        {
            var baseDirs = new[] {
                AppContext.BaseDirectory,
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Environment.CurrentDirectory,
            };

            var files = _searchPaths
                .Concat(baseDirs)
                .Where(d => !String.IsNullOrEmpty(d))
                .Distinct()
                .Select(d => Path.Combine(d, toFind))
                .Where(d => File.Exists(d))
                .Select(Path.GetFullPath);

            if (predicate != null)
                files = files.Where(predicate);

            var result = files.FirstOrDefault();
            if (result == null && throwWhenNotFound)
                throw new Exception($"Could not find '{toFind}'.");

            return result;
        }

        protected static string InvokeAndThrowIfNonZero(string exePath, IEnumerable<string> args, string workingDir)
        {
            var result = PlatformUtil.InvokeProcess(exePath, args, workingDir, CancellationToken.None);
            ProcessFailedException.ThrowIfNonZero(result);
            return result.StdOutput;
        }
    }

    public class ProcessFailedException : Exception
    {
        public string Command { get; }
        public string StdOutput { get; }

        public ProcessFailedException(string command, string stdOutput) 
            : base($"Command failed:\n{command}\n\nOutput was:\n{stdOutput}")
        {
            Command = command;
            StdOutput = stdOutput;
        }

        public static void ThrowIfNonZero((int ExitCode, string StdOutput, string Command) result)
        {
            if (result.ExitCode != 0)
                throw new ProcessFailedException(result.Command, result.StdOutput);
        }
    }
}
