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
        private static string SevenZipPath {
            get {
                if (SquirrelRuntimeInfo.IsWindows) return FindHelperFile("7za.exe");
                if (SquirrelRuntimeInfo.IsOSX) return FindHelperFile("7zz");
                throw new NotImplementedException("Unsupported OS");
            }
        }

        private static List<string> _searchPaths = new List<string>();
        protected static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(HelperFile));

        static HelperFile()
        {
#if DEBUG
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "build", "publish");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor", "7zip");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor", "wix");
#endif
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "bin");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "wix");
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

        public static void CompressLzma7z(string zipFilePath, string inFolder)
        {
            Log.Info($"Compressing '{inFolder}' to '{zipFilePath}' using 7z (LZMA)...");
            var args = new string[] { "a", zipFilePath, "-tzip", "-m0=LZMA", "-aoa", "-y", "*" };
            InvokeAndThrowIfNonZero(SevenZipPath, args, inFolder);
        }

        protected static void InvokeAndThrowIfNonZero(string exePath, IEnumerable<string> args, string workingDir)
        {
            var result = ProcessUtil.InvokeProcess(exePath, args, workingDir, CancellationToken.None);
            if (result.ExitCode != 0) {
                throw new Exception(
                    $"Command failed:\n{result.Command}\n\n" +
                    $"Output was:\n" + result.StdOutput);
            }
        }
    }
}
