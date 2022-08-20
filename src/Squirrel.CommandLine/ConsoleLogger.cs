using System;
using System.IO;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;
using NugetLogger = NuGet.Common.ILogger;
using NugetMessage = NuGet.Common.ILogMessage;
using NugetLevel = NuGet.Common.LogLevel;

namespace Squirrel.CommandLine
{
    class ConsoleLogger : ILogger, NugetLogger
    {
        public LogLevel Level { get; set; } = LogLevel.Info;

        private readonly object gate = new object();

        private readonly string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd('/', '\\');
        private readonly string localTemp = Path.GetTempPath().TrimEnd('/', '\\');

        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            if (SquirrelRuntimeInfo.IsWindows) {
                message = message.Replace(localTemp, "%temp%", StringComparison.InvariantCultureIgnoreCase);
                message = message.Replace(localAppData, "%localappdata%", StringComparison.InvariantCultureIgnoreCase);
            }

            lock (gate) {
                string lvl = logLevel.ToString().Substring(0, 4).ToUpper();
                if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal) {
                    Utility.ConsoleWriteWithColor($"[{lvl}] {message}{Environment.NewLine}", ConsoleColor.Red);
                } else if (logLevel == LogLevel.Warn) {
                    Utility.ConsoleWriteWithColor($"[{lvl}] {message}{Environment.NewLine}", ConsoleColor.Yellow);
                } else {
                    Console.WriteLine($"[{lvl}] {message}");
                }
            }
        }

        public static ConsoleLogger RegisterLogger()
        {
            var logger = new ConsoleLogger();
            SquirrelLocator.CurrentMutable.Register(() => logger, typeof(ILogger));
            SquirrelLocator.CurrentMutable.Register(() => logger, typeof(NugetLogger));
            return logger;
        }

        #region NuGet.Common.ILogger

        void NugetLogger.LogDebug(string data)
        {
            Write(data, LogLevel.Debug);
        }

        void NugetLogger.LogVerbose(string data)
        {
            Write(data, LogLevel.Debug);
        }

        void NugetLogger.LogInformation(string data)
        {
            Write(data, LogLevel.Info);
        }

        void NugetLogger.LogMinimal(string data)
        {
            Write(data, LogLevel.Info);
        }

        void NugetLogger.LogWarning(string data)
        {
            Write(data, LogLevel.Warn);
        }

        void NugetLogger.LogError(string data)
        {
            Write(data, LogLevel.Error);
        }

        void NugetLogger.LogInformationSummary(string data)
        {
            Write(data, LogLevel.Info);
        }

        LogLevel NugetToLogLevel(NugetLevel level)
        {
            return level switch {
                NugetLevel.Debug => LogLevel.Debug,
                NugetLevel.Verbose => LogLevel.Debug,
                NugetLevel.Information => LogLevel.Info,
                NugetLevel.Minimal => LogLevel.Info,
                NugetLevel.Warning => LogLevel.Warn,
                NugetLevel.Error => LogLevel.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
        }

        void NugetLogger.Log(NugetLevel level, string data)
        {
            Write(data, NugetToLogLevel(level));
        }

        Task NugetLogger.LogAsync(NugetLevel level, string data)
        {
            Write(data, NugetToLogLevel(level));
            return Task.CompletedTask;
        }

        void NugetLogger.Log(NugetMessage message)
        {
            Write(message.Message, NugetToLogLevel(message.Level));
        }

        Task NugetLogger.LogAsync(NugetMessage message)
        {
            Write(message.Message, NugetToLogLevel(message.Level));
            return Task.CompletedTask;
        }

        #endregion
    }
}