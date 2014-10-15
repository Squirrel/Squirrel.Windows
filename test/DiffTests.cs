using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class DiffTests
    {
        [Fact]
        public void CreateAtomDiffSmokeTest()
        {
            var baseFile = IntegrationTestHelper.GetPath("fixtures", "bsdiff", "atom-137.0.exe");
            var newFile = IntegrationTestHelper.GetPath("fixtures", "bsdiff", "atom-137.1.exe");

            var baseBytes = File.ReadAllBytes(baseFile);
            var newBytes = File.ReadAllBytes(newFile);

            var ms = new MemoryStream();
            BinaryPatchUtility.Create(baseBytes, newBytes, ms);

            Assert.True(ms.Length > 100);
        }
    }
}
