using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Squirrel;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class UpdateManagerTests
    {
        public class TimingOutUrlDownloader : IUrlDownloader
        {
            public IObservable<string> DownloadUrl(string url, IObserver<int> progress = null)
            {
                return Observable.Start<string>(new Func<string>(() => { throw new TimeoutException(); }));
            }

            public IObservable<Unit> QueueBackgroundDownloads(IEnumerable<string> urls, IEnumerable<string> localPaths, IObserver<int> progress = null)
            {
                throw new NotImplementedException();
            }
        }

        public class UpdateLocalReleasesTests
        {
            [Fact]
            public void UpdateLocalReleasesSmokeTest()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir)) {
                    var packageDir = Directory.CreateDirectory(Path.Combine(tempDir, "theApp", "packages"));

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, "theApp", "packages", x)));

                    var urlDownloader = new Mock<IUrlDownloader>();
                    var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, urlDownloader.Object);

                    using (fixture) {
                        fixture.UpdateLocalReleasesFile().Last();
                    }

                    var releasePath = Path.Combine(packageDir.FullName, "RELEASES");
                    File.Exists(releasePath).ShouldBeTrue();

                    var entries = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasePath, Encoding.UTF8));
                    entries.Count().ShouldEqual(3);
                }
            }

            [Fact]
            public void WhenBothFilesAreInSyncNoUpdatesAreApplied()
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

                    var urlDownloader = new Mock<IUrlDownloader>();
                    var fixture = new UpdateManager(remotePackages, "theApp", FrameworkVersion.Net40, tempDir, null, urlDownloader.Object);

                    UpdateInfo updateInfo;
                    using (fixture)
                    {
                        // sync both release files
                        fixture.UpdateLocalReleasesFile().Last();
                        ReleaseEntry.BuildReleasesFile(remotePackages);

                        // check for an update
                        updateInfo = fixture.CheckForUpdate().Wait();
                    }

                    Assert.NotNull(updateInfo);
                    Assert.Empty(updateInfo.ReleasesToApply);
                }
            }

            [Fact]
            public void WhenRemoteReleasesDoNotHaveDeltasNoUpdatesAreApplied()
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

                    var urlDownloader = new Mock<IUrlDownloader>();
                    var fixture = new UpdateManager(remotePackages, "theApp", FrameworkVersion.Net40, tempDir, null, urlDownloader.Object);

                    UpdateInfo updateInfo;
                    using (fixture)
                    {
                        // sync both release files
                        fixture.UpdateLocalReleasesFile().Last();
                        ReleaseEntry.BuildReleasesFile(remotePackages);

                        // check for an update
                        updateInfo = fixture.CheckForUpdate().Wait();
                    }

                    Assert.NotNull(updateInfo);
                    Assert.Empty(updateInfo.ReleasesToApply);
                }
            }

            [Fact]
            public void WhenTwoRemoteUpdatesAreAvailableChoosesDeltaVersion()
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
                    }.ForEach(x =>
                    {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(localPackages, x));
                    });

                    new[] {
                        "Squirrel.Core.1.0.0.0-full.nupkg",
                        "Squirrel.Core.1.1.0.0-delta.nupkg",
                        "Squirrel.Core.1.1.0.0-full.nupkg",
                    }.ForEach(x =>
                    {
                        var path = IntegrationTestHelper.GetPath("fixtures", x);
                        File.Copy(path, Path.Combine(remotePackages, x));
                    });

                    var urlDownloader = new Mock<IUrlDownloader>();
                    var fixture = new UpdateManager(remotePackages, "theApp", FrameworkVersion.Net40, tempDir, null, urlDownloader.Object);

                    UpdateInfo updateInfo;
                    using (fixture)
                    {
                        // sync both release files
                        fixture.UpdateLocalReleasesFile().Last();
                        ReleaseEntry.BuildReleasesFile(remotePackages);

                        updateInfo = fixture.CheckForUpdate().Wait();

                        Assert.True(updateInfo.ReleasesToApply.First().IsDelta);

                        updateInfo = fixture.CheckForUpdate(ignoreDeltaUpdates:true).Wait();

                        Assert.False(updateInfo.ReleasesToApply.First().IsDelta);
                    }
                }

            }

            [Fact]
            public void WhenFolderDoesNotExistThrowHelpfulError()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir)) {
                    var directory = Path.Combine(tempDir, "missing-folder");
                    var fixture = new UpdateManager(directory, "MyAppName", FrameworkVersion.Net40);

                    using (fixture) {
                        Assert.Throws<SquirrelConfigurationException>(
                            () => fixture.CheckForUpdate().Wait());
                    }
                }
            }

            [Fact]
            public void WhenReleasesFileDoesntExistThrowACustomError()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir)) {
                    var fixture = new UpdateManager(tempDir, "MyAppName", FrameworkVersion.Net40);

                    using (fixture) {
                        Assert.Throws<SquirrelConfigurationException>(
                            () => fixture.CheckForUpdate().Wait());
                    }
                }
            }

            [Fact]
            public void WhenReleasesFileIsBlankReturnNull()
            {
                string tempDir;
                using (Utility.WithTempDirectory(out tempDir)) {
                    var fixture = new UpdateManager(tempDir, "MyAppName", FrameworkVersion.Net40);
                    File.WriteAllText(Path.Combine(tempDir, "RELEASES"), "");

                    using (fixture) {
                        Assert.Null(fixture.CheckForUpdate().Wait());
                    }
                }
            }

            [Fact]
            public void WhenUrlTimesOutReturnNull()
            {
                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net45, null, null, new TimingOutUrlDownloader());

                var updateInfo = fixture.CheckForUpdate().Wait();

                Assert.Null(updateInfo);
            }

            [Fact]
            public void WhenUrlResultsInWebExceptionReturnNull()
            {
                // This should result in a WebException (which gets caught) unless you can actually access http://lol

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net45);

                var updateInfo = fixture.CheckForUpdate().Wait();

                Assert.Null(updateInfo);
            }
        }
    }
}
