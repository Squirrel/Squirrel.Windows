using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    [SupportedOSPlatform("osx")]
    class Program : IEnableLogger
    {
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        public static int Main(string[] args)
        {
            SetupLogLogger logger = null;
            try {
                logger = new SetupLogLogger(Utility.GetDefaultTempBaseDirectory());
                SquirrelLocator.CurrentMutable.Register(() => logger, typeof(ILogger));
                var opt = new StartupOption(args);

                if (opt.updateAction == UpdateAction.Unset) {
                    opt.WriteOptionDescriptions();
                    return -1;
                }

                Log.Info("Starting Squirrel Updater: " + String.Join(" ", args));
                Log.Info("Updater location is: " + SquirrelRuntimeInfo.EntryExePath);

                switch (opt.updateAction) {
                case UpdateAction.ProcessStart:
                    ProcessStart(opt.processStart, opt.processStartArgs, opt.shouldWait, opt.forceLatest);
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

        static void ProcessStart(string exeName, string arguments, bool shouldWait, bool forceLatest)
        {
            if (shouldWait) waitForParentToExit();

            // todo https://stackoverflow.com/questions/51441576/how-to-run-app-as-sudo
            // https://stackoverflow.com/questions/10283062/getting-sudo-to-ask-for-password-via-the-gui

            var desc = new AppDescOsx();
            var currentDir = desc.UpdateAndRetrieveCurrentFolder(forceLatest);

            ProcessUtil.InvokeProcess("open", new[] { "-n", currentDir }, null, CancellationToken.None);
        }

        static void waitForParentToExit()
        {
            var parentPid = NativeMac.getppid();
            var proc = Process.GetProcessById(parentPid);
            proc?.WaitForExit();
        }
    }
}