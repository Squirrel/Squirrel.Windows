using System;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    class Program : IEnableLogger
    {
        static StartupOption _options;
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        [STAThread]
        public static int Main(string[] args)
        {
            try {
                var logger = new SetupLogLogger(Utility.GetDefaultTempBaseDirectory());
                SquirrelLocator.CurrentMutable.Register(() => logger, typeof(ILogger));
                _options = new StartupOption(args);

                
                return main(args);
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                return -1;
            }
        }

        static int main(string[] args)
        {

            try {
            } catch (Exception ex) {
                logp.Write($"Failed to parse command line options. {ex.Message}", LogLevel.Error);
                throw;
            }

            // NB: Trying to delete the app directory while we have Setup.log
            // open will actually crash the uninstaller
            bool logToTemp = true;

            var logDir = logToTemp ? Utility.GetDefaultTempDirectory(null) : SquirrelRuntimeInfo.BaseDirectory;

            var logger = new SetupLogLogger(logDir, !logToTemp, _options.updateAction) { Level = LogLevel.Info };
            SquirrelLocator.CurrentMutable.Register(() => logger, typeof(SimpleSplat.ILogger));

            try {
                return executeCommandLine(args);
            } catch (Exception ex) {
                logger.Write("Finished with unhandled exception: " + ex, LogLevel.Fatal);
                throw;
            }
        }

        static int executeCommandLine(string[] args)
        {
            Log.Info("Starting Squirrel Updater: " + String.Join(" ", args));
            Log.Info("Updater location is: " + SquirrelRuntimeInfo.EntryExePath);

            if (_options.updateAction == UpdateAction.Unset) {
                _options.WriteOptionDescriptions();
                return -1;
            }

            switch (_options.updateAction) {
            case UpdateAction.ApplyLatest:
                break;
            }

            Log.Info("Finished Squirrel Updater");
            return 0;
        }
    }
}