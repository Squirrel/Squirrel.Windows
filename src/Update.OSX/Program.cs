using System;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    class Program : IEnableLogger
    {
        static StartupOption opt;
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        [STAThread]
        public static int Main(string[] args)
        {
            try {
                return main(args);
            } catch (Exception ex) {
                // NB: Normally this is a terrible idea but we want to make
                // sure Setup.exe above us gets the nonzero error code
                Console.Error.WriteLine(ex);
                return -1;
            }
        }

        static int main(string[] args)
        {
            try {
                opt = new StartupOption(args);
            } catch (Exception ex) {
                var logp = new SetupLogLogger(Utility.GetDefaultTempDirectory(null), false, UpdateAction.Unset) { Level = LogLevel.Info };
                logp.Write($"Failed to parse command line options. {ex.Message}", LogLevel.Error);
                throw;
            }

            // NB: Trying to delete the app directory while we have Setup.log
            // open will actually crash the uninstaller
            bool logToTemp = true;

            var logDir = logToTemp ? Utility.GetDefaultTempDirectory(null) : SquirrelRuntimeInfo.BaseDirectory;

            var logger = new SetupLogLogger(logDir, !logToTemp, opt.updateAction) { Level = LogLevel.Info };
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

            Console.WriteLine(opt.updateCurrentApp);
            Console.WriteLine(opt.updateStagingDir);

            if (opt.updateAction == UpdateAction.Unset) {
                opt.WriteOptionDescriptions();
                return -1;
            }


            switch (opt.updateAction) {
         
            }

            Log.Info("Finished Squirrel Updater");
            return 0;
        }
    }
}
