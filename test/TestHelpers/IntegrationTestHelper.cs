using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using Ionic.Zip;
using Squirrel.Core;
using ReactiveUIMicro;
using Squirrel.Tests.WiXUi;

namespace Squirrel.Tests.TestHelpers
{
    public static class IntegrationTestHelper
    {
        public static string GetPath(params string[] paths)
        {
            var ret = GetIntegrationTestRootDirectory();
            return (new FileInfo(paths.Aggregate (ret, Path.Combine))).FullName;
        }

        public static string GetIntegrationTestRootDirectory()
        {
            // XXX: This is an evil hack, but it's okay for a unit test
            // We can't use Assembly.Location because unit test runners love
            // to move stuff to temp directories
            var st = new StackFrame(true);
            var di = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(st.GetFileName()), ".."));

            return di.FullName;
        }

        public static bool SkipTestOnXPAndVista()
        {
            int osVersion = Environment.OSVersion.Version.Major*100 + Environment.OSVersion.Version.Minor;
            return (osVersion < 601);
        }

        public static void RunBlockAsSTA(Action block)
        {
            Exception ex = null;
            var t = new Thread(() => {
                try {
                    block();
                } catch (Exception e) {
                    ex = e;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            if (ex != null) {
                // NB: If we don't do this, the test silently passes
                throw new Exception("", ex);
            }
        }

        static object gate = 42;
        public static IDisposable WithFakeInstallDirectory(string packageFileName, out string path)
        {
            var ret = Utility.WithTempDirectory(out path);

            File.Copy(GetPath("fixtures", packageFileName), Path.Combine(path, packageFileName));
            var rp = ReleaseEntry.GenerateFromFile(Path.Combine(path, packageFileName));
            ReleaseEntry.WriteReleaseFile(new[] { rp }, Path.Combine(path, "RELEASES"));

            // NB: This is a temporary hack. The reason we serialize the tests
            // like this, is to make sure that we don't have two tests registering
            // their Service Locators with RxApp.
            Monitor.Enter(gate);
            return new CompositeDisposable(ret, Disposable.Create(() => Monitor.Exit(gate)));
        }

        public static IDisposable WithFakeInstallDirectory(out string path)
        {
            return WithFakeInstallDirectory("SampleUpdatingApp.1.1.0.0.nupkg", out path);
        }

        public static IDisposable WithFakeAlreadyInstalledApp(out string path)
        {
            return WithFakeAlreadyInstalledApp("InstalledSampleUpdatingApp-1.1.0.0.zip", out path);
        }

        public static IDisposable WithFakeAlreadyInstalledApp(string zipFile, out string path)
        {
            var ret = Utility.WithTempDirectory(out path);

            var zf = new ZipFile(GetPath("fixtures", zipFile));
            zf.ExtractAll(path);

            Monitor.Enter(gate);
            return new CompositeDisposable(ret, Disposable.Create(() => Monitor.Exit(gate)));
        }
    }
}
