using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using Squirrel.SimpleSplat;

namespace Squirrel.Update
{
    [SupportedOSPlatform("osx")]
    class Program : IEnableLogger
    {
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        static AppDescOsx _app;
        static ILogger _logger;

        public static int Main(string[] args)
        {
            try {
                _app = new AppDescOsx();
                _logger = SetupLogLogger.RegisterLogger(_app.AppId);
                
                var opt = new StartupOption(args);

                if (opt.updateAction == UpdateAction.Unset) {
                    opt.WriteOptionDescriptions();
                    return -1;
                }

                Log.Info("Starting Squirrel Updater (OSX): " + String.Join(" ", args));
                Log.Info("Updater location is: " + SquirrelRuntimeInfo.EntryExePath);

                switch (opt.updateAction) {
                case UpdateAction.ProcessStart:
                    ProcessStart(opt.processStart, opt.processStartArgs, opt.shouldWait, opt.forceLatest);
                    break;
                }

                Log.Info("Finished Squirrel Updater (OSX)");
                return 0;
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                _logger?.Write(ex.ToString(), LogLevel.Fatal);
                return -1;
            }
        }

        static void ProcessStart(string exeName, string arguments, bool shouldWait, bool forceLatest)
        {
            if (_app.CurrentlyInstalledVersion == null)
                throw new InvalidOperationException("ProcessStart is only valid in an installed application");
            
            if (shouldWait) PlatformUtil.WaitForParentProcessToExit();

            // todo https://stackoverflow.com/questions/51441576/how-to-run-app-as-sudo
            // https://stackoverflow.com/questions/10283062/getting-sudo-to-ask-for-password-via-the-gui
            // /usr/bin/osascript -e 'do shell script "/path/to/myscript args 2>&1 etc" with administrator privileges'

            var currentDir = _app.UpdateAndRetrieveCurrentFolder(forceLatest);

            var exe = "/usr/bin/open";
            var args = $"-n \"{currentDir}\" --args {arguments}";
            
            Log.Info($"Running: {exe} {args}");
            
            PlatformUtil.StartProcessNonBlocking(exe, args, null);
        }
    }
}