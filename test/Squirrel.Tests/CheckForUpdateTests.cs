using System;
using System.IO;
using System.Linq;
using System.Text;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class CheckForUpdateTests
    {
        [Fact(Skip = "Rewrite this to be an integration test")]
        public void NewReleasesShouldBeDetected()
        {
            Assert.False(true, "Rewrite this to be an integration test");
            /*
            string localReleasesFile = Path.Combine(".", "theApp", "packages", "RELEASES");

            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.Setup(x => x.OpenRead())
                .Returns(File.OpenRead(IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOh")));

            var fs = new Mock<IFileSystemFactory>();
            fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);

            var urlDownloader = new Mock<IUrlDownloader>();
            var dlPath = IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOne");
            urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
                .Returns(Observable.Return(File.ReadAllText(dlPath, Encoding.UTF8)));

            var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);
            var result = default(UpdateInfo);

            using (fixture) {
                result = fixture.CheckForUpdate().First();
            }

            Assert.NotNull(result);
            Assert.Equal(1, result.ReleasesToApply.Single().Version.Major);
            Assert.Equal(1, result.ReleasesToApply.Single().Version.Minor);
            */
        }

        [Fact(Skip = "Rewrite this to be an integration test")]
        public void CorruptedReleaseFileMeansWeStartFromScratch()
        {
            Assert.False(true, "Rewrite this to be an integration test");

            /*
            string localPackagesDir = Path.Combine(".", "theApp", "packages");
            string localReleasesFile = Path.Combine(localPackagesDir, "RELEASES");

            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.Setup(x => x.Exists).Returns(true);
            fileInfo.Setup(x => x.OpenRead())
                .Returns(new MemoryStream(Encoding.UTF8.GetBytes("lol this isn't right")));

            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.Setup(x => x.Exists).Returns(true);

            var fs = new Mock<IFileSystemFactory>();
            fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);
            fs.Setup(x => x.CreateDirectoryRecursive(localPackagesDir)).Verifiable();
            fs.Setup(x => x.DeleteDirectoryRecursive(localPackagesDir)).Verifiable();
            fs.Setup(x => x.GetDirectoryInfo(localPackagesDir)).Returns(dirInfo.Object);

            var urlDownloader = new Mock<IUrlDownloader>();
            var dlPath = IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOne");
            urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
                .Returns(Observable.Return(File.ReadAllText(dlPath, Encoding.UTF8)));

            var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);
            using (fixture) {
                fixture.CheckForUpdate().First();
            }

            fs.Verify(x => x.CreateDirectoryRecursive(localPackagesDir), Times.Once());
            fs.Verify(x => x.DeleteDirectoryRecursive(localPackagesDir), Times.Once());
            */
        }

        [Fact(Skip = "Rewrite this to be an integration test")]
        public void CorruptRemoteFileShouldThrowOnCheck()
        {
            Assert.False(true, "Rewrite this to be an integration test");

            /*
            string localPackagesDir = Path.Combine(".", "theApp", "packages");
            string localReleasesFile = Path.Combine(localPackagesDir, "RELEASES");

            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.Setup(x => x.Exists).Returns(false);

            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.Setup(x => x.Exists).Returns(true);

            var fs = new Mock<IFileSystemFactory>();
            fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);
            fs.Setup(x => x.CreateDirectoryRecursive(localPackagesDir)).Verifiable();
            fs.Setup(x => x.DeleteDirectoryRecursive(localPackagesDir)).Verifiable();
            fs.Setup(x => x.GetDirectoryInfo(localPackagesDir)).Returns(dirInfo.Object);

            var urlDownloader = new Mock<IUrlDownloader>();
            urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
                .Returns(Observable.Return("lol this isn't right"));

            var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);

            using (fixture) {
                Assert.Throws<Exception>(() => fixture.CheckForUpdate().First());   
            }
            */
        }

        [Fact(Skip = "TODO")]
        public void IfLocalVersionGreaterThanRemoteWeRollback()
        {
            throw new NotImplementedException();
        }

        [Fact(Skip = "TODO")]
        public void IfLocalAndRemoteAreEqualThenDoNothing()
        {
            throw new NotImplementedException();
        }
    }
}
