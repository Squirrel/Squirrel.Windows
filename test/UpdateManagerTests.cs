using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squirrel;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class UpdateManagerTests
    {
        public class UpdateLocalReleasesTests
        {
            [Fact]
            public async Task UpdateLocalReleasesSmokeTest()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir)) {
                    var packageDir = Directory.CreateDirectory(Path.Combine(tempDir, "theApp", "packages"));

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, "theApp", "packages", x)));

                    var fixture = new UpdateManager.ApplyReleasesImpl(tempDir);

                    await fixture.updateLocalReleasesFile();

                    var releasePath = Path.Combine(packageDir.FullName, "RELEASES");
                    File.Exists(releasePath).ShouldBeTrue();

                    var entries = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasePath, Encoding.UTF8));
                    entries.Count().ShouldEqual(3);
                }
            }

            [Fact]
            public async Task WhenBothFilesAreInSyncNoUpdatesAreApplied()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir))
                {
                    var localPackages = Path.Combine(tempDir, "theApp", "packages");
                    var remotePackages = Path.Combine(tempDir, "releases");
                    Directory.CreateDirectory(localPackages);
                    Directory.CreateDirectory(remotePackages);

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x =>
                    {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(localPackages, x));
                        File.Copy(path, Path.Combine(remotePackages, x));
                    });

                    var fixture = new UpdateManager.ApplyReleasesImpl(tempDir);
                        
                    // sync both release files
                    await fixture.updateLocalReleasesFile();
                    ReleaseEntry.BuildReleasesFile(remotePackages);

                    // check for an update
                    UpdateInfo updateInfo;
                    using (var mgr = new UpdateManager(remotePackages, "theApp", FrameworkVersion.Net40, tempDir, new FakeUrlDownloader())) {
                        updateInfo = await mgr.CheckForUpdate();
                    }

                    Assert.NotNull(updateInfo);
                    Assert.Empty(updateInfo.ReleasesToApply);
                }
            }

            [Fact]
            public async Task WhenRemoteReleasesDoNotHaveDeltasNoUpdatesAreApplied()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir))
                {
                    var localPackages = Path.Combine(tempDir, "theApp", "packages");
                    var remotePackages = Path.Combine(tempDir, "releases");
                    Directory.CreateDirectory(localPackages);
                    Directory.CreateDirectory(remotePackages);

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x =>
                    {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(localPackages, x));
                    });

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x =>
                    {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(remotePackages, x));
                    });

                    var fixture = new UpdateManager.ApplyReleasesImpl(tempDir);

                    // sync both release files
                    await fixture.updateLocalReleasesFile();
                    ReleaseEntry.BuildReleasesFile(remotePackages);

                    UpdateInfo updateInfo;
                    using (var mgr = new UpdateManager(remotePackages, "theApp", FrameworkVersion.Net40, tempDir, new FakeUrlDownloader())) {
                        updateInfo = await mgr.CheckForUpdate();
                    }

                    Assert.NotNull(updateInfo);
                    Assert.Empty(updateInfo.ReleasesToApply);
                }
            }

            [Fact]
            public async Task WhenTwoRemoteUpdatesAreAvailableChoosesDeltaVersion()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir))
                {
                    var localPackages = Path.Combine(tempDir, "theApp", "packages");
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

                    var fixture = new UpdateManager.ApplyReleasesImpl(tempDir);

                    // sync both release files
                    await fixture.updateLocalReleasesFile();
                    ReleaseEntry.BuildReleasesFile(remotePackages);

                    using (var mgr = new UpdateManager(remotePackages, "theApp", FrameworkVersion.Net40, tempDir, new FakeUrlDownloader())) {
                        UpdateInfo updateInfo;
                        updateInfo = await mgr.CheckForUpdate();
                        Assert.True(updateInfo.ReleasesToApply.First().IsDelta);

                        updateInfo = await mgr.CheckForUpdate(ignoreDeltaUpdates: true);
                        Assert.False(updateInfo.ReleasesToApply.First().IsDelta);
                    }
                }
            }

            [Fact]
            public async Task WhenFolderDoesNotExistThrowHelpfulError()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir)) {
                    var directory = Path.Combine(tempDir, "missing-folder");
                    var fixture = new UpdateManager(directory, "MyAppName", FrameworkVersion.Net40);

                    using (fixture) {
                        await Assert.ThrowsAsync<Exception>(() => fixture.CheckForUpdate());
                    }
                }
            }

            [Fact]
            public async Task WhenReleasesFileDoesntExistThrowACustomError()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir)) {
                    var fixture = new UpdateManager(tempDir, "MyAppName", FrameworkVersion.Net40);

                    using (fixture) {
                        await Assert.ThrowsAsync<Exception>(() => fixture.CheckForUpdate());
                    }
                }
            }

            [Fact]
            public async Task WhenReleasesFileIsBlankReturnNull()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir)) {
                    var fixture = new UpdateManager(tempDir, "MyAppName", FrameworkVersion.Net40);
                    File.WriteAllText(Path.Combine(tempDir, "RELEASES"), "");

                    using (fixture) {
                        Assert.Null(await fixture.CheckForUpdate());
                    }
                }
            }

            [Fact]
            public async Task WhenUrlResultsInWebExceptionReturnNull()
            {
                // This should result in a WebException (which gets caught) unless you can actually access http://lol
                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net45);
                var updateInfo = await fixture.CheckForUpdate();
                Assert.Null(updateInfo);
            }
        }
    }
}
