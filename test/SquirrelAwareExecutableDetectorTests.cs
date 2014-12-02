using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class SquirrelAwareExecutableDetectorTests
    {
        [Fact]
        public void AtomShellShouldBeSquirrelAware()
        {
            var target = IntegrationTestHelper.GetPath("fixtures", "atom.exe");

            Assert.True(File.Exists(target));
            Assert.True(SquirrelAwareExecutableDetector.GetPESquirrelAwareVersion(target) == 1);
        }

        [Fact]
        public void SquirrelAwareViaVersionBlock()
        {
            var target = Path.Combine(
                IntegrationTestHelper.GetIntegrationTestRootDirectory(),
                "..", "src", "Setup", "bin", "Release", "Setup.exe");

            Assert.True(File.Exists(target));

            var ret = SquirrelAwareExecutableDetector.GetPESquirrelAwareVersion(target);
            Assert.Equal(1, ret.Value);
        }

        [Fact]
        public void SquirrelAwareViaAssemblyAttribute()
        {
            var target = Assembly.GetExecutingAssembly().Location;

            Assert.True(File.Exists(target));

            var ret = SquirrelAwareExecutableDetector.GetPESquirrelAwareVersion(target);
            Assert.Equal(1, ret.Value);
        }

        [Fact]
        public void NotSquirrelAware()
        {
            var target = IntegrationTestHelper.GetPath("..", "src", "Update", "bin", "Release", "Update.exe");
            Assert.True(File.Exists(target));

            var ret = SquirrelAwareExecutableDetector.GetPESquirrelAwareVersion(target);
            Assert.Null(ret);
        }

        [Fact]
        public void SquirrelAwareTestAppShouldBeSquirrelAware()
        {
            var target = IntegrationTestHelper.GetPath("fixtures", "SquirrelAwareApp.exe");
            Assert.True(File.Exists(target));

            Assert.NotNull(SquirrelAwareExecutableDetector.GetPESquirrelAwareVersion(target));
        }

        [Fact]
        public void NotSquirrelAwareTestAppShouldNotBeSquirrelAware()
        {
            var target = IntegrationTestHelper.GetPath("fixtures", "NotSquirrelAwareApp.exe");
            Assert.True(File.Exists(target));

            Assert.Null(SquirrelAwareExecutableDetector.GetPESquirrelAwareVersion(target));
        }

    }
}
