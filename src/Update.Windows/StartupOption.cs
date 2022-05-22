using Mono.Options;
using System;
using System.IO;

namespace Squirrel.Update
{
    enum UpdateAction
    {
        Unset = 0, Install, Uninstall, Download, Update, Shortcut,
        Deshortcut, ProcessStart, UpdateSelf, CheckForUpdate, Setup
    }

    internal class StartupOption
    {
        private readonly OptionSet optionSet;
        internal UpdateAction updateAction { get; private set; } = default(UpdateAction);
        internal string target { get; private set; } = default(string);
        internal string processStart { get; private set; } = default(string);
        internal string processStartArgs { get; private set; } = default(string);
        internal string icon { get; private set; } = default(string);
        internal string shortcutArgs { get; private set; } = default(string);
        internal bool shouldWait { get; private set; } = false;
        internal bool onlyUpdateShortcuts { get; private set; } = false;
        internal bool checkInstall { get; private set; } = false;
        internal bool silentInstall { get; private set; } = false;
        internal bool forceLatest { get; private set; } = false;
        internal long setupOffset { get; private set; } = 0;

        public StartupOption(string[] args)
        {
            optionSet = Parse(args);
        }

        private OptionSet Parse(string[] args)
        {
            var exeName = Path.GetFileName(SquirrelRuntimeInfo.EntryExePath);
            var opts = new OptionSet() {
                "",
                $"Squirrel Updater ({SquirrelRuntimeInfo.SquirrelDisplayVersion}) manages packages and installs updates for Squirrel applications",
                $"Usage: {exeName} command [OPTS]",
                "",
                "Commands:",
                { "uninstall", "Uninstall the app in the same directory as Update.exe", v => updateAction = UpdateAction.Uninstall},
                { "download=", "Download the releases specified by the URL and write new results to stdout as JSON", v => { updateAction = UpdateAction.Download; target = v; } },
                { "checkForUpdate=", "Check for one available update and writes new results to stdout as JSON", v => { updateAction = UpdateAction.CheckForUpdate; target = v; } },
                { "update=", "Update the application to the latest remote version specified by URL", v => { updateAction = UpdateAction.Update; target = v; } },
                { "createShortcut=", "Create a shortcut for the given executable name", v => { updateAction = UpdateAction.Shortcut; target = v; } },
                { "removeShortcut=", "Remove a shortcut for the given executable name", v => { updateAction = UpdateAction.Deshortcut; target = v; } },
                { "updateSelf=", "Copy the currently executing Update.exe into the default location", v => { updateAction =  UpdateAction.UpdateSelf; target = v; } },
                "",
                "Options:",
                { "h|?|help", "Display Help and exit", _ => {} },
                { "i=|icon", "Path to an ICO file that will be used for icon shortcuts", v => icon = v},
                { "l=|shortcut-locations=", "Comma-separated string of shortcut locations, e.g. 'Desktop,StartMenu'", v => shortcutArgs = v},
                { "updateOnly", "Update shortcuts that already exist, rather than creating new ones", _ => onlyUpdateShortcuts = true},

                // hidden arguments, used internally by Squirrel and should not be used by Squirrel applications themselves
                { "install=", "Install the app whose package is in the specified directory or url", v => { updateAction = UpdateAction.Install; target = v; }, true },
                { "s|silent", "Silent install", _ => silentInstall = true, true},
                { "processStart=", "Start an executable in the current version of the app package", v => { updateAction = UpdateAction.ProcessStart; processStart = v; }, true},
                { "processStartAndWait=", "Start an executable in the current version of the app package", v => { updateAction = UpdateAction.ProcessStart; processStart = v; shouldWait = true; }, true},
                { "forceLatest", "Force updates the current version folder", v => forceLatest = true},
                { "a=|process-start-args=", "Arguments that will be used when starting executable", v => processStartArgs = v, true},
                { "setup=", "Install the package at this location", v => {  updateAction = UpdateAction.Setup; target = v; }, true },
                { "setupOffset=", "Offset where in setup package to start reading", v => { setupOffset = long.Parse(v); }, true },
                { "checkInstall", "Will install the app silently if it is not currently installed. Used for machine-wide deployments", v => { checkInstall = true; }, true },
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

