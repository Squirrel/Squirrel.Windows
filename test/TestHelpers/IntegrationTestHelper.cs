using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Squirrel;
using Splat;
using Xunit;
using System.Text;
using SharpCompress.Archives.Zip;
using SharpCompress.Readers;
using SharpCompress.Common;

namespace Squirrel.Tests.TestHelpers
{
    public static class IntegrationTestHelper
    {
        public static string GetPath(params string[] paths)
        {
            var ret = GetIntegrationTestRootDirectory();
            return (new FileInfo(paths.Aggregate (ret, Path.Combine))).FullName;
        }

        public static string GetIntegrationTestRootDirectory()
        {
            // XXX: This is an evil hack, but it's okay for a unit test
            // We can't use Assembly.Location because unit test runners love
            // to move stuff to temp directories
            var st = new StackFrame(true);
            var di = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(st.GetFileName()), ".."));

            return di.FullName;
        }

        public static bool SkipTestOnXPAndVista()
        {
            int osVersion = Environment.OSVersion.Version.Major*100 + Environment.OSVersion.Version.Minor;
            return (osVersion < 601);
        }

        public static void RunBlockAsSTA(Action block)
        {
            Exception ex = null;
            var t = new Thread(() => {
                try {
                    block();
                } catch (Exception e) {
                    ex = e;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            if (ex != null) {
                // NB: If we don't do this, the test silently passes
                throw new Exception("", ex);
            }
        }

        static object gate = 42;
        public static IDisposable WithFakeInstallDirectory(string packageFileName, out string path)
        {
            var ret = Utility.WithTempDirectory(out path);

            File.Copy(GetPath("fixtures", packageFileName), Path.Combine(path, packageFileName));
            var rp = ReleaseEntry.GenerateFromFile(Path.Combine(path, packageFileName));
            ReleaseEntry.WriteReleaseFile(new[] { rp }, Path.Combine(path, "RELEASES"));

            return ret;
        }

        public static string CreateFakeInstalledApp(string version, string outputDir, string nuspecFile = null)
        {
            var targetDir = default(string);

            var nuget = IntegrationTestHelper.GetPath("..", ".nuget", "nuget.exe");
            nuspecFile = nuspecFile ?? "SquirrelInstalledApp.nuspec";

            using (var clearTemp = Utility.WithTempDirectory(out targetDir)) {
                var nuspec = File.ReadAllText(IntegrationTestHelper.GetPath("fixtures", nuspecFile), Encoding.UTF8);
                File.WriteAllText(Path.Combine(targetDir, nuspecFile), nuspec.Replace("0.1.0", version), Encoding.UTF8);

                File.Copy(
                    IntegrationTestHelper.GetPath("fixtures", "SquirrelAwareApp.exe"), 
                    Path.Combine(targetDir, "SquirrelAwareApp.exe"));
                File.Copy(
                    IntegrationTestHelper.GetPath("fixtures", "NotSquirrelAwareApp.exe"), 
                    Path.Combine(targetDir, "NotSquirrelAwareApp.exe"));

                var psi = new ProcessStartInfo(nuget, "pack " + Path.Combine(targetDir, nuspecFile)) {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = targetDir,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                var pi = Process.Start(psi);
                pi.WaitForExit();
                var output = pi.StandardOutput.ReadToEnd();
                var err = pi.StandardError.ReadToEnd();
                Console.WriteLine(output);  Console.WriteLine(err);

                var di = new DirectoryInfo(targetDir);
                var pkg = di.EnumerateFiles("*.nupkg").First();

                var targetPkgFile = Path.Combine(outputDir, pkg.Name);
                File.Copy(pkg.FullName, targetPkgFile);
                return targetPkgFile;
            }
        }

        public static IDisposable WithFakeInstallDirectory(out string path)
        {
            return WithFakeInstallDirectory("SampleUpdatingApp.1.1.0.0.nupkg", out path);
        }

        public static IDisposable WithFakeAlreadyInstalledApp(out string path)
        {
            return WithFakeAlreadyInstalledApp("InstalledSampleUpdatingApp-1.1.0.0.zip", out path);
        }

        public static IDisposable WithFakeAlreadyInstalledApp(string zipFile, out string path)
        {
            var ret = Utility.WithTempDirectory(out path);

            // NB: Apparently Ionic.Zip is perfectly content to extract a Zip
            // file that doesn't actually exist, without failing.
            var zipPath = GetPath("fixtures", zipFile);
            Assert.True(File.Exists(zipPath));

            var opts = new ExtractionOptions() { ExtractFullPath = true, Overwrite = true, PreserveFileTime = true };
            using (var za = ZipArchive.Open(zipFile))
            using (var reader = za.ExtractAllEntries()) {
                reader.WriteEntryToDirectory(path, opts);
            }

            return ret;
        }
    }
}
