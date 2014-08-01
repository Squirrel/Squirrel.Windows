using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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

        public async Task DownloadFile(string url, string targetFile)
        {
        }
    }

    public class ApplyReleasesTests : IEnableLogger
    {
        [Fact]
        public void WhenNoNewReleasesAreAvailableTheListIsEmpty()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir)) {
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp"));
                var packages = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packages);

                var package = "Squirrel.Core.1.0.0.0-full.nupkg";
                File.Copy(IntegrationTestHelper.GetPath("fixtures", package), Path.Combine(packages, package));

                var aGivenPackage = Path.Combine(packages, package);
                var baseEntry = ReleaseEntry.GenerateFromFile(aGivenPackage);

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { baseEntry }, "dontcare", FrameworkVersion.Net40);

                Assert.Empty(updateInfo.ReleasesToApply);
            }
        }

        [Fact]
        public void ThrowsWhenOnlyDeltaReleasesAreAvailable()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir))
            {
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp"));
                var packages = Path.Combine(tempDir, "theApp", "packages");
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
                    () => UpdateInfo.Create(baseEntry, new[] { deltaEntry }, "dontcare", FrameworkVersion.Net40));
            }
        }

        [Fact]
        public async Task ApplyReleasesWithOneReleaseFile()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                string packagesDir = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.0.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                using (fixture) {
                    var progress = new List<int>();

                    await fixture.ApplyReleases(updateInfo, progress.Add);
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress
                        .Aggregate(0, (acc, x) => { x.ShouldBeGreaterThan(acc); return x; })
                        .ShouldEqual(100);
                }

                var filesToFind = new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                    new {Name = Path.Combine("sub", "Ionic.Zip.dll"), Version = new Version("1.9.1.8")},
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
                string packagesDir = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                    "Squirrel.Core.1.2.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.2.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                using (fixture) {
                    var progress = new List<int>();
                    await fixture.ApplyReleases(updateInfo, progress.Add);
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress
                        .Aggregate(0, (acc, x) => { x.ShouldBeGreaterThan(acc); return x; })
                        .ShouldEqual(100);
                }

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
                string packagesDir = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                    "Squirrel.Core.1.3.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.3.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                using (fixture) {
                    var progress = new List<int>();
                    await fixture.ApplyReleases(updateInfo, progress.Add);
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress
                        .Aggregate(0, (acc, x) => { x.ShouldBeGreaterThan(acc); return x; })
                        .ShouldEqual(100);
                }

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
                string packagesDir = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-delta.nupkg",
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-delta.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { deltaEntry, latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();

                using (fixture) {
                    var progress = new List<int>();

                    await fixture.ApplyReleases(updateInfo, progress.Add);
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress
                        .Aggregate(0, (acc, x) => { x.ShouldBeGreaterThan(acc); return x; })
                        .ShouldEqual(100);
                }

                var filesToFind = new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                    new {Name = Path.Combine("sub", "Ionic.Zip.dll"), Version = new Version("1.9.1.8")},
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
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp", "packages"));

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-delta.nupkg"
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, "theApp", "packages", x)));

                var urlDownloader = new FakeUrlDownloader();
                using (var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, urlDownloader)) {
                    var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Squirrel.Core.1.0.0.0-full.nupkg"));
                    var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Squirrel.Core.1.1.0.0-delta.nupkg"));

                    var resultObs = (Task<ReleaseEntry>)fixture.GetType().GetMethod("createFullPackagesFromDeltas", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(fixture, new object[] { new[] {deltaEntry}, baseEntry });

                    var result = await resultObs;
                    var zp = new ZipPackage(Path.Combine(tempDir, "theApp", "packages", result.Filename));
                    zp.Version.ToString().ShouldEqual("1.1.0.0");
                }
            }
        }

        [Fact]
        public async Task ExecutablesPinnedToTaskbarShouldPointToNewVersion()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                string packagesDir = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "SampleUpdatingApp.1.0.0.0.nupkg",
                    "SampleUpdatingApp.1.1.0.0.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "SampleUpdatingApp.1.0.0.0.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "SampleUpdatingApp.1.1.0.0.nupkg"));

                var updateInfo = UpdateInfo.Create(null, new[] { baseEntry }, packagesDir, FrameworkVersion.Net40);
                using (fixture) {
                    await fixture.ApplyReleases(updateInfo);
                }

                var oldExecutable = Path.Combine(tempDir, "theApp", "app-1.0.0.0", "SampleUpdatingApp.exe");
                File.Exists(oldExecutable).ShouldBeTrue();
                TaskbarHelper.PinToTaskbar(oldExecutable);

                updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                using (fixture) {
                    await fixture.ApplyReleases(updateInfo);
                }

                var newExecutable = Path.Combine(tempDir, "theApp", "app-1.1.0.0", "SampleUpdatingApp.exe");
                File.Exists(newExecutable).ShouldBeTrue();
                TaskbarHelper.IsPinnedToTaskbar(newExecutable).ShouldBeTrue();

                Utility.Retry(() => TaskbarHelper.UnpinFromTaskbar(newExecutable));
            }
        }
    }
}