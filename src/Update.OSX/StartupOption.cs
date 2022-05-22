using Mono.Options;
using System;
using System.IO;

namespace Squirrel.Update
{
    enum UpdateAction
    {
        Unset = 0, ProcessStart
    }

    internal class StartupOption
    {
        private readonly OptionSet optionSet;
        internal UpdateAction updateAction { get; private set; } = default(UpdateAction);
        internal string processStart { get; private set; } = default(string);
        internal string processStartArgs { get; private set; } = default(string);
        internal bool shouldWait { get; private set; } = false;
        internal bool forceLatest { get; private set; } = false;
        
        public StartupOption(string[] args)
        {
            optionSet = Parse(args);
        }

        private OptionSet Parse(string[] args)
        {
            var exeName = Path.GetFileName(SquirrelRuntimeInfo.EntryExePath);
            var opts = new OptionSet() {
                "",
                $"Squirrel Updater (OSX) ({SquirrelRuntimeInfo.SquirrelDisplayVersion}) installs updates for Squirrel applications",
                $"Usage: {exeName} command [OPTS]",
                "",
                "Commands:", 
                { "processStart=", "Start an executable in the current version of the app package", v => { updateAction = UpdateAction.ProcessStart; processStart = v; }, true},
                { "processStartAndWait=", "Start an executable in the current version of the app package", v => { updateAction = UpdateAction.ProcessStart; processStart = v; shouldWait = true; }, true},
                "",
                "Options:",
                { "h|?|help", "Display Help and exit", _ => { } },
                { "forceLatest", "Force updates the current version folder", v => forceLatest = true},
                { "a=|process-start-args=", "Arguments that will be used when starting executable", v => processStartArgs = v, true},
            };

            opts.Parse(args);

            return opts;
        }

        internal void WriteOptionDescriptions()
        {
            optionSet.WriteOptionDescriptions(Console.Out);
        }
    }
}