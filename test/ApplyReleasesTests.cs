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
    public class ApplyReleasesTests : IEnableLogger
    {
        [Serializable]
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

        [Fact]
        public void WhenNoNewReleasesAreAvailableTheListIsEmpty()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir)) {
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp"));
                var packages = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packages);

                var package = "Squirrel.Core.1.0.0.0-full.nupkg";
                File.Copy(IntegrationTestHelper.GetPath("fixtures", package),
                          Path.Combine(packages, package));

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
        public void ApplyReleasesWithOneReleaseFile()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                string packagesDir = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.0.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                using (fixture) {
                    var progress = new ReplaySubject<int>();
                    fixture.ApplyReleases(updateInfo, progress).First();
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress.Buffer(2,1).All(x => x.Count != 2 || x[1] > x[0]).First().ShouldBeTrue();
                    progress.Last().ShouldEqual(100);
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
        public void ApplyReleaseWhichRemovesAFile()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                string packagesDir = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "Squirrel.Core.1.1.0.0-full.nupkg",
                    "Squirrel.Core.1.2.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.2.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                using (fixture) {
                    var progress = new ReplaySubject<int>();
                    fixture.ApplyReleases(updateInfo, progress).First();
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress.Buffer(2,1).All(x => x.Count != 2 || x[1] > x[0]).First().ShouldBeTrue();
                    progress.Last().ShouldEqual(100);
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
        public void ApplyReleaseWhichMovesAFileToADifferentDirectory()
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

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.3.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                using (fixture) {
                    var progress = new ReplaySubject<int>();
                    fixture.ApplyReleases(updateInfo, progress).First();
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress.Buffer(2, 1).All(x => x.Count != 2 || x[1] > x[0]).First().ShouldBeTrue();
                    progress.Last().ShouldEqual(100);
                }

                var rootDirectory = Path.Combine(tempDir, "theApp", "app-1.3.0.0");

                new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                }.ForEach(x =>
                {
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
        public void ApplyReleasesWithDeltaReleases()
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

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-delta.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "Squirrel.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { deltaEntry, latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();

                using (fixture) {
                    var progress = new ReplaySubject<int>();

                    fixture.ApplyReleases(updateInfo, progress).First();
                    this.Log().Info("Progress: [{0}]", String.Join(",", progress));

                    progress.Buffer(2,1).All(x => x.Count != 2 || x[1] > x[0]).First().ShouldBeTrue();
                    progress.Last().ShouldEqual(100);
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
        public void CreateFullPackagesFromDeltaSmokeTest()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir)) {
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp", "packages"));

                new[] {
                    "Squirrel.Core.1.0.0.0-full.nupkg",
                    "Squirrel.Core.1.1.0.0-delta.nupkg"
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, "theApp", "packages", x)));

                var urlDownloader = new Mock<IUrlDownloader>();
                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, urlDownloader.Object);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Squirrel.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Squirrel.Core.1.1.0.0-delta.nupkg"));

                var resultObs = (IObservable<ReleaseEntry>)fixture.GetType().GetMethod("createFullPackagesFromDeltas", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(fixture, new object[] { new[] {deltaEntry}, baseEntry });

                var result = resultObs.Last();
                var zp = new ZipPackage(Path.Combine(tempDir, "theApp", "packages", result.Filename));

                zp.Version.ToString().ShouldEqual("1.1.0.0");
            }
        }

        [Fact(Skip = "TODO")]
        public void ShouldCallAppUninstallOnTheOldVersion()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void CallAppInstallOnTheJustInstalledVersion()
        {
            string tempDir;
            using (acquireEnvVarLock())
            using (Utility.WithTempDirectory(out tempDir)) {
                var di = Path.Combine(tempDir, "theApp", "app-1.1.0.0");
                Directory.CreateDirectory(di);

                File.Copy(getPathToSquirrelTestTarget(), Path.Combine(di, "SquirrelIAppUpdateTestTarget.exe"));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, null);

                this.Log().Info("Invoking post-install");
                var mi = fixture.GetType().GetMethod("runPostInstallOnDirectory", BindingFlags.NonPublic | BindingFlags.Instance);
                mi.Invoke(fixture, new object[] { di, true, new Version(1, 1, 0, 0), Enumerable.Empty<ShortcutCreationRequest>() });

                getEnvVar("AppInstall_Called").ShouldEqual("1");
                getEnvVar("VersionInstalled_Called").ShouldEqual("1.1.0.0");
            }
        }

        [Fact]
        public void ShouldCreateAppShortcutsBasedOnClientExe()
        {
            string tempDir;
            using (acquireEnvVarLock())
            using (Utility.WithTempDirectory(out tempDir)) 
            using (setEnvVar("ShortcutDir", tempDir)) {
                var di = Path.Combine(tempDir, "theApp", "app-1.1.0.0");
                Directory.CreateDirectory(di);

                File.Copy(getPathToSquirrelTestTarget(), Path.Combine(di, "SquirrelIAppUpdateTestTarget.exe"));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, null);

                this.Log().Info("Invoking post-install");
                var mi = fixture.GetType().GetMethod("runPostInstallOnDirectory", BindingFlags.NonPublic | BindingFlags.Instance);
                mi.Invoke(fixture, new object[] { di, true, new Version(1, 1, 0, 0), Enumerable.Empty<ShortcutCreationRequest>() });

                File.Exists(Path.Combine(tempDir, "Foo.lnk")).ShouldBeTrue();
            }
        }

        [Fact(Skip = "TODO")]
        public void DeletedShortcutsShouldntBeRecreatedOnUpgrade()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void IfAppSetupThrowsWeFailTheInstall()
        {
            string tempDir;

            using (acquireEnvVarLock())
            using (setShouldThrow())
            using (Utility.WithTempDirectory(out tempDir)) {
                var di = Path.Combine(tempDir, "theApp", "app-1.1.0.0");
                Directory.CreateDirectory(di);

                File.Copy(getPathToSquirrelTestTarget(), Path.Combine(di, "SquirrelIAppUpdateTestTarget.exe"));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, null);

                bool shouldDie = true;
                try {
                    this.Log().Info("Invoking post-install");

                    var mi = fixture.GetType().GetMethod("runPostInstallOnDirectory", BindingFlags.NonPublic | BindingFlags.Instance);
                    mi.Invoke(fixture, new object[] { di, true, new Version(1, 1, 0, 0), Enumerable.Empty<ShortcutCreationRequest>() });
                } catch (TargetInvocationException ex) {
                    this.Log().Info("Expected to receive Exception", ex);

                    // NB: This is the exception explicitly rigged in OnAppInstall
                    if (ex.InnerException is FileNotFoundException) {
                        shouldDie = false;
                    } else {
                        this.Log().ErrorException("Expected FileNotFoundException, didn't get it", ex);
                    }
                }

                shouldDie.ShouldBeFalse();
            }
        }

        [Fact]
        public void ExecutablesPinnedToTaskbarShouldPointToNewVersion()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                string packagesDir = Path.Combine(tempDir, "theApp", "packages");
                Directory.CreateDirectory(packagesDir);

                new[] {
                    "SampleUpdatingApp.1.0.0.0.nupkg",
                    "SampleUpdatingApp.1.1.0.0.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(packagesDir, x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "SampleUpdatingApp.1.0.0.0.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(packagesDir, "SampleUpdatingApp.1.1.0.0.nupkg"));

                var updateInfo = UpdateInfo.Create(null, new[] { baseEntry }, packagesDir, FrameworkVersion.Net40);
                using (fixture) {
                    fixture.ApplyReleases(updateInfo).ToList().First();
                }

                var oldExecutable = Path.Combine(tempDir, "theApp", "app-1.0.0.0", "SampleUpdatingApp.exe");
                File.Exists(oldExecutable).ShouldBeTrue();
                TaskbarHelper.PinToTaskbar(oldExecutable);

                updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, packagesDir, FrameworkVersion.Net40);
                using (fixture) {
                    fixture.ApplyReleases(updateInfo).ToList().First();
                }

                var newExecutable = Path.Combine(tempDir, "theApp", "app-1.1.0.0", "SampleUpdatingApp.exe");
                File.Exists(newExecutable).ShouldBeTrue();
                TaskbarHelper.IsPinnedToTaskbar(newExecutable).ShouldBeTrue();

                Utility.Retry(() => TaskbarHelper.UnpinFromTaskbar(newExecutable));
            }
        }

        string getPathToSquirrelTestTarget()
        {
#if DEBUG
            const string config = "Debug";
#else
            const string config = "Release";
#endif

            var ret = IntegrationTestHelper.GetPath("..", "SquirrelIAppUpdateTestTarget", "bin", config, "SquirrelIAppUpdateTestTarget.exe");
            File.Exists(ret).ShouldBeTrue();

            return ret;
        }

        static readonly object gate = 42;
        static IDisposable acquireEnvVarLock()
        {
            // NB: Since we use process-wide environment variables to communicate
            // across AppDomains, we have to serialize all of the tests
            Monitor.Enter(gate);
            return Disposable.Create(() => {
                // NB: The test target sets a bunch of environment variables that 
                // we should clear out, but there's no way for the test target to
                // clean these up correctly
                Environment.GetEnvironmentVariables().Keys.OfType<string>()
                    .Where(x => x.StartsWith("__IAPPSETUP_TEST"))
                    .ForEach(x => Environment.SetEnvironmentVariable(x, null));

                Monitor.Exit(gate);
            });
        }

        static IDisposable setShouldThrow()
        {
            return setEnvVar("ShouldThrow", true);
        }

        static string getEnvVar(string name)
        {
            return Environment.GetEnvironmentVariable(String.Format("__IAPPSETUP_TEST_{0}", name.ToUpperInvariant()));
        }

        static IDisposable setEnvVar(string name, object val)
        {
            var prevVal = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(String.Format("__IAPPSETUP_TEST_{0}", name.ToUpperInvariant()), val.ToString(), EnvironmentVariableTarget.Process);

            return Disposable.Create(() => Environment.SetEnvironmentVariable(name, prevVal));
        }
    }
}
