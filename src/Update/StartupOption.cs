using Mono.Options;
using System;

namespace Squirrel.Update
{
    enum UpdateAction
    {
        Unset = 0, Install, Uninstall, Download, Update, Shortcut,
        Deshortcut, ProcessStart, UpdateSelf, CheckForUpdate
    }

    internal class StartupOption
    {
        private readonly OptionSet optionSet;
        internal bool silentInstall { get; private set; } = false;
        internal UpdateAction updateAction { get; private set; } = default(UpdateAction);
        internal string target { get; private set; } = default(string);
        //internal string baseUrl { get; private set; } = default(string);
        internal string processStart { get; private set; } = default(string);
        internal string processStartArgs { get; private set; } = default(string);
        internal string icon { get; private set; } = default(string);
        internal string shortcutArgs { get; private set; } = default(string);
        internal bool shouldWait { get; private set; } = false;
        internal bool onlyUpdateShortcuts { get; private set; } = false;

        public StartupOption(string[] args)
        {
            optionSet = Parse(args);
        }

        private OptionSet Parse(string[] args)
        {
            var opts = new OptionSet() {
                "Usage: Squirrel.exe command [OPTS]",
                "Manages Squirrel packages",
                "",
                "Commands",
                { "install=", "Install the app whose package is in the specified directory", v => { updateAction = UpdateAction.Install; target = v; } },
                { "uninstall", "Uninstall the app the same dir as Update.exe", v => updateAction = UpdateAction.Uninstall},
                { "download=", "Download the releases specified by the URL and write new results to stdout as JSON", v => { updateAction = UpdateAction.Download; target = v; } },
                { "checkForUpdate=", "Check for one available update and writes new results to stdout as JSON", v => { updateAction = UpdateAction.CheckForUpdate; target = v; } },
                { "update=", "Update the application to the latest remote version specified by URL", v => { updateAction = UpdateAction.Update; target = v; } },
                { "createShortcut=", "Create a shortcut for the given executable name", v => { updateAction = UpdateAction.Shortcut; target = v; } },
                { "removeShortcut=", "Remove a shortcut for the given executable name", v => { updateAction = UpdateAction.Deshortcut; target = v; } },
                { "updateSelf=", "Copy the currently executing Update.exe into the default location", v => { updateAction =  UpdateAction.UpdateSelf; target = v; } },
                { "processStart=", "Start an executable in the latest version of the app package", v => { updateAction =  UpdateAction.ProcessStart; processStart = v; }, true},
                { "processStartAndWait=", "Start an executable in the latest version of the app package", v => { updateAction =  UpdateAction.ProcessStart; processStart = v; shouldWait = true; }, true},
                "",
                "Options:",
                { "h|?|help", "Display Help and exit", _ => {} },
                { "i=|icon", "Path to an ICO file that will be used for icon shortcuts", v => icon = v},
                { "s|silent", "Silent install", _ => silentInstall = true},
                //{ "b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true},
                { "a=|process-start-args=", "Arguments that will be used when starting executable", v => processStartArgs = v, true},
                { "l=|shortcut-locations=", "Comma-separated string of shortcut locations, e.g. 'Desktop,StartMenu'", v => shortcutArgs = v},
                { "updateOnly", "Update shortcuts that already exist, rather than creating new ones", _ => onlyUpdateShortcuts = true},
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

