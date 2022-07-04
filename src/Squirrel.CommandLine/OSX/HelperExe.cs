using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using Newtonsoft.Json;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine.OSX
{
    internal class HelperExe : HelperFile
    {
        public static string UpdateMacPath
            => FindHelperFile("UpdateMac", p => Microsoft.NET.HostModel.AppHost.HostWriter.IsBundle(p, out var _));

        public static string SquirrelEntitlements => FindHelperFile("Squirrel.entitlements");

        [SupportedOSPlatform("osx")]
        public static void CodeSign(string identity, string entitlements, string filePath)
        {
            if (String.IsNullOrEmpty(entitlements)) {
                Log.Info("No codesign entitlements provided, using default dotnet entitlements: " +
                         "https://docs.microsoft.com/en-us/dotnet/core/install/macos-notarization-issues");
                entitlements = SquirrelEntitlements;
            }

            if (!File.Exists(entitlements)) {
                throw new Exception("Could not find entitlements file at: " + entitlements);
            }

            var args = new List<string> {
                "-s", identity,
                "-f",
                "-v",
                "--deep",
                "--timestamp",
                "--options", "runtime",
                "--entitlements", entitlements,
                filePath
            };

            Log.Info($"Beginning codesign for package...");

            Console.WriteLine(InvokeAndThrowIfNonZero("codesign", args, null));
            
            Log.Info("codesign completed successfully");
        }

        public static void SpctlAssess(string filePath)
        {
            var args2 = new List<string> {
                "--assess",
                "-vvvv",
                filePath
            };

            Console.WriteLine(InvokeAndThrowIfNonZero("spctl", args2, null));
        }

        [SupportedOSPlatform("osx")]
        public static void CreateInstallerPkg(string appBundlePath, string pkgOutputPath, string signIdentity)
        {
            Log.Info($"Creating installer '.pkg' for app at '{appBundlePath}'");
            
            if (File.Exists(pkgOutputPath)) File.Delete(pkgOutputPath);

            using var _1 = Utility.GetTempDirectory(out var tmp);
            using var _2 = Utility.GetTempDirectory(out var tmpPayload1);
            using var _3 = Utility.GetTempDirectory(out var tmpPayload2);

            // copy .app to tmp folder
            var bundleName = Path.GetFileName(appBundlePath);
            var tmpBundlePath = Path.Combine(tmpPayload1, bundleName);
            Utility.CopyFiles(new DirectoryInfo(appBundlePath), new DirectoryInfo(tmpBundlePath));

            // generate non-relocatable pkg
            var pkgPlistPath = Path.Combine(tmp, "tmp.plist");
            InvokeAndThrowIfNonZero("pkgbuild", new[] { "--analyze", "--root", tmpPayload1, pkgPlistPath }, null);
            InvokeAndThrowIfNonZero("plutil", new[] { "-replace", "BundleIsRelocatable", "-bool", "NO", pkgPlistPath }, null);

            var pkg1Path = Path.Combine(tmpPayload2, "1.pkg");
            string[] args1 = {
                "--root", tmpPayload1,
                "--component-plist", pkgPlistPath,
                "--install-location", "/Applications",
                pkg1Path,
            };

            InvokeAndThrowIfNonZero("pkgbuild", args1, null);

            // create product package that installs to home dir
            var distributionPath = Path.Combine(tmp, "distribution.xml");
            InvokeAndThrowIfNonZero("productbuild", new[] { "--synthesize", "--package", pkg1Path, distributionPath }, null);

            // disable local system installation and build final package
            var distXml = File.ReadAllLines(distributionPath).ToList();
            distXml.Insert(2, "<domains enable_anywhere=\"false\" enable_currentUserHome=\"true\" enable_localSystem=\"false\" />");
            File.WriteAllLines(distributionPath, distXml);

            List<string> args2 = new() {
                "--distribution", distributionPath,
                "--package-path", tmpPayload2,
                pkgOutputPath
            };

            if (!String.IsNullOrEmpty(signIdentity)) {
                args2.Add("--sign");
                args2.Add(signIdentity);
            } else {
                Log.Warn("No Installer signing identity provided. The '.pkg' will not be signed.");
            }

            InvokeAndThrowIfNonZero("productbuild", args2, null);

            Log.Info("Installer created successfully");
        }

        [SupportedOSPlatform("osx")]
        public static void Notarize(string filePath, string keychainProfileName)
        {
            Log.Info($"Preparing to Notarize '{filePath}'. This will upload to Apple and usually takes minutes, but could take hours.");

            var args = new List<string> {
                "notarytool",
                "submit",
                "-f", "json",
                "--keychain-profile", keychainProfileName,
                "--wait",
                filePath
            };

            var ntresultjson = PlatformUtil.InvokeProcess("xcrun", args, null, CancellationToken.None);
            Log.Info(ntresultjson.StdOutput);

            // try to catch any notarization errors. if we have a submission id, retrieve notary logs.
            try {
                var ntresult = JsonConvert.DeserializeObject<NotaryToolResult>(ntresultjson.StdOutput);
                if (ntresult?.status != "Accepted" || ntresultjson.ExitCode != 0) {
                    if (ntresult?.id != null) {
                        var logargs = new List<string> {
                            "notarytool",
                            "log",
                            ntresult?.id,
                            "--keychain-profile", keychainProfileName,
                        };

                        var result = PlatformUtil.InvokeProcess("xcrun", logargs, null, CancellationToken.None);
                        Log.Warn(result.StdOutput);
                    }

                    throw new Exception("Notarization failed: " + ntresultjson.StdOutput);
                }
            } catch (JsonReaderException) {
                throw new Exception("Notarization failed: " + ntresultjson.StdOutput);
            }

            Log.Info("Notarization completed successfully");

            Log.Info($"Stapling Notarization to '{filePath}'");
            Console.WriteLine(InvokeAndThrowIfNonZero("xcrun", new[] { "stapler", "staple", filePath }, null));
        }

        private class NotaryToolResult
        {
            public string id { get; set; }
            public string message { get; set; }
            public string status { get; set; }
        }

        [SupportedOSPlatform("osx")]
        public static void CreateDittoZip(string folder, string outputZip)
        {
            if (File.Exists(outputZip)) File.Delete(outputZip);

            var args = new List<string> {
                "-c",
                "-k",
                "--rsrc",
                "--keepParent",
                "--sequesterRsrc",
                folder,
                outputZip
            };

            Log.Info($"Creating ditto bundle '{outputZip}'");
            InvokeAndThrowIfNonZero("ditto", args, null);
        }
    }
}