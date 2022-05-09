using System;
using System.Diagnostics;
using System.Threading;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    class Program : IEnableLogger
    {
        static StartupOption _options;
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        public static int Main(string[] args)
        {
            SetupLogLogger logger = null;
            try {
                logger = new SetupLogLogger(Utility.GetDefaultTempBaseDirectory());
                SquirrelLocator.CurrentMutable.Register(() => logger, typeof(ILogger));
                _options = new StartupOption(args);

                if (_options.updateAction == UpdateAction.Unset) {
                    _options.WriteOptionDescriptions();
                    return -1;
                }

                Log.Info("Starting Squirrel Updater: " + String.Join(" ", args));
                Log.Info("Updater location is: " + SquirrelRuntimeInfo.EntryExePath);

                switch (_options.updateAction) {
                case UpdateAction.ApplyLatest:
                    ApplyLatestVersion(_options.updateCurrentApp, _options.updateStagingDir, _options.restartApp);
                    break;
                }

                Log.Info("Finished Squirrel Updater");
                return 0;
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                logger?.Write(ex.ToString(), LogLevel.Fatal);
                return -1;
            }
        }

        static void ApplyLatestVersion(string currentDir, string stagingDir, bool restartApp)
        {
            if (!Utility.FileHasExtension(currentDir, ".app")) {
                throw new ArgumentException("The current dir must end with '.app' on macos.");
            }
            // todo https://stackoverflow.com/questions/51441576/how-to-run-app-as-sudo
            // https://stackoverflow.com/questions/10283062/getting-sudo-to-ask-for-password-via-the-gui

            Process.Start("killall", $"`basename -a '{currentDir}'`")?.WaitForExit();

            var config = new UpdateConfig(null, null);
            config.UpdateAndRetrieveCurrentFolder(false);

            if (restartApp)
                ProcessUtil.InvokeProcess("open", new[] { "-n", currentDir }, null, CancellationToken.None);
        }
    }
}