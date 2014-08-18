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
            var target = Path.Combine(
                IntegrationTestHelper.GetIntegrationTestRootDirectory(),
                "..", "src", "Update", "bin", "Release", "Update.exe");

            Assert.True(File.Exists(target));

            var ret = SquirrelAwareExecutableDetector.GetPESquirrelAwareVersion(target);
            Assert.Equal(1, ret.Value);
        }

        [Fact]
        public void NotSquirrelAware()
        {
            var target = Assembly.GetExecutingAssembly().Location;
            Assert.True(File.Exists(target));

            var ret = SquirrelAwareExecutableDetector.GetPESquirrelAwareVersion(target);
            Assert.Null(ret);
        }
    }
}
