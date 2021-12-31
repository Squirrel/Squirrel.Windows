using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Squirrel;
using Squirrel.Lib;
using Squirrel.SimpleSplat;

namespace Squirrel
{
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    internal static class HelperExe
    {
        public static string SetupPath => FindHelperExecutable("Setup.exe", _searchPaths);
        public static string UpdatePath => FindHelperExecutable("Update.exe", _searchPaths);
        public static string StubExecutablePath => FindHelperExecutable("StubExecutable.exe", _searchPaths);
        public static string SingleFileHostPath => FindHelperExecutable("singlefilehost.exe", _searchPaths);
        public static string WixTemplatePath => FindHelperExecutable("template.wxs", _searchPaths);

        // private so we don't expose paths to internal tools. these should be exposed as a helper function
        private static string NugetPath => FindHelperExecutable("NuGet.exe", _searchPaths);
        private static string RceditPath => FindHelperExecutable("rcedit.exe", _searchPaths);
        private static string SevenZipPath => FindHelperExecutable("7z.exe", _searchPaths);
        private static string SignToolPath => FindHelperExecutable("signtool.exe", _searchPaths);
        private static string WixCandlePath => FindHelperExecutable("candle.exe", _searchPaths);
        private static string WixLightPath => FindHelperExecutable("light.exe", _searchPaths);

        private static List<string> _searchPaths = new List<string>();
        private static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(HelperExe));

        static HelperExe()
        {
            if (ModeDetector.InUnitTestRunner()) {
                var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", ""));
                AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor"));
                AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor", "7zip"));
                AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor", "wix"));
            } else {
#if DEBUG
                AddSearchPath(Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "..", "..", "..", "build", "publish"));
                AddSearchPath(Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor"));
                AddSearchPath(Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor", "7zip"));
                AddSearchPath(Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor", "wix"));
#else
                AddSearchPath(Path.Combine(AssemblyRuntimeInfo.BaseDirectory, "bin"));
#endif
            }
        }

        public static void AddSearchPath(string path)
        {
            if (Directory.Exists(path))
                _searchPaths.Insert(0, path);
        }

        private static string FindHelperExecutable(string toFind, IEnumerable<string> additionalDirs = null, bool throwWhenNotFound = true)
        {
            if (File.Exists(toFind))
                return Path.GetFullPath(toFind);

            additionalDirs = additionalDirs ?? Enumerable.Empty<string>();
            var dirs = (new[] { AppContext.BaseDirectory, Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) })
                .Concat(additionalDirs ?? Enumerable.Empty<string>())
                .Where(d => !String.IsNullOrEmpty(d))
                .Select(Path.GetFullPath);

            var exe = @".\" + toFind;
            var result = dirs
                .Select(x => Path.Combine(x, toFind))
                .FirstOrDefault(x => File.Exists(x));

            if (result == null && throwWhenNotFound)
                throw new Exception($"Could not find helper '{exe}'. If not in the default location, add additional search paths using command arguments.");

            return result ?? exe;
        }

        public static async Task CompileWixTemplateToMsi(string wxsTarget, string outputFile)
        {
            var workingDir = Path.GetDirectoryName(wxsTarget);
            var targetName = Path.GetFileNameWithoutExtension(wxsTarget);
            var objFile = Path.Combine(workingDir, targetName + ".wixobj");

            try {
                // Candle reprocesses and compiles WiX source files into object files (.wixobj).
                var candleParams = new string[] { "-nologo", "-ext", "WixNetFxExtension", "-out", objFile, wxsTarget };
                var processResult = await Utility.InvokeProcessAsync(WixCandlePath, candleParams, CancellationToken.None, workingDir);

                if (processResult.Item1 != 0) {
                    var msg = String.Format(
                        "Failed to compile WiX template, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
                        "candle.exe", candleParams, processResult.Item2);

                    throw new Exception(msg);
                }

                // Light links and binds one or more .wixobj files and creates a Windows Installer database (.msi or .msm). 
                var lightParams = new string[] { "-ext", "WixNetFxExtension", "-spdb", "-sval", "-out", outputFile, objFile };
                processResult = await Utility.InvokeProcessAsync(WixLightPath, lightParams, CancellationToken.None, workingDir);

                if (processResult.Item1 != 0) {
                    var msg = String.Format(
                        "Failed to link WiX template, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
                        "light.exe", lightParams, processResult.Item2);

                    throw new Exception(msg);
                }
            } finally {
                Utility.DeleteFileOrDirectoryHardOrGiveUp(objFile);
            }
        }

        public static async Task SetExeIcon(string exePath, string iconPath)
        {
            var args = new[] { Path.GetFullPath(exePath), "--set-icon", iconPath };
            var processResult = await Utility.InvokeProcessAsync(RceditPath, args, CancellationToken.None);

            if (processResult.ExitCode != 0) {
                var msg = String.Format(
                    "Failed to modify resources, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
                    RceditPath, args, processResult.StdOutput);

                throw new Exception(msg);
            } else {
                Console.WriteLine(processResult.StdOutput);
            }
        }

        public static async Task SetPEVersionBlockFromPackageInfo(string exePath, Squirrel.NuGet.IPackage package, string iconPath = null)
        {
            var realExePath = Path.GetFullPath(exePath);
            var company = String.Join(",", package.Authors);

            List<string> args = new List<string>() {
                realExePath,
                "--set-version-string", "CompanyName", company,
                "--set-version-string", "LegalCopyright", package.Copyright ?? "Copyright © " + DateTime.Now.Year.ToString() + " " + company,
                "--set-version-string", "FileDescription", package.Summary ?? package.Description ?? "Installer for " + package.Id,
                "--set-version-string", "ProductName", package.Description ?? package.Summary ?? package.Id,
                "--set-file-version", package.Version.ToString(),
                "--set-product-version", package.Version.ToString(),
            };

            if (iconPath != null) {
                args.Add("--set-icon");
                args.Add(Path.GetFullPath(iconPath));
            }

            var processResult = await Utility.InvokeProcessAsync(RceditPath, args, CancellationToken.None);

            if (processResult.ExitCode != 0) {
                var msg = String.Format(
                    "Failed to modify resources, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
                    RceditPath, args, processResult.StdOutput);

                throw new Exception(msg);
            } else {
                Console.WriteLine(processResult.StdOutput);
            }
        }

        public static async Task SignPEFile(string exePath, string signingOpts)
        {
            if (String.IsNullOrEmpty(signingOpts)) {
                Log.Debug("{0} was not signed.", exePath);
                return;
            }

            try {
                if (AuthenticodeTools.IsTrusted(exePath)) {
                    Log.Info("{0} is already signed, skipping...", exePath);
                    return;
                }
            } catch (Exception ex) {
                Log.ErrorException("Failed to determine signing status for " + exePath, ex);
            }

            Log.Info("About to sign {0}", exePath);

            var psi = Utility.CreateProcessStartInfo(SignToolPath, $"sign {signingOpts} \"{exePath}\"");
            var processResult = await Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None);

            if (processResult.ExitCode != 0) {
                var optsWithPasswordHidden = new Regex(@"/p\s+\w+").Replace(signingOpts, "/p ********");
                var msg = String.Format("Failed to sign, command invoked was: '{0} sign {1} {2}'\r\n{3}",
                    SignToolPath, optsWithPasswordHidden, exePath, processResult.StdOutput);
                throw new Exception(msg);
            } else {
                Log.Info("Sign successful: " + processResult.StdOutput);
            }
        }

        public static async Task NugetPack(string nuspecPath, string baseDirectory, string outputDirectory)
        {
            var args = new string[] { "pack", nuspecPath, "-BasePath", baseDirectory, "-OutputDirectory", outputDirectory };

            Log.Info($"Packing '{baseDirectory}' into nupkg.");
            var res = await Utility.InvokeProcessAsync(NugetPath, args, CancellationToken.None);

            if (res.ExitCode != 0)
                throw new Exception($"Failed nuget pack (exit {res.ExitCode}): \r\n " + res.StdOutput);
        }

        public static async Task ExtractZipToDirectory(string zipFilePath, string outFolder)
        {
            try {
                var cmd = SevenZipPath;
                var args = String.Format("x \"{0}\" -tzip -mmt on -aoa -y -o\"{1}\" *", zipFilePath, outFolder);

                // TODO this should probably fall back to SharpCompress if not on windows
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                    cmd = "wine";
                    args = SevenZipPath + " " + args;
                }

                var psi = Utility.CreateProcessStartInfo(cmd, args);

                var result = await Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None);
                if (result.ExitCode != 0) throw new Exception(result.StdOutput);
            } catch (Exception ex) {
                Log.Error($"Failed to extract file {zipFilePath} to {outFolder}\n{ex.Message}");
                throw;
            }
        }

        public static async Task CreateZipFromDirectory(string zipFilePath, string inFolder)
        {
            try {
                var cmd = SevenZipPath;
                var args = String.Format("a \"{0}\" -tzip -aoa -y -mmt on *", zipFilePath);

                // TODO this should probably fall back to SharpCompress if not on windows
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                    cmd = "wine";
                    args = SevenZipPath + " " + args;
                }

                var psi = Utility.CreateProcessStartInfo(cmd, args, inFolder);

                var result = await Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None);
                if (result.ExitCode != 0) throw new Exception(result.StdOutput);
            } catch (Exception ex) {
                Log.Error($"Failed to extract file {zipFilePath} to {inFolder}\n{ex.Message}");
                throw;
            }
        }
    }
}
