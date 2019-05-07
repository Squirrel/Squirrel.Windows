using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet;
using Splat;
using Squirrel;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class FakeUrlDownloader : IFileDownloader
    {
        public Task<byte[]> DownloadUrl(string url)
        {
            return Task.FromResult(new byte[0]);
        }

        public async Task DownloadFile(string url, string targetFile, Action<int> progress)
        {
        }
    }

    public class ApplyReleasesTests : IEnableLogger
    {
        [Fact]
        public async Task CleanInstallRunsSquirrelAwareAppsWithInstallFlag()
        {
            string tempDir;
            string remotePkgDir;

            using (Utility.WithTempDirectory(out tempDir))
            using (Utility.WithTempDirectory(out remotePkgDir)) {
                IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir);
                var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.FullInstall();

                    // NB: We execute the Squirrel-aware apps, so we need to give
                    // them a minute to settle or else the using statement will
                    // try to blow away a running process
                    await Task.Delay(1000);

                    Assert.False(File.Exists(Path.Combine(tempDir, "theApp", "app-0.1.0", "args2.txt")));
                    Assert.True(File.Exists(Path.Combine(tempDir, "theApp", "app-0.1.0", "args.txt")));

                    var text = File.ReadAllText(Path.Combine(tempDir, "theApp", "app-0.1.0", "args.txt"), Encoding.UTF8);
                    Assert.Contains("firstrun", text);
                }
            }
        }

        [Fact]
        public async Task UpgradeRunsSquirrelAwareAppsWithUpgradeFlag()
        {
            string tempDir;
            string remotePkgDir;

            using (Utility.WithTempDirectory(out tempDir))
            using (Utility.WithTempDirectory(out remotePkgDir)) {
                IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir);
                var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.FullInstall();
                }

                await Task.Delay(1000);

                IntegrationTestHelper.CreateFakeInstalledApp("0.2.0", remotePkgDir);
                pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.UpdateApp();
                }

                await Task.Delay(1000);

                Assert.False(File.Exists(Path.Combine(tempDir, "theApp", "app-0.2.0", "args2.txt")));
                Assert.True(File.Exists(Path.Combine(tempDir, "theApp", "app-0.2.0", "args.txt")));

                var text = File.ReadAllText(Path.Combine(tempDir, "theApp", "app-0.2.0", "args.txt"), Encoding.UTF8);
                Assert.Contains("updated|0.2.0", text);
            }
        }

        [Fact]
        public async Task RunningUpgradeAppTwiceDoesntCrash()
        {
            string tempDir;
            string remotePkgDir;

            using (Utility.WithTempDirectory(out tempDir))
            using (Utility.WithTempDirectory(out remotePkgDir)) {
                IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir);
                var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.FullInstall();
                }

                await Task.Delay(1000);

                IntegrationTestHelper.CreateFakeInstalledApp("0.2.0", remotePkgDir);
                pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.UpdateApp();
                }

                await Task.Delay(1000);

                // NB: The 2nd time we won't have any updates to apply. We should just do nothing!
                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.UpdateApp();
                }

                await Task.Delay(1000);
            }
        }

        [Fact]
        public async Task FullUninstallRemovesAllVersions()
        {
            string tempDir;
            string remotePkgDir;

            using (Utility.WithTempDirectory(out tempDir))
            using (Utility.WithTempDirectory(out remotePkgDir)) {
                IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir);
                var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.FullInstall();
                }

                await Task.Delay(1000);

                IntegrationTestHelper.CreateFakeInstalledApp("0.2.0", remotePkgDir);
                pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.UpdateApp();
                }

                await Task.Delay(1000);

                using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir)) {
                    await fixture.FullUninstall();
                }

                Assert.False(File.Exists(Path.Combine(tempDir, "theApp", "app-0.1.0", "args.txt")));
                Assert.False(File.Exists(Path.Combine(tempDir, "theApp", "app-0.2.0", "args.txt")));
                Assert.True(File.Exists(Path.Combine(tempDir, "theApp", ".dead")));
            }
        }

        [Fact]
        public void WhenNoNewReleasesAreAvailableTheListIsEmpty()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir)) {
                var appDir = Directory.CreateDirectory(Path.Combine(tempDir, "theApp"));
                var packages = Path.Combine(appDir.FullName, "packages");
                Directory.CreateDirectory(packages);

                var package = "Squirrel.Core.1.0.0.0-full.nupkg";
                File.Copy(IntegrationTestHelper.GetPath("fixtures", package), Path.Combine(packages, package));

                var aGivenPackage = Path.Combine(packages, package);
                var baseEntry = ReleaseEntry.GenerateFromFile(aGivenPackage);

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { baseEntry }, "dontcare");

                Assert.Empty(updateInfo.ReleasesToApply);
            }
        }

        [Fact]
        public void ThrowsWhenOnlyDeltaReleasesAreAvailable()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir))
            {
                var appDir = Directory.CreateDirectory(Path.Combine(tempDir, "theApp"));
                var packages = Path.Combine(appDir.FullName, "packages");
                Directory.CreateDirectory(packages);

                var baseFile = "Squirrel.Core.1.0.0.0-full.nupkg";
                File.Copy(IntegrationTestHelper.GetPath("fixtures", baseFile),
                          Path.Combine(packages, baseFile));
                var basePackage = Path.Combine(packages, baseFile);
                var baseEntry = ReleaseEntry.GenerateFromFile(basePackage);

                var deltaFile = "Squirrel.Core.1.1.0.0-delta.nupkg";
                File.Copy(IntegrationTestHelper.GetPath("fixtures", deltaFile),
                          Path.Combine(packages, deltaFile));
                var deltaPackage = Path.Combine(packages, deltaFile);
                var deltaEntry = ReleaseEntry.GenerateFromFile(deltaPackage);

                Assert.Throws<Exception>(
                    () => UpdateInfo.Create(baseEntry, new[] { deltaEntry }, "dontcare"));
            }
        }

        [Fact]
        public async Task ApplyReleasesWithOneReleaseFile()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, "theApp");
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager.ApplyReleasesImpl(appDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.0.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                var progress = new List<int>();

                await fixture.ApplyReleases(updateInfo, false, false, progress.Add);
                this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                progress
                    .Aggregate(0, (acc, x) => { (x >= acc).ShouldBeTrue(); return x; })
                    .ShouldEqual(100);

                var filesToFind = new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                };

                filesToFind.ForEach(x => {
                    var path = Path.Combine(tempDir, "theApp", "app-1.1.0.0", x.Name);
                    this.Log().Info("Looking for {0}", path);
                    File.Exists(path).ShouldBeTrue();

                    var vi = FileVersionInfo.GetVersionInfo(path);
                    var verInfo = new Version(vi.FileVersion ?? "1.0.0.0");
                    x.Version.ShouldEqual(verInfo);
                });
            }
        }

        [Fact]
        public async Task ApplyReleaseWhichRemovesAFile()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, "theApp");
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                    "Squirrel.Core.1.2.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager.ApplyReleasesImpl(appDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.2.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                var progress = new List<int>();
                await fixture.ApplyReleases(updateInfo, false, false, progress.Add);
                this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                progress
                    .Aggregate(0, (acc, x) => { (x >= acc).ShouldBeTrue(); return x; })
                    .ShouldEqual(100);

                var rootDirectory = Path.Combine(tempDir, "theApp", "app-1.2.0.0");

                new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                }.ForEach(x => {
                    var path = Path.Combine(rootDirectory, x.Name);
                    this.Log().Info("Looking for {0}", path);
                    File.Exists(path).ShouldBeTrue();
                });

                var removedFile = Path.Combine("sub", "Ionic.Zip.dll");
                var deployedPath = Path.Combine(rootDirectory, removedFile);
                File.Exists(deployedPath).ShouldBeFalse();
            }
        }

        [Fact]
        public async Task ApplyReleaseWhichMovesAFileToADifferentDirectory()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir))
            {
                string appDir = Path.Combine(tempDir, "theApp");
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                    "Squirrel.Core.1.3.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager.ApplyReleasesImpl(appDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.3.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                var progress = new List<int>();
                await fixture.ApplyReleases(updateInfo, false, false, progress.Add);
                this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                progress
                    .Aggregate(0, (acc, x) => { (x >= acc).ShouldBeTrue(); return x; })
                    .ShouldEqual(100);

                var rootDirectory = Path.Combine(tempDir, "theApp", "app-1.3.0.0");

                new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                }.ForEach(x => {
                    var path = Path.Combine(rootDirectory, x.Name);
                    this.Log().Info("Looking for {0}", path);
                    File.Exists(path).ShouldBeTrue();
                });

                var oldFile = Path.Combine(rootDirectory, "sub", "Ionic.Zip.dll");
                File.Exists(oldFile).ShouldBeFalse();

                var newFile = Path.Combine(rootDirectory, "other", "Ionic.Zip.dll");
                File.Exists(newFile).ShouldBeTrue();
            }
        }

        [Fact]
        public async Task ApplyReleasesWithDeltaReleases()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, "theApp");
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-delta.nupkg",
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager.ApplyReleasesImpl(appDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-delta.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { deltaEntry, latestFullEntry }, packagesDir);
                updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();

                var progress = new List<int>();

                await fixture.ApplyReleases(updateInfo, false, false, progress.Add);
                this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                progress
                    .Aggregate(0, (acc, x) => { (x >= acc).ShouldBeTrue(); return x; })
                    .ShouldEqual(100);

                var filesToFind = new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                };

                filesToFind.ForEach(x => {
                    var path = Path.Combine(tempDir, "theApp", "app-1.1.0.0", x.Name);
                    this.Log().Info("Looking for {0}", path);
                    File.Exists(path).ShouldBeTrue();

                    var vi = FileVersionInfo.GetVersionInfo(path);
                    var verInfo = new Version(vi.FileVersion ?? "1.0.0.0");
                    x.Version.ShouldEqual(verInfo);
                });
            }
        }

        [Fact]
        public async Task CreateFullPackagesFromDeltaSmokeTest()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, "theApp");
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-delta.nupkg"
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, "theApp", "packages", x)));

                var urlDownloader = new FakeUrlDownloader();
                var fixture = new UpdateManager.ApplyReleasesImpl(appDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Squirrel.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Squirrel.Core.1.1.0.0-delta.nupkg"));

                var resultObs = (Task<ReleaseEntry>)fixture.GetType().GetMethod("createFullPackagesFromDeltas", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(fixture, new object[] { new[] {deltaEntry}, baseEntry });

                var result = await resultObs;
                var zp = new ZipPackage(Path.Combine(tempDir, "theApp", "packages", result.Filename));
                zp.Version.ToString().ShouldEqual("1.1.0.0");
            }
        }

        [Fact]
        public async Task CreateShortcutsRoundTrip()
        {
            string remotePkgPath;
            string path;

            using (Utility.WithTempDirectory(out path)) {
                using (Utility.WithTempDirectory(out remotePkgPath))
                using (var mgr = new UpdateManager(remotePkgPath, "theApp", path)) {
                    IntegrationTestHelper.CreateFakeInstalledApp("1.0.0.1", remotePkgPath);
                    await mgr.FullInstall();
                }

                var fixture = new UpdateManager.ApplyReleasesImpl(Path.Combine(path, "theApp"));
                fixture.CreateShortcutsForExecutable("SquirrelAwareApp.exe", ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup | ShortcutLocation.AppRoot, false, null, null);

                // NB: COM is Weird.
                Thread.Sleep(1000);
                fixture.RemoveShortcutsForExecutable("SquirrelAwareApp.exe", ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup | ShortcutLocation.AppRoot);

                // NB: Squirrel-Aware first-run might still be running, slow
                // our roll before blowing away the temp path
                Thread.Sleep(1000);
            }
        }
        
        [Fact]
        public void UnshimOurselvesSmokeTest()
        {
            // NB: This smoke test is really more of a manual test - try it
            // by shimming Slack, then verifying the shim goes away
            var appDir = Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Slack");
            var fixture = new UpdateManager.ApplyReleasesImpl(appDir);

            fixture.unshimOurselves();
        }

        [Fact(Skip = "This test is currently failing in CI")]
        public async Task GetShortcutsSmokeTest()
        {
            string remotePkgPath;
            string path;

            using (Utility.WithTempDirectory(out path)) {
                using (Utility.WithTempDirectory(out remotePkgPath))
                using (var mgr = new UpdateManager(remotePkgPath, "theApp", path)) {
                    IntegrationTestHelper.CreateFakeInstalledApp("1.0.0.1", remotePkgPath);
                    await mgr.FullInstall();
                }

                var fixture = new UpdateManager.ApplyReleasesImpl(Path.Combine(path, "theApp"));
                var result = fixture.GetShortcutsForExecutable("SquirrelAwareApp.exe", ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup, null);

                Assert.Equal(3, result.Keys.Count);

                // NB: Squirrel-Aware first-run might still be running, slow
                // our roll before blowing away the temp path
                Thread.Sleep(1000);
            }
        }
    }
}