using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squirrel.NuGet;
using Squirrel.Tests.TestHelpers;
using Xunit;
using ZipPackage = Squirrel.NuGet.ZipPackage;

namespace Squirrel.Tests
{
    public class ZipPackageTests
    {
        [Fact]
        public void HasSameFilesAndDependenciesAsPackaging()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "slack-1.1.8-full.nupkg");

            var zp = new ZipPackage(inputPackage);
            var zipfw = zp.GetFrameworks();
            var zipf = zp.GetFiles().OrderBy(f => f.Path).ToArray();
            var zipfLib = zp.GetLibFiles().OrderBy(f => f.Path).ToArray();

            using Package package = Package.Open(inputPackage);
            var packagingfw = GetSupportedFrameworks(zp, package);
            var packaging = GetFiles(package).OrderBy(f => f.Path).ToArray();
            var packagingLib = GetLibFiles(package).OrderBy(f => f.Path).ToArray();

            //for (int i = 0; i < zipf.Length; i++) {
            //    if (zipf[i] != packagingLib[i])
            //        throw new Exception();
            //}

            Assert.Equal(packagingfw, zipfw);
            Assert.Equal(packaging, zipf);
            Assert.Equal(packagingLib, zipfLib);
        }

        [Fact]
        public void ParsesNuspecCorrectly()
        {
            var inputPackage = IntegrationTestHelper.GetPath("fixtures", "FullNuspec.1.0.0.nupkg");
            var zp = new ZipPackage(inputPackage);

            var dyn = ExposedObject.From(zp);

            Assert.Equal("FullNuspec", zp.Id);
            Assert.Equal(new SemanticVersion("1.0"), zp.Version);
            Assert.Equal(new [] { "Anaïs Betts", "Caelan Sayler" }, dyn.Authors);
            Assert.Equal(new Uri("https://github.com/clowd/Clowd.Squirrel"), zp.ProjectUrl);
            Assert.Equal(new Uri("https://user-images.githubusercontent.com/1287295/131249078-9e131e51-0b66-4dc7-8c0a-99cbea6bcf80.png"), zp.IconUrl);
            Assert.Equal("A test description", dyn.Description);
            Assert.Equal("A summary", dyn.Summary);
            Assert.Equal("release notes\nwith multiple lines", zp.ReleaseNotes);
            Assert.Equal("Copyright ©", dyn.Copyright);
            Assert.Equal("en-US", zp.Language);
            Assert.Equal("Squirrel for Windows", dyn.Title);

            Assert.NotEmpty(zp.DependencySets);
            var net461 = zp.DependencySets.First();
            Assert.Equal(new[] { ".NETFramework4.6.1" }, net461.SupportedFrameworks);
            Assert.Equal(".NETFramework4.6.1", net461.TargetFramework);

            Assert.NotEmpty(net461.Dependencies);
            var dvt = net461.Dependencies.First();
            Assert.Equal("System.ValueTuple", dvt.Id);
            Assert.Equal("4.5.0", dvt.VersionSpec);

            Assert.Equal(new[] { "net5.0" }, zp.DependencySets.Last().SupportedFrameworks);

            Assert.NotEmpty(zp.FrameworkAssemblies);
            var fw = zp.FrameworkAssemblies.First();
            Assert.Equal("System.Net.Http", fw.AssemblyName);
            Assert.Equal(new [] { ".NETFramework4.6.1" }, fw.SupportedFrameworks);
        }

        IEnumerable<string> GetSupportedFrameworks(ZipPackage zp, Package package)
        {
            var fileFrameworks = from part in package.GetParts()
                                 where IsPackageFile(part)
                                 select NugetUtil.ParseFrameworkNameFromFilePath(NugetUtil.GetPath(part.Uri), out var effectivePath);

            return zp.FrameworkAssemblies.SelectMany(f => f.SupportedFrameworks)
                       .Concat(fileFrameworks)
                       .Where(f => f != null)
                       .Distinct();
        }

        IEnumerable<IPackageFile> GetLibFiles(Package package)
        {
            return GetFiles(package, NugetUtil.LibDirectory);
        }

        IEnumerable<IPackageFile> GetFiles(Package package, string directory)
        {
            string folderPrefix = directory + Path.DirectorySeparatorChar;
            return GetFiles(package).Where(file => file.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
        }

        List<IPackageFile> GetFiles(Package package)
        {
            return (from part in package.GetParts()
                    where IsPackageFile(part)
                    select (IPackageFile) new ZipPackageFile(NugetUtil.GetPath(part.Uri))).ToList();
        }

        bool IsPackageFile(PackagePart part)
        {
            string path = NugetUtil.GetPath(part.Uri);
            string directory = Path.GetDirectoryName(path);
            string[] ExcludePaths = new[] { "_rels", "package" };
            return !ExcludePaths.Any(p => directory.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && !IsManifest(path);
        }

        bool IsManifest(string p)
        {
            return Path.GetExtension(p).Equals(NugetUtil.ManifestExtension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
