using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;

namespace Update
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Any(x => x.StartsWith("/squirrel", StringComparison.OrdinalIgnoreCase))) {
                // NB: We're marked as Squirrel-aware, but we don't want to do
                // anything in response to these events
                return 0;
            }

            var opts = new OptionSet() {
                { "h|?|help", v => ShowHelp() }
            };

            opts.Parse(args);

            return 0;
        }

        static void ShowHelp()
        {
            Console.WriteLine("Help!");
        }
    }
}
