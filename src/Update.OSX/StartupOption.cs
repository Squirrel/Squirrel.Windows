using Mono.Options;
using System;
using System.IO;

namespace Squirrel.Update
{
    enum UpdateAction
    {
        Unset = 0, ApplyLatest
    }

    internal class StartupOption
    {
        private readonly OptionSet optionSet;
        internal UpdateAction updateAction { get; private set; } = default(UpdateAction);
        internal string updateCurrentApp { get; private set; }
        internal string updateStagingDir { get; private set; }
        internal bool restartApp { get; private set; }

        public StartupOption(string[] args)
        {
            optionSet = Parse(args);
        }

        private OptionSet Parse(string[] args)
        {
            var exeName = Path.GetFileName(SquirrelRuntimeInfo.EntryExePath);
            var opts = new OptionSet() {
                "",
#pragma warning disable CS0436 // Type conflicts with imported type
                $"Squirrel Updater (OSX) ({ThisAssembly.AssemblyInformationalVersion}) installs updates for Squirrel applications",
#pragma warning restore CS0436 // Type conflicts with imported type
                $"Usage: {exeName} command [OPTS]",
                "",
                "Commands:", {
                    "apply=", "Replace {0:CURRENT} .app with the latest in {1:STAGING}",
                    (v1, v2) => {
                        updateAction = UpdateAction.ApplyLatest;
                        updateCurrentApp = v1;
                        updateStagingDir = v2;
                    }
                },
                { "restartApp", "Launch the app after applying the latest version", v => restartApp = true },
                "",
                "Options:",
                { "h|?|help", "Display Help and exit", _ => { } },
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