using System.IO;
using System.Reflection;
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
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", ""));
            HelperExe.AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor"));
            HelperExe.AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor", "7zip"));
            HelperExe.AddSearchPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor", "wix"));
        }

        public new void Dispose()
        {
            // Place tear down code here
            base.Dispose();
        }
    }
}
