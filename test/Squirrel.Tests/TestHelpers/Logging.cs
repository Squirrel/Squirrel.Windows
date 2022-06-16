using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;
using Xunit.Abstractions;

namespace Squirrel.Tests
{
    public interface ITestLogging
    {
        IFullLogger Log();
    }

    public class TestLogger : ILogger
    {
        public LogLevel Level { get; set; }
        public ITestOutputHelper OutputHelper { get; }

        public TestLogger(ITestOutputHelper outputHelper)
        {
            OutputHelper = outputHelper;
        }

        public void Write([Localizable(false)] string message, LogLevel logLevel)
        {
            OutputHelper.WriteLine(message);
        }
    }

    public abstract class TestLoggingBase : ITestLogging
    {
        public ITestOutputHelper OutputHelper { get; }
        public IFullLogger Logger { get; }

        public TestLoggingBase(ITestOutputHelper log)
        {
            OutputHelper = log;
            Logger = new WrappingFullLogger(new TestLogger(OutputHelper), typeof(TestLogger));
        }

        public IFullLogger Log()
        {
            return Logger;
        }
    }
}
