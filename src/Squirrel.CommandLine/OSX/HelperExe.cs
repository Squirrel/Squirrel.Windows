using System;
using System.Collections.Generic;
using System.IO;
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
        public static void CodeSign(string identity, string entitlements, string[] files)
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
                "--entitlements", entitlements
            };

            args.AddRange(files);

            Log.Info($"Preparing to codesign {files.Length} Mach-O files...");

            InvokeAndThrowIfNonZero("codesign", args, null);

            Log.Info("codesign completed successfully");
        }

        [SupportedOSPlatform("osx")]
        public static void CreateInstallerPkg(string appBundlePath, string pkgOutputPath, string signIdentity)
        {
            Log.Info($"Creating installer '.pkg' for app at '{appBundlePath}'");

            var args = new List<string> {
                "--install-location", "~/Applications",
                "--component", appBundlePath,
            };

            if (!String.IsNullOrEmpty(signIdentity)) {
                args.Add("--sign");
                args.Add(signIdentity);
            } else {
                Log.Warn("No Installer signing identity provided. The '.pkg' will not be signed.");
            }

            args.Add(pkgOutputPath);

            InvokeAndThrowIfNonZero("pkgbuild", args, null);

            Log.Info("Installer created successfully");
        }

        [SupportedOSPlatform("osx")]
        public static void StapleNotarization(string filePath)
        {
            Log.Info($"Stapling Notarization to '{filePath}'");
            var args = new List<string> {
                "stapler", "staple", filePath,
            };
            InvokeAndThrowIfNonZero("xcrun", args, null);
        }

        [SupportedOSPlatform("osx")]
        public static void NotarizePkg(string pkgPath, string profileName)
        {
            Log.Info($"Preparing to Notarize '{pkgPath}'. This will upload to Apple and usually takes minutes, but could take hours.");
            
            var args = new List<string> {
                "notarytool",
                "submit",
                "-f", "json",
                // "--apple-id", appleId,
                // "--password", appPwd,
                // "--team-id", teamId,
                "--keychain-profile", profileName,
                "--wait",
                pkgPath
            };
            
            var ntresultjson = PlatformUtil.InvokeProcess("xcrun", args, null, CancellationToken.None);
            Log.Info(ntresultjson);
            
            if (ntresultjson.ExitCode != 0) {
                try {
                    var ntresult = JsonConvert.DeserializeObject<NotaryToolResult>(ntresultjson.StdOutput);
                    // find and report notarization errors if possible
                    if (ntresult?.id != null) {
                        var logargs = new List<string> {
                            "notarytool",
                            "log",
                            ntresult?.id,
                            "--keychain-profile", profileName,
                            // "--apple-id", appleId,
                            // "--password", appPwd,
                            // "--team-id", teamId,
                        };

                        var result = PlatformUtil.InvokeProcess("xcrun", logargs, null, CancellationToken.None);
                        Log.Warn(result.StdOutput);
                    }
                } catch { ;}
                
                throw new Exception("Notarization failed: " + ntresultjson.StdOutput);
            }
            
            Log.Info("Notarization completed successfully");
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
            var args = new List<string> {
                "-c",
                "-k",
                "--keepParent",
                folder,
                outputZip
            };

            InvokeAndThrowIfNonZero("ditto", args, null);
        }
    }
}