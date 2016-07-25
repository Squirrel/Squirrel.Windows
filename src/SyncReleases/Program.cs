using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;
using Octokit;
using Splat;
using Squirrel;
using Squirrel.Json;

namespace SyncReleases
{
    class Program : IEnableLogger 
    {
        static OptionSet opts;

        public static int Main(string[] args)
        {
            var pg = new Program();
            try {
                return pg.main(args).Result;
            } catch (Exception ex) {
                // NB: Normally this is a terrible idea but we want to make
                // sure Setup.exe above us gets the nonzero error code
                Console.Error.WriteLine(ex);
                return -1;
            }
        }

        async Task<int> main(string[] args)
        {
            using (var logger = new SetupLogLogger(false) { Level = Splat.LogLevel.Info }) {
                Splat.Locator.CurrentMutable.Register(() => logger, typeof(Splat.ILogger));

                var releaseDir = default(string);
                var repoUrl = default(string);
                var token = default(string);

                opts = new OptionSet() {
                    "Usage: SyncReleases.exe command [OPTS]",
                    "Builds a Releases directory from releases on GitHub",
                    "",
                    "Options:",
                    { "h|?|help", "Display Help and exit", _ => {} },
                    { "r=|releaseDir=", "Path to a release directory to download to", v => releaseDir = v},
                    { "u=|url=", "When pointing to GitHub, use the URL to the repository root page, else point to an existing remote Releases folder", v => repoUrl = v},
                    { "t=|token=", "The OAuth token to use as login credentials", v => token = v},
                };

                opts.Parse(args);

                if (repoUrl == null || repoUrl.StartsWith("http", true, CultureInfo.InvariantCulture) == false) {
                    ShowHelp();
                    return -1;
                }

                var releaseDirectoryInfo = new DirectoryInfo(releaseDir ?? Path.Combine(".", "Releases"));
                if (!releaseDirectoryInfo.Exists) releaseDirectoryInfo.Create();

                var githubException = default(Exception);
                try {
                    await SyncImplementations.SyncFromGitHub(repoUrl, token, releaseDirectoryInfo);
                    return 0;
                } catch (Exception ex) {
                    githubException = ex;
                    Console.Error.WriteLine("Attempting to sync URL as remote RELEASES folder");
                }

                try {
                    await SyncImplementations.SyncRemoteReleases(new Uri(repoUrl), releaseDirectoryInfo);
                } catch (Exception) {
                    Console.Error.WriteLine("Failed to sync URL as GitHub repo: " + githubException.Message);
                    throw;
                }
            }

            return 0;
        }
        
        public void ShowHelp()
        {
            opts.WriteOptionDescriptions(Console.Out);
        }
    }

    class SetupLogLogger : Splat.ILogger, IDisposable
    {
        StreamWriter inner;
        readonly object gate = 42;
        public Splat.LogLevel Level { get; set; }

        public SetupLogLogger(bool saveInTemp)
        {
            var dir = saveInTemp ?
                Path.GetTempPath() : 
                Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            var file = Path.Combine(dir, "SquirrelSetup.log");
            if (File.Exists(file)) File.Delete(file);

            inner = new StreamWriter(file, false, Encoding.UTF8);
        }

        public void Write(string message, Splat.LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            lock (gate) inner.WriteLine(message);
        }

        public void Dispose()
        {
            lock(gate) inner.Dispose();
        }
    }
}
