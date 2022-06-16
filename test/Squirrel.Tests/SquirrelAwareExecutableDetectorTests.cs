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
            Assert.True(SquirrelAwareExecutableDetector.GetSquirrelAwareVersion(target) == 1);
        }

        [Fact]
        public void SquirrelAwareViaLanguageNeutralVersionBlock()
        {
            var target = IntegrationTestHelper.GetPath("fixtures", "SquirrelAwareTweakedNetCoreApp.exe");
            Assert.True(File.Exists(target));

            var ret = SquirrelAwareExecutableDetector.GetSquirrelAwareVersion(target);
            Assert.Equal(1, ret.Value);
        }

        [Fact]
        public void NotSquirrelAwareTestAppShouldNotBeSquirrelAware()
        {
            var target = IntegrationTestHelper.GetPath("fixtures", "NotSquirrelAwareApp.exe");
            Assert.True(File.Exists(target));

            Assert.Null(SquirrelAwareExecutableDetector.GetSquirrelAwareVersion(target));
        }

        [Fact]
        public void SquirrelAwareViaManifest()
        {
            var target = IntegrationTestHelper.GetPath("fixtures", "PublishSingleFileAwareApp.exe");
            Assert.True(File.Exists(target));

            var ret = SquirrelAwareExecutableDetector.GetSquirrelAwareVersion(target);
            Assert.Equal(1, ret);
        }
    }
}
