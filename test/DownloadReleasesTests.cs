using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Splat;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class DownloadReleasesTests : IEnableLogger
    {
        [Fact(Skip = "Rewrite this to be an integration test")]
        public void ChecksumShouldFailIfFilesAreMissing()
        {
            Assert.False(true, "Rewrite this to be an integration test");

            /*
            var filename = "Squirrel.Core.1.0.0.0.nupkg";
            var nuGetPkg = IntegrationTestHelper.GetPath("fixtures", filename);
            var fs = new Mock<IFileSystemFactory>();
            var urlDownloader = new Mock<IUrlDownloader>();

            ReleaseEntry entry;
            using (var f = File.OpenRead(nuGetPkg)) {
                entry = ReleaseEntry.GenerateFromFile(f, filename);
            }

            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.Setup(x => x.OpenRead()).Returns(File.OpenRead(nuGetPkg));
            fileInfo.Setup(x => x.Exists).Returns(false);

            fs.Setup(x => x.GetFileInfo(Path.Combine(".", "theApp", "packages", filename))).Returns(fileInfo.Object);

            var fixture = ExposedObject.From(
                new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object));

            bool shouldDie = true;
            try {
                // NB: We can't use Assert.Throws here because the binder
                // will try to pick the wrong method
                fixture.checksumPackage(entry);
            } catch (Exception) {
                shouldDie = false;
            }

            shouldDie.ShouldBeFalse();
            */
        }

        [Fact(Skip = "Rewrite this to be an integration test")]
        public void ChecksumShouldFailIfFilesAreBogus()
        {
            Assert.False(true, "Rewrite this to be an integration test");

            /*
            var filename = "Squirrel.Core.1.0.0.0.nupkg";
            var nuGetPkg = IntegrationTestHelper.GetPath("fixtures", filename);
            var fs = new Mock<IFileSystemFactory>();
            var urlDownloader = new Mock<IUrlDownloader>();

            ReleaseEntry entry;
            using (var f = File.OpenRead(nuGetPkg)) {
                entry = ReleaseEntry.GenerateFromFile(f, filename);
            }

            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.Setup(x => x.OpenRead()).Returns(new MemoryStream(Encoding.UTF8.GetBytes("Lol broken")));
            fileInfo.Setup(x => x.Exists).Returns(true);
            fileInfo.Setup(x => x.Length).Returns(new FileInfo(nuGetPkg).Length);
            fileInfo.Setup(x => x.Delete()).Verifiable();

            fs.Setup(x => x.GetFileInfo(Path.Combine(".", "theApp", "packages", filename))).Returns(fileInfo.Object);

            var fixture = ExposedObject.From(
                new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object));

            bool shouldDie = true;
            try {
                fixture.checksumPackage(entry);
            } catch (Exception ex) {
                this.Log().InfoException("Checksum failure", ex);
                shouldDie = false;
            }

            shouldDie.ShouldBeFalse();
            fileInfo.Verify(x => x.Delete(), Times.Once());
            */
        }

        [Fact(Skip = "Rewrite this to be an integration test")]
        public async Task DownloadReleasesFromHttpServerIntegrationTest()
        {
            Assert.False(true, "Rewrite this to not use the SampleUpdatingApp");

            /*
            string tempDir = null;

            var updateDir = new DirectoryInfo(IntegrationTestHelper.GetPath("..", "SampleUpdatingApp", "SampleReleasesFolder"));

            IDisposable disp;
            try {
                var httpServer = new StaticHttpServer(30405, updateDir.FullName);
                disp = httpServer.Start();
            } catch (HttpListenerException) {
                Assert.False(true, @"Windows sucks, go run 'netsh http add urlacl url=http://+:30405/ user=MYMACHINE\MyUser");
                return;
            }

            var entriesToDownload = updateDir.GetFiles("*.nupkg")
                .Select(x => ReleaseEntry.GenerateFromFile(x.FullName))
                .ToArray();

            entriesToDownload.Count().ShouldBeGreaterThan(0);

            using (disp)
            using (Utility.WithTempDirectory(out tempDir)) {
                // NB: This is normally done by CheckForUpdates, but since 
                // we're skipping that in the test we have to do it ourselves
                Directory.CreateDirectory(Path.Combine(tempDir, "SampleUpdatingApp", "packages"));

                var fixture = new UpdateManager("http://localhost:30405", "SampleUpdatingApp", tempDir);
                using (fixture) {
                    var progress = new List<int>();
                    await fixture.DownloadReleases(entriesToDownload, progress.Add);

                    progress
                        .Aggregate(0, (acc, x) => { x.ShouldBeGreaterThan(acc); return x; })
                        .ShouldEqual(100);
                }

                entriesToDownload.ForEach(x => {
                    this.Log().Info("Looking for {0}", x.Filename);
                    var actualFile = Path.Combine(tempDir, "SampleUpdatingApp", "packages", x.Filename);
                    File.Exists(actualFile).ShouldBeTrue();

                    var actualEntry = ReleaseEntry.GenerateFromFile(actualFile);
                    actualEntry.SHA1.ShouldEqual(x.SHA1);
                    actualEntry.Version.ShouldEqual(x.Version);
                });
            }
            */
        }

        [Fact(Skip = "Rewrite this to be an integration test")]
        public async Task DownloadReleasesFromFileDirectoryIntegrationTest()
        {
            Assert.False(true, "Rewrite this to not use the SampleUpdatingApp");

            /*
            string tempDir = null;

            var updateDir = new DirectoryInfo(IntegrationTestHelper.GetPath("..", "SampleUpdatingApp", "SampleReleasesFolder"));

            var entriesToDownload = updateDir.GetFiles("*.nupkg")
                .Select(x => ReleaseEntry.GenerateFromFile(x.FullName))
                .ToArray();

            entriesToDownload.Count().ShouldBeGreaterThan(0);

            using (Utility.WithTempDirectory(out tempDir)) {
                // NB: This is normally done by CheckForUpdates, but since 
                // we're skipping that in the test we have to do it ourselves
                Directory.CreateDirectory(Path.Combine(tempDir, "SampleUpdatingApp", "packages"));

                var fixture = new UpdateManager(updateDir.FullName, "SampleUpdatingApp", tempDir);
                using (fixture) {
                    var progress = new List<int>();

                    await fixture.DownloadReleases(entriesToDownload, progress.Add);
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress
                        .Aggregate(0, (acc, x) => { x.ShouldBeGreaterThan(acc); return x; })
                        .ShouldEqual(100);
                }

                entriesToDownload.ForEach(x => {
                    this.Log().Info("Looking for {0}", x.Filename);
                    var actualFile = Path.Combine(tempDir, "SampleUpdatingApp", "packages", x.Filename);
                    File.Exists(actualFile).ShouldBeTrue();

                    var actualEntry = ReleaseEntry.GenerateFromFile(actualFile);
                    actualEntry.SHA1.ShouldEqual(x.SHA1);
                    actualEntry.Version.ShouldEqual(x.Version);
                });
            }
            */
        }
    }
}
