using System;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    class ConsoleLogger : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Info;

        private readonly object gate = new object();

        private readonly string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            if (SquirrelRuntimeInfo.IsWindows)
                message = message.Replace(localAppData, "%localappdata%", StringComparison.InvariantCultureIgnoreCase);

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

        public static ILogger RegisterLogger()
        {
            var logger = new ConsoleLogger();
            SquirrelLocator.CurrentMutable.Register(() => logger, typeof(ILogger));
            return logger;
        }
    }
}
