using System;
using System.IO;
using System.Linq;
using Moq;
using Squirrel.Core;
using Squirrel.Tests.TestHelpers;
using Xunit;
using Xunit.Extensions;

namespace Squirrel.Tests.Core
{
    public class ReleaseEntryTests
    {
        [Theory]
        [InlineData("94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.nupkg  1004502", "MyCoolApp-1.0.nupkg", 1004502)]
        [InlineData("3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.nupkg  1040561", "MyCoolApp-1.1.nupkg", 1040561)]
        [InlineData("14db31d2647c6d2284882a2e101924a9c409ee67  MyCoolApp-1.1.nupkg.delta  80396", "MyCoolApp-1.1.nupkg.delta", 80396)]
        public void ParseValidReleaseEntryLines(string releaseEntry, string fileName, long fileSize)
        {
            var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);
            Assert.Equal(fileName, fixture.Filename);
            Assert.Equal(fileSize, fixture.Filesize);
        }

        [Theory]
        [InlineData("Squirrel.Core.1.0.0.0.nupkg", 4457, "75255cfd229a1ed1447abe1104f5635e69975d30")]
        [InlineData("Squirrel.Core.1.1.0.0.nupkg", 15830, "9baf1dbacb09940086c8c62d9a9dbe69fe1f7593")]
        public void GenerateFromFileTest(string name, long size, string sha1)
        {
            var path = IntegrationTestHelper.GetPath("fixtures", name);

            using (var f = File.OpenRead(path)) {
                var fixture = ReleaseEntry.GenerateFromFile(f, "dontcare");
                Assert.Equal(size, fixture.Filesize);
                Assert.Equal(sha1, fixture.SHA1.ToLowerInvariant());
            }
        }

        [Theory]
        [InlineData("94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.nupkg  1004502", 1, 0)]
        [InlineData("3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.nupkg  1040561", 1, 1)]
        [InlineData("14db31d2647c6d2284882a2e101924a9c409ee67  MyCoolApp-1.1-delta.nupkg  80396", 1, 1)]
        public void ParseVersionTest(string releaseEntry, int expectedMajor, int expectedMinor)
        {
            var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);

            Assert.Equal(expectedMajor, fixture.Version.Major);
            Assert.Equal(expectedMinor, fixture.Version.Minor);
        }

        [Fact]
        public void CanParseGeneratedReleaseEntryAsString()
        {
            var path = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Core.1.1.0.0.nupkg");
            var entryAsString = ReleaseEntry.GenerateFromFile(path).EntryAsString;
            ReleaseEntry.ParseReleaseEntry(entryAsString);
        }

        [Fact]
        public void InvalidReleaseNotesThrowsException()
        {
            var path = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Core.1.0.0.0.nupkg");
            var fixture = ReleaseEntry.GenerateFromFile(path);
            Assert.Throws<Exception>(() => fixture.GetReleaseNotes(IntegrationTestHelper.GetPath("fixtures")));
        }

        [Fact]
        public void GetLatestReleaseWithNullCollectionReturnsNull()
        {
            Assert.Null(ReleaseEntry.GetPreviousRelease(
                null, null, null));
        }

        [Fact]
        public void GetLatestReleaseWithEmptyCollectionReturnsNull()
        {
            Assert.Null(ReleaseEntry.GetPreviousRelease(
                Enumerable.Empty<ReleaseEntry>(), null, null));
        }

        [Fact]
        public void WhenCurrentReleaseMatchesLastReleaseReturnNull()
        {
            var package = Mock.Of<IReleasePackage>(
                r => r.InputPackageFile == "Espera-1.7.6-beta.nupkg");

            var releaseEntries = new[] {
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.6-beta.nupkg"))
            };
            Assert.Null(ReleaseEntry.GetPreviousRelease(
                releaseEntries, package, @"C:\temp\somefolder"));
        }

        [Fact]
        public void WhenMultipleReleaseMatchesReturnEarlierResult()
        {
            var expected = new Version("1.7.5");
            var package = Mock.Of<IReleasePackage>(
                r => r.InputPackageFile == "Espera-1.7.6-beta.nupkg");

            var releaseEntries = new[] {
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.6-beta.nupkg")),
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.5-beta.nupkg"))
            };

            var actual = ReleaseEntry.GetPreviousRelease(
                releaseEntries,
                package,
                @"C:\temp\");

            Assert.Equal(expected, actual.Version);
        }

        [Fact]
        public void WhenMultipleReleasesFoundReturnPreviousVersion()
        {
            var expected = new Version("1.7.6");
            var input = Mock.Of<IReleasePackage>(
                r => r.InputPackageFile == "Espera-1.7.7-beta.nupkg");

            var releaseEntries = new[] {
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.6-beta.nupkg")),
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.5-beta.nupkg"))
            };

            var actual = ReleaseEntry.GetPreviousRelease(
                releaseEntries,
                input,
                @"C:\temp\");

            Assert.Equal(expected, actual.Version);
        }

        [Fact]
        public void WhenMultipleReleasesFoundInOtherOrderReturnPreviousVersion()
        {
            var expected = new Version("1.7.6");
            var input = Mock.Of<IReleasePackage>(
                r => r.InputPackageFile == "Espera-1.7.7-beta.nupkg");

            var releaseEntries = new[] {
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.5-beta.nupkg")),
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.6-beta.nupkg"))
            };

            var actual = ReleaseEntry.GetPreviousRelease(
                releaseEntries,
                input,
                @"C:\temp\");

            Assert.Equal(expected, actual.Version);
        }

        [Fact]
        public void WhenReleasesAreOutOfOrderSortByVersion()
        {
            var path = Path.GetTempFileName();
            var firstVersion = new Version("1.0.0");
            var secondVersion = new Version("1.1.0");
            var thirdVersion = new Version("1.2.0");

            var releaseEntries = new[] {
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-delta.nupkg")),
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.0-delta.nupkg")),
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.0-full.nupkg")),
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-full.nupkg")),
                ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.0.0-full.nupkg"))
            };

            ReleaseEntry.WriteReleaseFile(releaseEntries, path);

            var releases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(path)).ToArray();

            Assert.Equal(firstVersion, releases[0].Version);
            Assert.Equal(secondVersion, releases[1].Version);
            Assert.Equal(true, releases[1].IsDelta);
            Assert.Equal(secondVersion, releases[2].Version);
            Assert.Equal(false, releases[2].IsDelta);
            Assert.Equal(thirdVersion, releases[3].Version);
            Assert.Equal(true, releases[3].IsDelta);
            Assert.Equal(thirdVersion, releases[4].Version);
            Assert.Equal(false, releases[4].IsDelta);
        }

        [Fact]
        public void ParseReleaseFileShouldReturnNothingForBlankFiles()
        {
            Assert.True(ReleaseEntry.ParseReleaseFile("").Count() == 0);
            Assert.True(ReleaseEntry.ParseReleaseFile(null).Count() == 0);
        }

        static string MockReleaseEntry(string name)
        {
            return string.Format("94689fede03fed7ab59c24337673a27837f0c3ec  {0}  1004502", name);
        }
    }
}
