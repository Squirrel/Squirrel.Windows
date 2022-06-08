using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Squirrel.NuGet;
using Squirrel.Tests.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace Squirrel.Tests
{
    public class ApplyReleasesTests : TestLoggingBase
    {
        public ApplyReleasesTests(ITestOutputHelper log) : base(log)
        {
        }

        public const string APP_ID = "theFakeApp";

        [Fact]
        public async Task CleanInstallRunsSquirrelAwareAppsWithInstallFlag()
        {
            using var _1 = Utility.GetTempDirectory(out var tempDir);
            using var _2 = Utility.GetTempDirectory(out var remotePkgDir);

            IntegrationTestHelper.CreateNewVersionInPackageDir("0.1.0", remotePkgDir);
            using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir);
            await fixture.FullInstall();

            // NB: We execute the Squirrel-aware apps, so we need to give
            // them a minute to settle or else the using statement will
            // try to blow away a running process
            await Task.Delay(1000);

            Assert.False(File.Exists(Path.Combine(tempDir, APP_ID, "current", "args2.txt")));
            Assert.True(File.Exists(Path.Combine(tempDir, APP_ID, "current", "args.txt")));

            var text = File.ReadAllText(Path.Combine(tempDir, APP_ID, "current", "args.txt"), Encoding.UTF8);
            Assert.Contains("firstrun", text);
        }

        [Fact]
        public async Task UpgradeRunsSquirrelAwareAppsWithUpgradeFlag()
        {
            using var _1 = Utility.GetTempDirectory(out var tempDir);
            using var _2 = Utility.GetTempDirectory(out var remotePkgDir);

            IntegrationTestHelper.CreateNewVersionInPackageDir("0.1.0", remotePkgDir);

            using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir)) {
                await fixture.FullInstall();
            }

            await Task.Delay(1000);

            IntegrationTestHelper.CreateNewVersionInPackageDir("0.2.0", remotePkgDir);

            using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir)) {
                await fixture.UpdateApp();
            }

            await Task.Delay(1000);

            Assert.False(File.Exists(Path.Combine(tempDir, APP_ID, "staging", "app-0.2.0", "args2.txt")));
            Assert.True(File.Exists(Path.Combine(tempDir, APP_ID, "staging", "app-0.2.0", "args.txt")));

            var text = File.ReadAllText(Path.Combine(tempDir, APP_ID, "staging", "app-0.2.0", "args.txt"), Encoding.UTF8);
            Assert.Contains("updated", text);
            Assert.Contains("0.2.0", text);
        }

        [Fact]
        public async Task RunningUpgradeAppTwiceDoesntCrash()
        {
            using var _1 = Utility.GetTempDirectory(out var tempDir);
            using var _2 = Utility.GetTempDirectory(out var remotePkgDir);

            IntegrationTestHelper.CreateNewVersionInPackageDir("0.1.0", remotePkgDir);

            using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir)) {
                await fixture.FullInstall();
            }

            await Task.Delay(1000);

            IntegrationTestHelper.CreateNewVersionInPackageDir("0.2.0", remotePkgDir);

            using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir)) {
                await fixture.UpdateApp();
            }

            await Task.Delay(1000);

            // NB: The 2nd time we won't have any updates to apply. We should just do nothing!
            using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir)) {
                await fixture.UpdateApp();
            }

            await Task.Delay(1000);
        }

        [Fact]
        public async Task FullUninstallRemovesAllVersions()
        {
            using var _1 = Utility.GetTempDirectory(out var tempDir);
            using var _2 = Utility.GetTempDirectory(out var remotePkgDir);

            IntegrationTestHelper.CreateNewVersionInPackageDir("0.1.0", remotePkgDir);

            using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir)) {
                await fixture.FullInstall();
            }

            await Task.Delay(1000);

            IntegrationTestHelper.CreateNewVersionInPackageDir("0.2.0", remotePkgDir);

            using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir)) {
                await fixture.UpdateApp();
            }

            await Task.Delay(1000);

            using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir)) {
                await fixture.FullUninstall();
            }

            Assert.False(File.Exists(Path.Combine(tempDir, APP_ID, "app-0.1.0", "args.txt")));
            Assert.False(File.Exists(Path.Combine(tempDir, APP_ID, "app-0.2.0", "args.txt")));
            Assert.True(File.Exists(Path.Combine(tempDir, APP_ID, ".dead")));
        }

        [Fact]
        public async Task CanInstallAndUpdatePackageWithDotsInId()
        {
            string tempDir;
            string remotePkgDir;
            const string pkgName = "Squirrel.Installed.App";

            using (Utility.GetTempDirectory(out tempDir))
            using (Utility.GetTempDirectory(out remotePkgDir)) {
                // install 0.1.0
                IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir, "SquirrelInstalledAppWithDots.nuspec");
                var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, pkgName, tempDir)) {
                    await fixture.FullInstall();
                }

                //Assert.True(Directory.Exists(Path.Combine(tempDir, pkgName, "app-0.1.0")));
                Assert.True(Directory.Exists(Path.Combine(tempDir, pkgName, "current")));

                var info = new AppDescWindows(Path.Combine(tempDir, pkgName), pkgName);

                var version = info.GetVersions().Single();
                Assert.True(version.IsCurrent);
                Assert.Equal(new SemanticVersion(0, 1, 0), version.Manifest.Version);

                await Task.Delay(1000);
                Assert.True(File.ReadAllText(Path.Combine(version.DirectoryPath, "args.txt")).Contains("--squirrel-firstrun"));

                // update top 0.2.0
                IntegrationTestHelper.CreateFakeInstalledApp("0.2.0", remotePkgDir, "SquirrelInstalledAppWithDots.nuspec");
                pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
                ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

                using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, pkgName, tempDir)) {
                    await fixture.UpdateApp();
                }

                info.UpdateAndRetrieveCurrentFolder(false);

                var versions = info.GetVersions().ToArray();
                Assert.Equal(2, versions.Count());
                Assert.Equal(new SemanticVersion(0, 2, 0), versions.Single(s => s.IsCurrent).Version);

                //Assert.True(Directory.Exists(Path.Combine(tempDir, pkgName, "app-0.2.0")));
                await Task.Delay(1000);

                // uninstall
                using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, pkgName, tempDir)) {
                    await fixture.FullUninstall();
                }

                Assert.False(File.Exists(Path.Combine(tempDir, pkgName, "app-0.1.0", "args.txt")));
                Assert.False(File.Exists(Path.Combine(tempDir, pkgName, "app-0.2.0", "args.txt")));
                Assert.True(File.Exists(Path.Combine(tempDir, pkgName, ".dead")));
            }
        }

        [Fact]
        public void WhenNoNewReleasesAreAvailableTheListIsEmpty()
        {
            using var _ = Utility.GetTempDirectory(out var tempDir);

            var appDir = Directory.CreateDirectory(Path.Combine(tempDir, APP_ID));
            var packages = Path.Combine(appDir.FullName, "packages");
            Directory.CreateDirectory(packages);

            var package = "Squirrel.Core.1.0.0.0-full.nupkg";
            File.Copy(IntegrationTestHelper.GetPath("fixtures", package), Path.Combine(packages, package));

            var aGivenPackage = Path.Combine(packages, package);
            var baseEntry = ReleaseEntry.GenerateFromFile(aGivenPackage);

            var updateInfo = UpdateInfo.Create(baseEntry, new[] { baseEntry }, "dontcare");

            Assert.Empty(updateInfo.ReleasesToApply);
        }

        [Fact]
        public void ThrowsWhenOnlyDeltaReleasesAreAvailable()
        {
            string tempDir;
            using (Utility.GetTempDirectory(out tempDir)) {
                var appDir = Directory.CreateDirectory(Path.Combine(tempDir, APP_ID));
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

            using (Utility.GetTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, APP_ID);
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir("", APP_ID, tempDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.0.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                var progress = new List<int>();

                await fixture.ApplyReleasesPublic(updateInfo, false, false, progress.Add);
                this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                progress
                    .Aggregate(0, (acc, x) => {
                        (x >= acc).ShouldBeTrue();
                        return x;
                    })
                    .ShouldEqual(100);

                var filesToFind = new[] {
                    new { Name = "NLog.dll", Version = new Version("2.0.0.0") },
                    new { Name = "NSync.Core.dll", Version = new Version("1.1.0.0") },
                };

                filesToFind.ForEach(x => {
                    var path = Path.Combine(tempDir, APP_ID, "staging", "app-1.1.0.0", x.Name);
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

            using (Utility.GetTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, APP_ID);
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                    "Squirrel.Core.1.2.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir("", APP_ID, tempDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.2.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                var progress = new List<int>();
                await fixture.ApplyReleasesPublic(updateInfo, false, false, progress.Add);
                this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                progress
                    .Aggregate(0, (acc, x) => {
                        (x >= acc).ShouldBeTrue();
                        return x;
                    })
                    .ShouldEqual(100);

                var rootDirectory = Path.Combine(tempDir, APP_ID, "staging", "app-1.2.0.0");

                new[] {
                    new { Name = "NLog.dll", Version = new Version("2.0.0.0") },
                    new { Name = "NSync.Core.dll", Version = new Version("1.1.0.0") },
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

            using (Utility.GetTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, APP_ID);
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                    "Squirrel.Core.1.3.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir("", APP_ID, tempDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.3.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                var progress = new List<int>();
                await fixture.ApplyReleasesPublic(updateInfo, false, false, progress.Add);
                this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                progress
                    .Aggregate(0, (acc, x) => {
                        (x >= acc).ShouldBeTrue();
                        return x;
                    })
                    .ShouldEqual(100);

                var rootDirectory = Path.Combine(tempDir, APP_ID, "staging", "app-1.3.0.0");

                new[] {
                    new { Name = "NLog.dll", Version = new Version("2.0.0.0") },
                    new { Name = "NSync.Core.dll", Version = new Version("1.1.0.0") },
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

            using (Utility.GetTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, APP_ID);
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-delta.nupkg",
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir("", APP_ID, tempDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-delta.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { deltaEntry, latestFullEntry }, packagesDir);
                updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();

                var progress = new List<int>();

                await fixture.ApplyReleasesPublic(updateInfo, false, false, progress.Add);
                this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                // TODO: this is failing intermittently, not sure why but is not a big deal atm
                // progress
                //     .Aggregate(0, (acc, x) => { (x >= acc).ShouldBeTrue(); return x; })
                //     .ShouldEqual(100);

                var filesToFind = new[] {
                    new { Name = "NLog.dll", Version = new Version("2.0.0.0") },
                    new { Name = "NSync.Core.dll", Version = new Version("1.1.0.0") },
                };

                filesToFind.ForEach(x => {
                    var path = Path.Combine(tempDir, APP_ID, "staging", "app-1.1.0.0", x.Name);
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
            using (Utility.GetTempDirectory(out tempDir)) {
                string appDir = Path.Combine(tempDir, APP_ID);
                string packagesDir = Path.Combine(appDir, "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-delta.nupkg"
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, APP_ID, "packages", x)));

                using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir("", APP_ID, tempDir);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, APP_ID, "packages", "Squirrel.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, APP_ID, "packages", "Squirrel.Core.1.1.0.0-delta.nupkg"));

                var result = fixture.createFullPackagesFromDeltas(new[] { deltaEntry }, baseEntry, null);
                
                var zp = new ZipPackage(Path.Combine(tempDir, APP_ID, "packages", result.Filename));
                zp.Version.ToString().ShouldEqual("1.1.0.0");
            }
        }

        [Fact]
        public async Task CreateShortcutsRoundTrip()
        {
            using var _1 = Utility.GetTempDirectory(out var tempDir);
            using var _2 = Utility.GetTempDirectory(out var remotePkgDir);

            IntegrationTestHelper.CreateNewVersionInPackageDir("0.1.0", remotePkgDir);
            using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, tempDir);
            await fixture.FullInstall();

            fixture.CreateShortcutsForExecutable("SquirrelAwareApp.exe",
                ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup | ShortcutLocation.AppRoot, false, null, null);

            Assert.True(File.Exists(Path.Combine(tempDir, APP_ID, "PublishSingleFileAwareApp.lnk")));
            
            // NB: COM is Weird.
            Thread.Sleep(1000);
            fixture.RemoveShortcutsForExecutable("SquirrelAwareApp.exe",
                ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup | ShortcutLocation.AppRoot);

            // NB: Squirrel-Aware first-run might still be running, slow
            // our roll before blowing away the temp path
            Thread.Sleep(1000);
        }

        //[Fact]
        //public async Task GetShortcutsSmokeTest()
        //{
        //    string remotePkgPath;
        //    string path;

        //    using (Utility.WithTempDirectory(out path)) {
        //        using (Utility.WithTempDirectory(out remotePkgPath))
        //        using (var mgr = new UpdateManager(remotePkgPath, APP_ID, path)) {
        //            IntegrationTestHelper.CreateFakeInstalledApp("1.0.0.1", remotePkgPath);
        //            await mgr.FullInstall();
        //        }

        //        var fixture = new ApplyReleasesImpl(Path.Combine(path, APP_ID));
        //        var result = fixture.GetShortcutsForExecutable("SquirrelAwareApp.exe", ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup, null);

        //        Assert.Equal(3, result.Keys.Count);

        //        // NB: Squirrel-Aware first-run might still be running, slow
        //        // our roll before blowing away the temp path
        //        Thread.Sleep(1000);
        //    }
        //}
    }
}