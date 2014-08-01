using System.Runtime.Versioning;
using MarkdownSharp;
using NuGet;
using Squirrel;
using Squirrel.Tests.TestHelpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Splat;
using Xunit;

namespace Squirrel.Tests.Core
{
    public class CreateReleasePackageTests : IEnableLogger
    {
        [Fact]
        public void ReleasePackageIntegrationTest()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Tests.0.1.0-pre.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var sourceDir = IntegrationTestHelper.GetPath("fixtures", "packages");

            var fixture = new ReleasePackage(inputPackage);
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            try {
                fixture.CreateReleasePackage(outputPackage, sourceDir);

                this.Log().Info("Resulting package is at {0}", outputPackage);
                var pkg = new ZipPackage(outputPackage);

                int refs = pkg.FrameworkAssemblies.Count();
                this.Log().Info("Found {0} refs", refs);
                refs.ShouldEqual(0);

                this.Log().Info("Files in release package:");

                List<IPackageFile> files = pkg.GetFiles().ToList();
                files.ForEach(x => this.Log().Info(x.Path));

                List<string> nonDesktopPaths = new[] {"sl", "winrt", "netcore", "win8", "windows8", "MonoAndroid", "MonoTouch", "MonoMac", "wp", }
                    .Select(x => @"lib\" + x)
                    .ToList();

                files.Any(x => nonDesktopPaths.Any(y => x.Path.ToLowerInvariant().Contains(y.ToLowerInvariant()))).ShouldBeFalse();
                files.Any(x => x.Path.ToLowerInvariant().EndsWith(@".xml")).ShouldBeFalse();
            } finally {
                File.Delete(outputPackage);
            }
        }

        [Fact]
        public void FindPackageInOurLocalPackageList()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Core.1.0.0.0.nupkg");
            var sourceDir = IntegrationTestHelper.GetPath("fixtures", "packages");
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            var fixture = ExposedObject.From(new ReleasePackage(inputPackage));
            IPackage result = fixture.matchPackage(new LocalPackageRepository(sourceDir), "xunit", VersionUtility.ParseVersionSpec("[1.0,2.0]"));

            result.Id.ShouldEqual("xunit");
            result.Version.Version.Major.ShouldEqual(2);
            result.Version.Version.Minor.ShouldEqual(0);
        }

        [Fact]
        public void FindDependentPackagesForDummyPackage()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Tests.0.1.0-pre.nupkg");
            var fixture = new ReleasePackage(inputPackage);
            var sourceDir = IntegrationTestHelper.GetPath("fixtures", "packages");
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            IEnumerable<IPackage> results = fixture.findAllDependentPackages(default(IPackage), (IPackageRepository)new LocalPackageRepository(sourceDir), default(HashSet<string>), default(FrameworkName));
            results.Count().ShouldBeGreaterThan(0);
        }

        [Fact]
        public void CanLoadPackageWhichHasNoDependencies()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Core.NoDependencies.1.0.0.0.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var fixture = new ReleasePackage(inputPackage);
            var sourceDir = IntegrationTestHelper.GetPath("fixtures", "packages");
            try {
                fixture.CreateReleasePackage(outputPackage, sourceDir);
            }
            finally {
                File.Delete(outputPackage);
            }
        }

        [Fact]
        public void CanResolveMultipleLevelsOfDependencies()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "Squirrel.Tests.0.1.0-pre.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var sourceDir = IntegrationTestHelper.GetPath("fixtures", "packages");

            var fixture = new ReleasePackage(inputPackage);
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            try {
                fixture.CreateReleasePackage(outputPackage, sourceDir);

                this.Log().Info("Resulting package is at {0}", outputPackage);
                var pkg = new ZipPackage(outputPackage);

                int refs = pkg.FrameworkAssemblies.Count();
                this.Log().Info("Found {0} refs", refs);
                refs.ShouldEqual(0);

                this.Log().Info("Files in release package:");
                pkg.GetFiles().ForEach(x => this.Log().Info(x.Path));

                var filesToLookFor = new[] {
                    "xunit.assert.dll",         // Tests => Xunit => Xunit.Assert
                    "NuGet.Core.dll",           // Tests => NuGet
                    "Squirrel.Tests.dll",
                };

                filesToLookFor.ForEach(name => {
                    this.Log().Info("Looking for {0}", name);
                    pkg.GetFiles().Any(y => y.Path.ToLowerInvariant().Contains(name.ToLowerInvariant())).ShouldBeTrue();
                });
            } finally {
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
                renderMinfo.Invoke(fixture, new object[] {targetFile, processor});

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
        public void UsesTheRightVersionOfADependencyWhenMultipleAreInPackages()
        {
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            string outputFile = null;

            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "CaliburnMicroDemo.1.0.0.nupkg");

            var wrongPackage = "Caliburn.Micro.1.4.1.nupkg";
            var wrongPackagePath = IntegrationTestHelper.GetPath("fixtures", wrongPackage);
            var rightPackage = "Caliburn.Micro.1.5.2.nupkg";
            var rightPackagePath = IntegrationTestHelper.GetPath("fixtures", rightPackage);

            try {
                var sourceDir = IntegrationTestHelper.GetPath("fixtures", "packages");
                (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

                File.Copy(wrongPackagePath, Path.Combine(sourceDir, wrongPackage), true);
                File.Copy(rightPackagePath, Path.Combine(sourceDir, rightPackage), true);

                var package = new ReleasePackage(inputPackage);
                var outputFileName = package.CreateReleasePackage(outputPackage, sourceDir);

                var zipPackage = new ZipPackage(outputFileName);

                var fileName = "Caliburn.Micro.dll";
                var dependency = zipPackage.GetLibFiles()
                    .Where(f => f.Path.EndsWith(fileName))
                    .Single(f => f.TargetFramework == FrameworkTargetVersion.Net40);

                outputFile = new FileInfo(Path.Combine(sourceDir, fileName)).FullName;

                using (var of = File.Create(outputFile))
                {
                    dependency.GetStream().CopyTo(of);
                }

                var assemblyName = AssemblyName.GetAssemblyName(outputFile);
                Assert.Equal(1, assemblyName.Version.Major);
                Assert.Equal(5, assemblyName.Version.Minor);
            }
            finally {
                File.Delete(outputPackage);
                File.Delete(outputFile);
            }
        }

        [Fact]
        public void DependentPackageNotFoundAndThrowsError()
        {
            string packagesDir;
            // use empty packages folder
            using (Utility.WithTempDirectory(out packagesDir)) {
                var inputPackage = IntegrationTestHelper.GetPath("fixtures", "ProjectDependsOnJsonDotNet.1.0.nupkg");

                var outputPackage = Path.GetTempFileName() + ".nupkg";

                try {
                    var package = new ReleasePackage(inputPackage);
                    Assert.Throws<Exception>(() =>
                        package.CreateReleasePackage(outputPackage, packagesDir));
                } finally {
                    File.Delete(outputPackage);
                }
            }
        }

        [Fact]
        public void ContentFilesAreIncludedInCreatedPackage()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "ProjectWithContent.1.0.0.0-beta.nupkg");
            var outputPackage = Path.GetTempFileName() + ".nupkg";
            var sourceDir = IntegrationTestHelper.GetPath("fixtures", "packages");

            var fixture = new ReleasePackage(inputPackage);
            (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

            try
            {
                fixture.CreateReleasePackage(outputPackage, sourceDir);

                this.Log().Info("Resulting package is at {0}", outputPackage);
                var pkg = new ZipPackage(outputPackage);

                int refs = pkg.FrameworkAssemblies.Count();
                this.Log().Info("Found {0} refs", refs);
                refs.ShouldEqual(0);

                this.Log().Info("Files in release package:");

                var contentFiles = pkg.GetContentFiles();
                Assert.Equal(2, contentFiles.Count());

                var contentFilePaths = contentFiles.Select(f => f.EffectivePath);

                Assert.Contains("some-words.txt", contentFilePaths);
                Assert.Contains("dir\\item-in-subdirectory.txt", contentFilePaths);

                Assert.Equal(1, pkg.GetLibFiles().Count());
            }
            finally
            {
                File.Delete(outputPackage);
            }
        }

        [Fact]
        public void WhenAProjectContainsNet45BinariesItContainsTheNecessaryDependency()
        {
            var outputPackage = Path.GetTempFileName() + ".nupkg";

            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "ThisShouldBeANet45Project.1.0.nupkg");

            var rightPackage = "Caliburn.Micro.1.5.2.nupkg";
            var rightPackagePath = IntegrationTestHelper.GetPath("fixtures", rightPackage);

            try
            {
                var sourceDir = IntegrationTestHelper.GetPath("fixtures", "packages");
                (new DirectoryInfo(sourceDir)).Exists.ShouldBeTrue();

                File.Copy(rightPackagePath, Path.Combine(sourceDir, rightPackage), true);

                var package = new ReleasePackage(inputPackage);
                var outputFileName = package.CreateReleasePackage(outputPackage, sourceDir);

                var zipPackage = new ZipPackage(outputFileName);

                var dependency = zipPackage.GetLibFiles()
                    .Where(f => f.Path.EndsWith("Caliburn.Micro.dll"))
                    .FirstOrDefault(f => f.TargetFramework == FrameworkTargetVersion.Net45);

                Assert.NotNull(dependency);
            }
            finally
            {
                File.Delete(outputPackage);
            }
        }
    }
}
