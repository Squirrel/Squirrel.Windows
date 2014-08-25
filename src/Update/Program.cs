using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;

namespace Update
{
    enum UpdateAction {
        Unset = 0, Install, Uninstall, Download, Update,
    }

    class Program
    {
        static OptionSet opts;

        static int Main(string[] args)
        {
            if (args.Any(x => x.StartsWith("/squirrel", StringComparison.OrdinalIgnoreCase))) {
                // NB: We're marked as Squirrel-aware, but we don't want to do
                // anything in response to these events
                return 0;
            }

            bool silentInstall = false;
            var updateAction = default(UpdateAction);
            string target = default(string);

            opts = new OptionSet() {
                "Usage: Update.exe command [OPTS]",
                "Manages Squirrel packages",
                "",
                "Commands",
                { "install=", "Install the app specified by the RELEASES file", v => { updateAction = UpdateAction.Install; target = v; } },
                { "uninstall", "Uninstall the app the same dir as Update.exe", v => updateAction = UpdateAction.Uninstall},
                { "download=", "Download the releases specified by the URL and write new results to stdout as JSON", v => { updateAction = UpdateAction.Download; target = v; } },
                { "update", "Update the application to the latest remote version", v => updateAction = UpdateAction.Update },
                "",
                "Options:",
                { "h|?|help", "Display Help and exit", _ => ShowHelp() },
                { "s|silent", "Silent install", _ => silentInstall = true},
            };

            opts.Parse(args);

            if (updateAction == UpdateAction.Unset) {
                ShowHelp();
            }

            return 0;
        }

        static void ShowHelp()
        {
            opts.WriteOptionDescriptions(Console.Out);
            Environment.Exit(0);
        }
    }
}
