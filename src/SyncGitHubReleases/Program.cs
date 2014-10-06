using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;
using Octokit;
using Splat;
using Squirrel;

namespace SyncGitHubReleases
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
                    "Usage: SyncGitHubReleases.exe command [OPTS]",
                    "Builds a Releases directory from releases on GitHub",
                    "",
                    "Options:",
                    { "h|?|help", "Display Help and exit", _ => ShowHelp() },
                    { "r=|releaseDir=", "Path to a release directory to use with releasify", v => releaseDir = v},
                    { "u=|repoUrl=", "The URL to the repository root page", v => repoUrl = v},
                    { "t=|token=", "The OAuth token to use as login credentials", v => token = v},
                };

                opts.Parse(args);

                if (token == null || repoUrl == null || repoUrl.StartsWith("http", true, CultureInfo.InvariantCulture)) {
                    ShowHelp();
                    return -1;
                }

                var repoUri = new Uri(repoUrl);
                var userAgent = new ProductHeaderValue("SyncGitHubReleases " + Assembly.GetExecutingAssembly().GetName().Version);
                var client = new GitHubClient(userAgent, repoUri) {
                    Credentials = new Credentials(token)
                };

                var repo = nwoFromRepoUrl(repoUrl);
                var allDownloads = await client.Release.GetAll(repo.Item1, repo.Item2);

                allDownloads.ForEachAsync
            }

            return 0;
        }

        public void ShowHelp()
        {
            opts.WriteOptionDescriptions(Console.Out);
        }

        Tuple<string, string> nwoFromRepoUrl(string repoUrl)
        {
            var uri = new Uri(repoUrl);

            var segments = uri.AbsolutePath.Split('/');
            if (segments.Count() != 2) {
                throw new Exception("Repo URL must be to the root URL of the repo e.g. https://github.com/myuser/myrepo");
            }

            return Tuple.Create(segments[0], segments[1]);
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
