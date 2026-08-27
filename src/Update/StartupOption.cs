using Mono.Options;
using System;

namespace Squirrel.Update
{
    internal class StartupOption
    {
        private readonly OptionSet optionSet;

        internal bool silentInstall { get; private set; } = false;
        internal UpdateAction updateAction { get; private set; } = default(UpdateAction);
        internal string target { get; private set; } = default(string);
        internal string releaseDir { get; private set; } = default(string);
        internal string packagesDir { get; private set; } = default(string);
        internal string bootstrapperExe { get; private set; } = default(string);
        internal string backgroundGif { get; private set; } = default(string);
        internal string signingParameters { get; private set; } = default(string);
        internal string baseUrl { get; private set; } = default(string);
        internal string processStart { get; private set; } = default(string);
        internal string processStartArgs { get; private set; } = default(string);
        internal string setupIcon { get; private set; } = default(string);
        internal string icon { get; private set; } = default(string);
        internal string shortcutArgs { get; private set; } = default(string);
        internal string frameworkVersion { get; private set; } = "net45";
        internal bool shouldWait { get; private set; } = false;
        internal bool noMsi { get; private set; } = (Environment.OSVersion.Platform != PlatformID.Win32NT);        // NB: WiX doesn't work under Mono / Wine
        internal bool packageAs64Bit { get; private set; } = false;
        internal bool noDelta { get; private set; } = false;
               
        public StartupOption(string[] args) {
           optionSet = Parse(args);
        }

        private OptionSet Parse(string[] args) {
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
                { "releasify=", "Update or generate a releases directory with a given NuGet package", v => { updateAction = UpdateAction.Releasify; target = v; } },
                { "createShortcut=", "Create a shortcut for the given executable name", v => { updateAction = UpdateAction.Shortcut; target = v; } },
                { "removeShortcut=", "Remove a shortcut for the given executable name", v => { updateAction = UpdateAction.Deshortcut; target = v; } },
                { "updateSelf=", "Copy the currently executing Update.exe into the default location", v => { updateAction =  UpdateAction.UpdateSelf; target = v; } },
                { "processStart=", "Start an executable in the latest version of the app package", v => { updateAction =  UpdateAction.ProcessStart; processStart = v; }, true},
                { "processStartAndWait=", "Start an executable in the latest version of the app package", v => { updateAction =  UpdateAction.ProcessStart; processStart = v; shouldWait = true; }, true},
                "",
                "Options:",
                { "h|?|help", "Display Help and exit", _ => {} },
                { "r=|releaseDir=", "Path to a release directory to use with releasify", v => releaseDir = v},
                { "p=|packagesDir=", "Path to the NuGet Packages directory for C# apps", v => packagesDir = v},
                { "bootstrapperExe=", "Path to the Setup.exe to use as a template", v => bootstrapperExe = v},
                { "g=|loadingGif=", "Path to an animated GIF to be displayed during installation", v => backgroundGif = v},
                { "i=|icon", "Path to an ICO file that will be used for icon shortcuts", v => icon = v},
                { "setupIcon=", "Path to an ICO file that will be used for the Setup executable's icon", v => setupIcon = v},
                { "n=|signWithParams=", "Sign the installer via SignTool.exe with the parameters given", v => signingParameters = v},
                { "s|silent", "Silent install", _ => silentInstall = true},
                { "b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true},
                { "a=|process-start-args=", "Arguments that will be used when starting executable", v => processStartArgs = v, true},
                { "l=|shortcut-locations=", "Comma-separated string of shortcut locations, e.g. 'Desktop,StartMenu'", v => shortcutArgs = v},
                { "no-msi", "Don't generate an MSI package", v => noMsi = true},
                { "no-delta", "Don't generate delta packages to save time", v => noDelta = true},
                { "framework-version=", "Set the required .NET framework version, e.g. net461", v => frameworkVersion = v },
                { "msi-win64", "Mark the MSI as 64-bit, which is useful in Enterprise deployment scenarios", _ => packageAs64Bit = true},
            };

            opts.Parse(args);

            // NB: setupIcon and icon are just aliases for compatibility
            // reasons, because of a dumb breaking rename I made in 1.0.1
            setupIcon = setupIcon ?? icon;

            return opts;
        }

        internal void WriteOptionDescriptions() {
            optionSet.WriteOptionDescriptions(Console.Out);
        }
    }
}

