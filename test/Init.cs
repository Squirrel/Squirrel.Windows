using System.IO;
using System.Reflection;
using Squirrel.CommandLine;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework("Squirrel.Tests.TestsInit", "Squirrel.Tests")]

namespace Squirrel.Tests
{
    public class TestsInit : XunitTestFramework
    {
        public TestsInit(IMessageSink messageSink)
          : base(messageSink)
        {
            // Place initialization code here
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location.Replace("file:///", ""));
            HelperFile.AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor"));
            HelperFile.AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor", "7zip"));
            HelperFile.AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor", "wix"));
        }

        public new void Dispose()
        {
            // Place tear down code here
            base.Dispose();
        }
    }
}
