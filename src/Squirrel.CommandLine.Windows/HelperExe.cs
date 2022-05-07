using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel.CommandLine
{
    [SupportedOSPlatform("windows")]
    internal class HelperExe : HelperFile
    {
        public static string SetupPath => FindHelperFile("Setup.exe");
        public static string UpdatePath 
            => FindHelperFile("Update.exe", p => Microsoft.NET.HostModel.AppHost.HostWriter.IsBundle(p, out var _));
        public static string StubExecutablePath => FindHelperFile("StubExecutable.exe");
        public static string SingleFileHostPath => FindHelperFile("singlefilehost.exe");
        public static string SignToolPath => FindHelperFile("signtool.exe");

        // private so we don't expose paths to internal tools. these should be exposed as a helper function
        private static string WixTemplatePath => FindHelperFile("template.wxs");
        private static string RceditPath => FindHelperFile("rcedit.exe");
        private static string WixCandlePath => FindHelperFile("candle.exe");
        private static string WixLightPath => FindHelperFile("light.exe");

        public static async Task<string> CompileWixTemplateToMsi(Dictionary<string, string> templateData, string workingDir, string appId)
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
                await InvokeAndThrowIfNonZero(WixCandlePath, candleParams, workingDir).ConfigureAwait(false);

                // Light links and binds one or more .wixobj files and creates a Windows Installer database (.msi or .msm). 
                Log.Info("Linking WiX Template (light.exe)");
                var lightParams = new string[] { "-ext", "WixNetFxExtension", "-spdb", "-sval", "-out", msiFile, objFile };
                await InvokeAndThrowIfNonZero(WixLightPath, lightParams, workingDir).ConfigureAwait(false);
                return msiFile;
            } finally {
                Utility.DeleteFileOrDirectoryHard(wxsFile, throwOnFailure: false);
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
    }
}
