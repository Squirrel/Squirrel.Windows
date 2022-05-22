using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.Lib;

namespace Squirrel.CommandLine.Windows
{
    internal class HelperExe : HelperFile
    {
        public static string SetupPath => FindHelperFile("Setup.exe");
        public static string UpdatePath
            => FindHelperFile("Update.exe", p => Microsoft.NET.HostModel.AppHost.HostWriter.IsBundle(p, out var _));
        public static string StubExecutablePath => FindHelperFile("StubExecutable.exe");

        // private so we don't expose paths to internal tools. these should be exposed as a helper function
        private static string SignToolPath => FindHelperFile("signtool.exe");
        private static string WixTemplatePath => FindHelperFile("template.wxs");
        private static string RceditPath => FindHelperFile("rcedit.exe");
        private static string WixCandlePath => FindHelperFile("candle.exe");
        private static string WixLightPath => FindHelperFile("light.exe");

        [SupportedOSPlatform("windows")]
        private static bool CheckIsAlreadySigned(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) return true;

            if (!File.Exists(filePath)) {
                Log.Warn($"Cannot sign '{filePath}', file does not exist.");
                return true;
            }

            try {
                if (AuthenticodeTools.IsTrusted(filePath)) {
                    Log.Debug("'{0}' is already signed, skipping...", filePath);
                    return true;
                }
            } catch (Exception ex) {
                Log.ErrorException("Failed to determine signing status for " + filePath, ex);
            }

            return false;
        }

        [SupportedOSPlatform("windows")]
        public static void SignPEFilesWithSignTool(string filePath, string signArguments)
        {
            if (CheckIsAlreadySigned(filePath)) return;

            List<string> args = new List<string>();
            args.Add("sign");
            args.AddRange(NativeMethods.CommandLineToArgvW(signArguments));
            args.Add(filePath);

            var result = ProcessUtil.InvokeProcess(SignToolPath, args, null, CancellationToken.None);
            if (result.ExitCode != 0) {
                var cmdWithPasswordHidden = new Regex(@"\/p\s+?[^\s]+").Replace(result.Command, "/p ********");
                throw new Exception(
                    $"Command failed:\n{cmdWithPasswordHidden}\n\n" +
                    $"Output was:\n" + result.StdOutput);
            } else {
                Log.Info("Sign successful: " + result.StdOutput);
            }
        }

        [SupportedOSPlatform("windows")]
        public static void SignPEFilesWithTemplate(string filePath, string signTemplate)
        {
            if (CheckIsAlreadySigned(filePath)) return;

            var command = signTemplate.Replace("\"{{file}}\"", "{{file}}").Replace("{{file}}", $"\"{filePath}\"");
            var args = NativeMethods.CommandLineToArgvW(command);

            if (args.Length < 2)
                throw new OptionValidationException("Invalid signing template");

            var result = ProcessUtil.InvokeProcess(args[0], args.Skip(1), null, CancellationToken.None);
            if (result.ExitCode != 0) {
                var cmdWithPasswordHidden = new Regex(@"\/p\s+?[^\s]+").Replace(result.Command, "/p ********");
                throw new Exception(
                    $"Command failed:\n{cmdWithPasswordHidden}\n\n" +
                    $"Output was:\n" + result.StdOutput);
            } else {
                Log.Info("Sign successful: " + result.StdOutput);
            }
        }

        [SupportedOSPlatform("windows")]
        public static string CompileWixTemplateToMsi(Dictionary<string, string> templateData, string workingDir, string appId)
        {
            var wxsFile = Path.Combine(workingDir, appId + ".wxs");
            var objFile = Path.Combine(workingDir, appId + ".wixobj");
            var msiFile = Path.Combine(workingDir, appId + "_DeploymentTool.msi");

            try {
                // apply dictionary to wsx template
                var templateText = File.ReadAllText(WixTemplatePath);
                var templateResult = CopStache.Render(templateText, templateData);
                File.WriteAllText(wxsFile, templateResult, Encoding.UTF8);

                // Candle reprocesses and compiles WiX source files into object files (.wixobj).
                Log.Info("Compiling WiX Template (candle.exe)");
                var candleParams = new string[] { "-nologo", "-ext", "WixNetFxExtension", "-out", objFile, wxsFile };
                InvokeAndThrowIfNonZero(WixCandlePath, candleParams, workingDir);

                // Light links and binds one or more .wixobj files and creates a Windows Installer database (.msi or .msm). 
                Log.Info("Linking WiX Template (light.exe)");
                var lightParams = new string[] { "-ext", "WixNetFxExtension", "-spdb", "-sval", "-out", msiFile, objFile };
                InvokeAndThrowIfNonZero(WixLightPath, lightParams, workingDir);
                return msiFile;
            } finally {
                Utility.DeleteFileOrDirectoryHard(wxsFile, throwOnFailure: false);
                Utility.DeleteFileOrDirectoryHard(objFile, throwOnFailure: false);
            }
        }

        [SupportedOSPlatform("windows")]
        public static void SetExeIcon(string exePath, string iconPath)
        {
            Log.Info("Updating PE icon for: " + exePath);
            var args = new[] { Path.GetFullPath(exePath), "--set-icon", iconPath };
            Utility.Retry(() => InvokeAndThrowIfNonZero(RceditPath, args, null));
        }

        [SupportedOSPlatform("windows")]
        public static void SetPEVersionBlockFromPackageInfo(string exePath, NuGet.IPackage package, string iconPath = null)
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

            Utility.Retry(() => InvokeAndThrowIfNonZero(RceditPath, args, null));
        }
    }
}
