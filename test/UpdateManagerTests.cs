using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel;
using Squirrel.Tests.TestHelpers;
using Xunit;
using System.Net;
using Squirrel.NuGet;
using System.Net.Http;
using NuGet.Versioning;

namespace Squirrel.Tests
{
    public class UpdateManagerTests
    {
        public const string APP_ID = "theFakeApp";

        public class CreateUninstallerRegKeyTests
        {
            [Fact]
            public async Task CallingMethodTwiceShouldUpdateInstaller()
            {
                using var _1 = Utility.GetTempDirectory(out var path);
                using var _2 = Utility.GetTempDirectory(out var remotePkgDir);

                IntegrationTestHelper.CreateNewVersionInPackageDir("0.1.0", remotePkgDir);
                using (var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, path))
                    await fixture.FullInstall();

                using (var mgr = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePkgDir, APP_ID, path)) {
                    await mgr.CreateUninstallerRegistryEntry();
                    var regKey = await mgr.CreateUninstallerRegistryEntry();

                    Assert.False(String.IsNullOrWhiteSpace((string) regKey.GetValue("DisplayName")));

                    mgr.RemoveUninstallerRegistryEntry();
                }

                // NB: Squirrel-Aware first-run might still be running, slow
                // our roll before blowing away the temp path
                Thread.Sleep(1000);

                var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                using (key) {
                    Assert.False(key.GetSubKeyNames().Contains(APP_ID));
                }
            }

            public class UpdateLocalReleasesTests
            {
                [Fact]
                public async Task UpdateLocalReleasesSmokeTest()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);

                    var appDir = Path.Combine(tempDir, APP_ID);
                    var packageDir = Directory.CreateDirectory(Path.Combine(appDir, "packages"));

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, APP_ID, "packages", x)));

                    var info = new AppDescWindows(appDir, APP_ID);
                    ReleaseEntry.BuildReleasesFile(info.PackagesDir);

                    var releasePath = Path.Combine(packageDir.FullName, "RELEASES");
                    File.Exists(releasePath).ShouldBeTrue();

                    var entries = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasePath, Encoding.UTF8));
                    entries.Count().ShouldEqual(3);
                }

                [Fact]
                public async Task InitialInstallSmokeTest()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);

                    var remotePackageDir = Directory.CreateDirectory(Path.Combine(tempDir, "remotePackages"));
                    var localAppDir = Path.Combine(tempDir, APP_ID);

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                    }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(remotePackageDir.FullName, x)));

                    using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePackageDir.FullName, APP_ID, tempDir);
                    await fixture.FullInstall();

                    var releasePath = Path.Combine(localAppDir, "packages", "RELEASES");
                    File.Exists(releasePath).ShouldBeTrue();

                    var entries = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasePath, Encoding.UTF8));
                    entries.Count().ShouldEqual(1);

                    Assert.True(File.Exists(Path.Combine(localAppDir, "current", "ReactiveUI.dll")));
                    Assert.True(File.Exists(Path.Combine(localAppDir, "current", "NSync.Core.dll")));

                    var manifest = NuspecManifest.ParseFromFile(Path.Combine(localAppDir, "current", Utility.SpecVersionFileName));
                    Assert.Equal(new NuGetVersion(1, 0, 0, 0), manifest.Version);
                }

                [Fact]
                public async Task SpecialCharactersInitialInstallTest()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);

                    var remotePackageDir = Directory.CreateDirectory(Path.Combine(tempDir, "remotePackages"));
                    var localAppDir = Path.Combine(tempDir, APP_ID);

                    new[] {
                        "SpecialCharacters-0.1.0-full.nupkg",
                    }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(remotePackageDir.FullName, x)));

                    using var fixture = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePackageDir.FullName, APP_ID, tempDir);
                    await fixture.FullInstall();

                    var releasePath = Path.Combine(localAppDir, "packages", "RELEASES");
                    File.Exists(releasePath).ShouldBeTrue();

                    var entries = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasePath, Encoding.UTF8));
                    entries.Count().ShouldEqual(1);

                    new[] {
                        "file space name.txt"
                    }.ForEach(x => File.Exists(Path.Combine(localAppDir, "current", x)).ShouldBeTrue());
                }

                [Fact]
                public async Task WhenBothFilesAreInSyncNoUpdatesAreApplied()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);

                    var appDir = Path.Combine(tempDir, APP_ID);
                    var localPackages = Path.Combine(appDir, "packages");
                    var remotePackages = Path.Combine(tempDir, "releases");
                    Directory.CreateDirectory(localPackages);
                    Directory.CreateDirectory(remotePackages);

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x => {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(localPackages, x));
                        File.Copy(path, Path.Combine(remotePackages, x));
                    });

                    // sync both release files
                    var info = new AppDescWindows(appDir, APP_ID);
                    ReleaseEntry.BuildReleasesFile(info.PackagesDir);
                    ReleaseEntry.BuildReleasesFile(remotePackages);

                    // check for an update
                    using var mgr = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePackages, APP_ID, tempDir);
                    UpdateInfo updateInfo = await mgr.CheckForUpdate();

                    Assert.NotNull(updateInfo);
                    Assert.Empty(updateInfo.ReleasesToApply);
                }

                [Fact]
                public async Task WhenRemoteReleasesDoNotHaveDeltasNoUpdatesAreApplied()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);

                    var appDir = Path.Combine(tempDir, APP_ID);
                    var localPackages = Path.Combine(appDir, "packages");
                    var remotePackages = Path.Combine(tempDir, "releases");
                    Directory.CreateDirectory(localPackages);
                    Directory.CreateDirectory(remotePackages);

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x => {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(localPackages, x));
                    });

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x => {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(remotePackages, x));
                    });

                    // sync both release files
                    var info = new AppDescWindows(appDir, APP_ID);
                    ReleaseEntry.BuildReleasesFile(info.PackagesDir);
                    ReleaseEntry.BuildReleasesFile(remotePackages);

                    using var mgr = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePackages, APP_ID, tempDir);
                    UpdateInfo updateInfo = await mgr.CheckForUpdate();

                    Assert.NotNull(updateInfo);
                    Assert.Empty(updateInfo.ReleasesToApply);
                }

                [Fact]
                public async Task WhenTwoRemoteUpdatesAreAvailableChoosesDeltaVersion()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);

                    var appDir = Path.Combine(tempDir, APP_ID);
                    var localPackages = Path.Combine(appDir, "packages");
                    var remotePackages = Path.Combine(tempDir, "releases");
                    Directory.CreateDirectory(localPackages);
                    Directory.CreateDirectory(remotePackages);

                    new[] { "Squirrel.Core.1.0.0.0-full.nupkg", }.ForEach(x => {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(localPackages, x));
                    });

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x => {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(remotePackages, x));
                    });

                    // sync both release files
                    var info = new AppDescWindows(appDir, APP_ID);
                    ReleaseEntry.BuildReleasesFile(info.PackagesDir);
                    ReleaseEntry.BuildReleasesFile(remotePackages);

                    using var mgr = UpdateManagerTestImpl.FromLocalPackageTempDir(remotePackages, APP_ID, tempDir);
                    UpdateInfo updateInfo = await mgr.CheckForUpdate();
                    
                    Assert.True(updateInfo.ReleasesToApply.First().IsDelta);

                    updateInfo = await mgr.CheckForUpdate(ignoreDeltaUpdates: true);
                    Assert.False(updateInfo.ReleasesToApply.First().IsDelta);
                }

                [Fact]
                public async Task WhenFolderDoesNotExistThrowHelpfulError()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);
                    var directory = Path.Combine(tempDir, "missing-folder");
                    using var fixture = new UpdateManager(directory);
                    await Assert.ThrowsAsync<Exception>(() => fixture.CheckForUpdate());
                }

                [Fact]
                public async Task WhenReleasesFileDoesntExistThrowACustomError()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);
                    using var fixture = new UpdateManager(tempDir);
                    await Assert.ThrowsAsync<Exception>(() => fixture.CheckForUpdate());
                }

                [Fact]
                public async Task WhenReleasesFileIsBlankThrowAnException()
                {
                    using var _1 = Utility.GetTempDirectory(out var tempDir);
                    using var fixture = new UpdateManager(tempDir);
                    File.WriteAllText(Path.Combine(tempDir, "RELEASES"), "");
                    await Assert.ThrowsAsync(typeof(Exception), () => fixture.CheckForUpdate());
                }

                [Fact]
                public async Task WhenUrlResultsInWebExceptionWeShouldThrow()
                {
                    // This should result in a WebException (which gets caught) unless you can actually access http://lol
                    using var fixture = new UpdateManager("http://lol");
                    await Assert.ThrowsAsync(typeof(HttpRequestException), () => fixture.CheckForUpdate());
                }

                [Fact]
                public void IsInstalledHandlesInvalidDirectoryStructure()
                {
                    using (Utility.GetTempDirectory(out var tempDir)) {
                        Directory.CreateDirectory(Path.Combine(tempDir, APP_ID));
                        Directory.CreateDirectory(Path.Combine(tempDir, APP_ID, "app-1.0.1"));
                        Directory.CreateDirectory(Path.Combine(tempDir, APP_ID, "wrongDir"));
                        File.WriteAllText(Path.Combine(tempDir, APP_ID, "Update.exe"), "1");
                        using (var fixture = UpdateManagerTestImpl.FromFakeWebSource("http://lol", APP_ID, tempDir)) {
                            Assert.Null(new AppDescWindows(Path.Combine(tempDir, "app.exe")).CurrentlyInstalledVersion);
                            Assert.Null(new AppDescWindows(Path.Combine(tempDir, APP_ID, "app.exe")).CurrentlyInstalledVersion);
                            Assert.Null(new AppDescWindows(Path.Combine(tempDir, APP_ID, "wrongDir", "app.exe")).CurrentlyInstalledVersion);
                            Assert.Equal(new SemanticVersion(1, 0, 9),
                                new AppDescWindows(Path.Combine(tempDir, APP_ID, "app-1.0.9", "app.exe")).CurrentlyInstalledVersion);
                        }
                    }
                }

                [Fact]
                public void CurrentlyInstalledVersionDoesNotThrow()
                {
                    using var fixture = new UpdateManager();
                    Assert.Null(fixture.CurrentlyInstalledVersion());
                    Assert.False(fixture.IsInstalledApp);
                }

                [Theory]
                [InlineData(0, 0, 25, 0)]
                [InlineData(12, 0, 25, 3)]
                [InlineData(55, 0, 25, 13)]
                [InlineData(100, 0, 25, 25)]
                [InlineData(0, 25, 50, 25)]
                [InlineData(12, 25, 50, 28)]
                [InlineData(55, 25, 50, 38)]
                [InlineData(100, 25, 50, 50)]
                public void CalculatesPercentageCorrectly(int percentageOfCurrentStep, int stepStartPercentage, int stepEndPercentage, int expectedPercentage)
                {
                    var percentage = UpdateManager.CalculateProgress(percentageOfCurrentStep, stepStartPercentage, stepEndPercentage);

                    Assert.Equal(expectedPercentage, percentage);
                }

                [Fact]
                public void CalculatesPercentageCorrectlyForUpdateExe()
                {
                    // Note: this mimicks the update.exe progress reporting of multiple steps

                    var progress = new List<int>();

                    // 3 % (3 stages), check for updates
                    foreach (var step in new[] { 0, 33, 66, 100 }) {
                        progress.Add(UpdateManager.CalculateProgress(step, 0, 3));

                        Assert.InRange(progress.Last(), 0, 3);
                    }

                    Assert.Equal(3, progress.Last());

                    // 3 - 30 %, download releases
                    for (var step = 0; step <= 100; step++) {
                        progress.Add(UpdateManager.CalculateProgress(step, 3, 30));

                        Assert.InRange(progress.Last(), 3, 30);
                    }

                    Assert.Equal(30, progress.Last());

                    // 30 - 100 %, apply releases
                    for (var step = 0; step <= 100; step++) {
                        progress.Add(UpdateManager.CalculateProgress(step, 30, 100));

                        Assert.InRange(progress.Last(), 30, 100);
                    }

                    Assert.Equal(100, progress.Last());
                }
            }
        }
    }
}