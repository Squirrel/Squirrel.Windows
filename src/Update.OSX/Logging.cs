using System;
using System.IO;
using NLog.Config;
using NLog.Targets;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    class SetupLogLogger : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Info;

        private readonly NLog.Logger _log;

        protected SetupLogLogger(string appId)
        {
            var name = "squirrel.log";
            var archivename = "squirrel.{###}.log";

            if (appId != null) {
                name = $"squirrel.{appId}.log";
                archivename = $"squirrel.{appId}.{{###}}.log";
            }

            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var logDirectory = Path.Combine(homeDir, "Library", "Logs");
            if (!Directory.Exists(logDirectory)) Directory.CreateDirectory(logDirectory);
            
            // https://gist.github.com/chrisortman/1092889
            SimpleConfigurator.ConfigureForTargetLogging(
                new FileTarget() {
                    FileName = Path.Combine(logDirectory, name),
                    Layout = new NLog.Layouts.SimpleLayout("${longdate} [${level:uppercase=true}] - ${message}"),
                    ArchiveFileName = Path.Combine(logDirectory, archivename),
                    ArchiveAboveSize = 1_000_000,
                    ArchiveNumbering = ArchiveNumberingMode.Sequence,
                    ConcurrentWrites = true, // should allow multiple processes to use the same file
                    KeepFileOpen = true,
                    MaxArchiveFiles = 1,
                },
                NLog.LogLevel.Debug
            );

            _log = NLog.LogManager.GetLogger("SetupLogLogger");
        }
        
        public static ILogger RegisterLogger(string appId)
        {
            var logger = new SetupLogLogger(appId);
            SquirrelLocator.CurrentMutable.Register(() => logger, typeof(ILogger));
            return logger;
        }

        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            Console.WriteLine($"[{logLevel}] {message}");

            switch (logLevel) {
            case LogLevel.Debug:
                _log.Debug(message);
                break;
            case LogLevel.Info:
                _log.Info(message);
                break;
            case LogLevel.Warn:
                _log.Warn(message);
                break;
            case LogLevel.Error:
                _log.Error(message);
                break;
            case LogLevel.Fatal:
                _log.Fatal(message);
                break;
            default:
                _log.Info(message);
                break;
            }
        }
    }
}