using System;
using System.IO;
using NLog.Config;
using NLog.Targets;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    class SetupLogLogger : ILogger
    {
        public LogLevel Level { get; set; }

        private readonly NLog.Logger _log;

        public SetupLogLogger(bool saveInTemp, UpdateAction action)
        {
            var dir = saveInTemp ?
                   Utility.GetTempDirectory(null).FullName :
                   SquirrelRuntimeInfo.BaseDirectory;

            string name, archivename;
            if (saveInTemp || action == UpdateAction.Unset) {
                name = "Squirrel.log";
                archivename = "Squirrel.archive{###}.log";
            } else {
                name = $"Squirrel-{action}.log";
                archivename = $"Squirrel-{action}.archive{{###}}.log";
            }

            // https://gist.github.com/chrisortman/1092889
            SimpleConfigurator.ConfigureForTargetLogging(
                new FileTarget() {
                    FileName = Path.Combine(dir, name),
                    Layout = new NLog.Layouts.SimpleLayout("${longdate} [${level:uppercase=true}] - ${message}"),
                    ArchiveFileName = Path.Combine(dir, archivename),
                    ArchiveAboveSize = 4_000_000 /* 4 MB */,
                    ArchiveNumbering = ArchiveNumberingMode.Sequence,
                    ConcurrentWrites = true, // should allow multiple processes to use the same file
                    KeepFileOpen = true,
                    MaxArchiveFiles = 3 /* MAX 16mb of log data per "action" */,
                },
                NLog.LogLevel.Debug
            );

            _log = NLog.LogManager.GetLogger("SetupLogLogger");
        }

        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

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
