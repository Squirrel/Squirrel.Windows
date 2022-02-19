using System.Runtime.Versioning;
using Squirrel.MarkdownSharp;
using Squirrel;
using Squirrel.Tests.TestHelpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Squirrel.SimpleSplat;
using Xunit;
using Squirrel.NuGet;

namespace Squirrel.Tests
{
    public class CreateReleasePackageTests : IEnableLogger
    {
        [Theory]
        [InlineData("1.2.3")]
        [InlineData("1.2.3-alpha13")]
        [InlineData("1.2.3-alpha135")]
        [InlineData("0.0.1")]
        [InlineData("1.299656.3-alpha")]
        public void SemanticVersionParsesValidVersion(string ver)
        {
            NugetUtil.ThrowIfVersionNotSemverCompliant(ver);
            Assert.True(SemanticVersion.TryParseStrict(ver, out var _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("0")]
        [InlineData("1.2.3.4")]
        [InlineData("1.2.3.4-alpha")]
        [InlineData("0.0.0.0")]
        [InlineData("0.0.0")]
        [InlineData("0.0")]
        [InlineData("0.0.0-alpha")]
        public void SemanticVersionThrowsInvalidVersion(string ver)
        {
            Assert.ThrowsAny<Exception>(() => NugetUtil.ThrowIfVersionNotSemverCompliant(ver));
        }

        [Fact]
        public void ReleasePackageIntegrationTest()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Tests.0.1.0-pre.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";

            var fixture = new ReleasePackage(inputPackage);

            try {
                fixture.CreateReleasePackage(outputPackage);

                this.Log().Info("Resulting package is at {0}", outputPackage);
                var pkg = new ZipPackage(outputPackage);

                int refs = pkg.FrameworkAssemblies.Count();
                this.Log().Info("Found {0} refs", refs);
                refs.ShouldEqual(0);

                this.Log().Info("Files in release package:");

                List<ZipPackageFile> files = pkg.Files.ToList();
                files.ForEach(x => this.Log().Info(x.Path));

                List<string> nonDesktopPaths = new[] { "sl", "winrt", "netcore", "win8", "windows8", "MonoAndroid", "MonoTouch", "MonoMac", "wp", }
                    .Select(x => @"lib\" + x)
                    .ToList();

                files.Any(x => nonDesktopPaths.Any(y => x.Path.ToLowerInvariant().Contains(y.ToLowerInvariant()))).ShouldBeFalse();
                files.Any(x => x.Path.ToLowerInvariant().EndsWith(@".xml")).ShouldBeFalse();
            } finally {
                File.Delete(outputPackage);
            }
        }

        [Fact]
        public void CanLoadPackageWhichHasNoDependencies()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Core.NoDependencies.1.0.0.0.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var fixture = new ReleasePackage(inputPackage);
            try {
                fixture.CreateReleasePackage(outputPackage);
            } finally {
                File.Delete(outputPackage);
            }
        }

        [Fact]
        public void ThrowsIfLoadsPackageWithDependencies()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "ProjectDependsOnJsonDotNet.1.0.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var fixture = new ReleasePackage(inputPackage);
            try {
                Assert.Throws<InvalidOperationException>(() => fixture.CreateReleasePackage(outputPackage));
            } finally {
                if (File.Exists(outputPackage))
                    File.Delete(outputPackage);
            }
        }

        [Fact]
        public void SpecFileMarkdownRenderingTest()
        {
            var dontcare = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Core.1.1.0.0.nupkg");
            var inputSpec = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Core.1.1.0.0.nuspec");
            var fixture = new ReleasePackage(dontcare);

            var targetFile = Path.GetTempFileName();
            File.Copy(inputSpec, targetFile, true);

            try {
                var processor = new Func<string, string>(input =>
                    (new Markdown()).Transform(input));

                // NB: For No Reason At All, renderReleaseNotesMarkdown is
                // invulnerable to ExposedObject. Whyyyyyyyyy
                var renderMinfo = fixture.GetType().GetMethod("renderReleaseNotesMarkdown",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                renderMinfo.Invoke(fixture, new object[] { targetFile, processor });

                var doc = XDocument.Load(targetFile);
                XNamespace ns = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";
                var relNotesElement = doc.Descendants(ns + "releaseNotes").First();
                var htmlText = relNotesElement.Value;

                this.Log().Info("HTML Text:\n{0}", htmlText);

                htmlText.Contains("## Release Notes").ShouldBeFalse();
            } finally {
                File.Delete(targetFile);
            }
        }

        [Fact]
        public void ContentFilesAreIncludedInCreatedPackage()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "ProjectWithContent.1.0.0.0-beta.nupkg");
            var outputPackage = Path.GetTempFileName() + ".zip";
            var fixture = new ReleasePackage(inputPackage);

            try {
                fixture.CreateReleasePackage(outputPackage);

                this.Log().Info("Resulting package is at {0}", outputPackage);
                var pkg = new ZipPackage(outputPackage);

                int refs = pkg.FrameworkAssemblies.Count();
                this.Log().Info("Found {0} refs", refs);
                refs.ShouldEqual(0);

                this.Log().Info("Files in release package:");

                var contentFiles = pkg.Files.Where(f => f.IsContentFile()).ToArray();
                Assert.Equal(2, contentFiles.Count());

                var contentFilePaths = contentFiles.Select(f => f.EffectivePath);

                Assert.Contains("some-words.txt", contentFilePaths);
                Assert.Contains("dir\\item-in-subdirectory.txt", contentFilePaths);

                Assert.Equal(1, pkg.Files.Where(f => f.IsLibFile()).Count());
            } finally {
                File.Delete(outputPackage);
            }
        }
    }
}
