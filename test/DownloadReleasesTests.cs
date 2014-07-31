using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Splat;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class DownloadReleasesTests : IEnableLogger
    {
        [Fact]
        public void ChecksumShouldPassOnValidPackages()
        {
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
            fileInfo.Setup(x => x.Exists).Returns(true);
            fileInfo.Setup(x => x.Length).Returns(new FileInfo(nuGetPkg).Length);

            fs.Setup(x => x.GetFileInfo(Path.Combine(".", "theApp", "packages", filename))).Returns(fileInfo.Object);

            var fixture = ExposedObject.From(
                new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, ".", fs.Object, urlDownloader.Object));

            fixture.checksumPackage(entry);
        }

        [Fact]
        public void ChecksumShouldFailIfFilesAreMissing()
        {
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
                new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, ".", fs.Object, urlDownloader.Object));

            bool shouldDie = true;
            try {
                // NB: We can't use Assert.Throws here because the binder
                // will try to pick the wrong method
                fixture.checksumPackage(entry);
            } catch (Exception) {
                shouldDie = false;
            }

            shouldDie.ShouldBeFalse();
        }

        [Fact]
        public void ChecksumShouldFailIfFilesAreBogus()
        {
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
                new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, ".", fs.Object, urlDownloader.Object));

            bool shouldDie = true;
            try {
                fixture.checksumPackage(entry);
            } catch (Exception ex) {
                this.Log().InfoException("Checksum failure", ex);
                shouldDie = false;
            }

            shouldDie.ShouldBeFalse();
            fileInfo.Verify(x => x.Delete(), Times.Once());
        }

        [Fact]
        public void DownloadReleasesFromHttpServerIntegrationTest()
        {
            string tempDir = null;

            var updateDir = new DirectoryInfo(IntegrationTestHelper.GetPath("..", "SampleUpdatingApp", "SampleReleasesFolder"));

            IDisposable disp;
            try {
                var httpServer = new StaticHttpServer(30405, updateDir.FullName);
                disp = httpServer.Start();
            }
            catch (HttpListenerException) {
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

                var fixture = new UpdateManager("http://localhost:30405", "SampleUpdatingApp", FrameworkVersion.Net40, tempDir);
                using (fixture) {
                    var progress = new ReplaySubject<int>();

                    fixture.DownloadReleases(entriesToDownload, progress).First();
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress.Buffer(2,1).All(x => x.Count != 2 || x[1] > x[0]).First().ShouldBeTrue();
                    progress.Last().ShouldEqual(100);
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
        }

        [Fact]
        public void DownloadReleasesFromFileDirectoryIntegrationTest()
        {
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

                var fixture = new UpdateManager(updateDir.FullName, "SampleUpdatingApp", FrameworkVersion.Net40, tempDir);
                using (fixture) {
                    var progress = new ReplaySubject<int>();

                    fixture.DownloadReleases(entriesToDownload, progress).First();
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress.Buffer(2,1).All(x => x.Count != 2 || x[1] > x[0]).First().ShouldBeTrue();
                    progress.Last().ShouldEqual(100);
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
        }
    }
}
