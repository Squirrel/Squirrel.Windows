using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    [SupportedOSPlatform("windows")]
    internal static class HelperExe
    {
        public static string SetupPath => FindHelperFile("Setup.exe");
        public static string UpdatePath(Func<string, bool> predicate) => FindHelperFile("Update.exe", predicate);
        public static string StubExecutablePath => FindHelperFile("StubExecutable.exe");
        public static string SingleFileHostPath => FindHelperFile("singlefilehost.exe");
        public static string WixTemplatePath => FindHelperFile("template.wxs");
        public static string SignToolPath => FindHelperFile("signtool.exe");

        // private so we don't expose paths to internal tools. these should be exposed as a helper function
        private static string SevenZipPath => FindHelperFile("7z.exe");
        private static string RceditPath => FindHelperFile("rcedit.exe");
        private static string WixCandlePath => FindHelperFile("candle.exe");
        private static string WixLightPath => FindHelperFile("light.exe");

        private static List<string> _searchPaths = new List<string>();
        private static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(HelperExe));

        static HelperExe()
        {
#if DEBUG
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "build", "publish");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor", "7zip");
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor", "wix");
#else
            AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "bin");
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

        private static string FindHelperFile(string toFind, Func<string, bool> predicate = null)
        {
            const bool throwWhenNotFound = true;

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
                .Where(File.Exists)
                .Select(Path.GetFullPath);

            if (predicate != null)
                files = files.Where(predicate);

            var result = files.FirstOrDefault();
            if (result == null && throwWhenNotFound)
                throw new Exception($"Could not find '{toFind}'.");

            return result ?? toFind;
        }

        public static async Task CompileWixTemplateToMsi(string wxsTarget, string outputFile)
        {
            var workingDir = Path.GetDirectoryName(wxsTarget);
            var targetName = Path.GetFileNameWithoutExtension(wxsTarget);
            var objFile = Path.Combine(workingDir, targetName + ".wixobj");

            try {
                // Candle reprocesses and compiles WiX source files into object files (.wixobj).
                Log.Info("Compiling WiX Template (candle.exe)");
                var candleParams = new string[] { "-nologo", "-ext", "WixNetFxExtension", "-out", objFile, wxsTarget };
                await InvokeAndThrowIfNonZero(WixCandlePath, candleParams, workingDir).ConfigureAwait(false);

                // Light links and binds one or more .wixobj files and creates a Windows Installer database (.msi or .msm). 
                Log.Info("Linking WiX Template (light.exe)");
                var lightParams = new string[] { "-ext", "WixNetFxExtension", "-spdb", "-sval", "-out", outputFile, objFile };
                await InvokeAndThrowIfNonZero(WixLightPath, lightParams, workingDir).ConfigureAwait(false);
            } finally {
                Utility.DeleteFileOrDirectoryHard(objFile, throwOnFailure: false);
            }
        }

        public static Task SetExeIcon(string exePath, string iconPath)
        {
            Log.Info("Updating PE icon for: " + exePath);
            var args = new[] { Path.GetFullPath(exePath), "--set-icon", iconPath };
            return InvokeAndThrowIfNonZero(RceditPath, args);
        }

        public static Task SetPEVersionBlockFromPackageInfo(string exePath, NuGet.IPackage package, string iconPath = null)
        {
            Log.Info("Updating StringTable resources for: " + exePath);
            var realExePath = Path.GetFullPath(exePath);

            List<string> args = new List<string>() {
                realExePath,
                "--set-version-string", "CompanyName", package.ProductCompany,
                "--set-version-string", "LegalCopyright", package.ProductCopyright,
                "--set-version-string", "FileDescription", package.ProductDescription,
                "--set-version-string", "ProductName", package.ProductName,
                "--set-file-version", package.Version.ToString(),
                "--set-product-version", package.Version.ToString(),
            };

            if (iconPath != null) {
                args.Add("--set-icon");
                args.Add(Path.GetFullPath(iconPath));
            }

            return InvokeAndThrowIfNonZero(RceditPath, args);
        }

        //private static string _7zPath;

        //private static async Task<string> Get7zPath()
        //{
        //    if (_7zPath != null) return _7zPath;

        //    var findCommand = SquirrelRuntimeInfo.IsWindows ? "where" : "which";

        //    // search for the 7z or 7za on the path
        //    var result = await Utility.InvokeProcessUnsafeAsync(Utility.CreateProcessStartInfo(findCommand, "7z"), CancellationToken.None).ConfigureAwait(false);
        //    if (result.ExitCode == 0) {
        //        _7zPath = "7z";
        //        return _7zPath;
        //    }

        //    result = await Utility.InvokeProcessUnsafeAsync(Utility.CreateProcessStartInfo(findCommand, "7za"), CancellationToken.None).ConfigureAwait(false);
        //    if (result.ExitCode == 0) {
        //        _7zPath = "7za";
        //        return _7zPath;
        //    }

        //    // we only bundle the windows version currently
        //    if (SquirrelRuntimeInfo.IsWindows) {
        //        _7zPath = HelperExe.SevenZipPath;
        //        return _7zPath;
        //    }

        //    return null;
        //}

        public static async Task CompressLzma7z(string zipFilePath, string inFolder)
        {
            Log.Info($"Compressing '{inFolder}' to '{zipFilePath}' using 7z (LZMA)...");
            var args = new string[] { "a", zipFilePath, "-tzip", "-m0=LZMA", "-aoa", "-y", "*" };
            await InvokeAndThrowIfNonZero(SevenZipPath, args, inFolder).ConfigureAwait(false);
        }

        private static async Task InvokeAndThrowIfNonZero(string exePath, IEnumerable<string> args, string workingDir = null)
        {
            var result = await Utility.InvokeProcessAsync(exePath, args, CancellationToken.None, workingDir).ConfigureAwait(false);
            if (result.ExitCode != 0) {
                throw new Exception(
                    $"Command failed: \n{Path.GetFileName(exePath)} {Utility.ArgsToCommandLine(args)}\n\n" +
                    $"Output was:\n" + result.StdOutput);
            }
        }
    }
}
