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
                    { "h|?|help", "Display Help and exit", _ => {} },
                    { "r=|releaseDir=", "Path to a release directory to download to", v => releaseDir = v},
                    { "u=|repoUrl=", "The URL to the repository root page", v => repoUrl = v},
                    { "t=|token=", "The OAuth token to use as login credentials", v => token = v},
                };

                opts.Parse(args);

                if (token == null || repoUrl == null || repoUrl.StartsWith("http", true, CultureInfo.InvariantCulture) == false) {
                    ShowHelp();
                    return -1;
                }

                var releaseDirectoryInfo = new DirectoryInfo(releaseDir ?? Path.Combine(".", "Releases"));
                if (!releaseDirectoryInfo.Exists) releaseDirectoryInfo.Create();

                var repoUri = new Uri(repoUrl);
                var userAgent = new ProductHeaderValue("SyncGitHubReleases", Assembly.GetExecutingAssembly().GetName().Version.ToString());
                var client = new GitHubClient(userAgent, repoUri) {
                    Credentials = new Credentials(token)
                };

                var nwo = nwoFromRepoUrl(repoUrl);
                var releases = (await client.Release.GetAll(nwo.Item1, nwo.Item2))
                    .OrderByDescending(x => x.PublishedAt)
                    .Take(2);

                await releases.ForEachAsync(async release => {
                    // NB: Why do I have to double-fetch the release assets? It's already in GetAll
                    var assets = await client.Release.GetAssets(nwo.Item1, nwo.Item2, release.Id);

                    await assets
                        .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                        .Where(x => {
                            var fi = new FileInfo(Path.Combine(releaseDirectoryInfo.FullName, x.Name));
                            return !(fi.Exists && fi.Length == x.Size);
                        })
                        .ForEachAsync(async x => {
                            var target = new FileInfo(Path.Combine(releaseDirectoryInfo.FullName, x.Name));
                            if (target.Exists) target.Delete();
                            var retryCount = 3;

                        retry:

                            try {
                                var hc = new HttpClient();
                                var rq = new HttpRequestMessage(HttpMethod.Get, x.Url);
                                rq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/octet-stream"));
                                rq.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(userAgent.Name, userAgent.Version));
                                rq.Headers.Add("Authorization", "Bearer " + token);

                                var resp = await hc.SendAsync(rq);
                                resp.EnsureSuccessStatusCode();

                                using (var from = await resp.Content.ReadAsStreamAsync())
                                using (var to = File.OpenWrite(target.FullName)) {
                                    await from.CopyToAsync(to);
                                }
                            } catch (Exception ex) {
                                if (--retryCount > 0) goto retry;
                                throw;
                            }
                        });
                });

                var entries = releaseDirectoryInfo.GetFiles("*.nupkg")
                    .AsParallel()
                    .Select(x => ReleaseEntry.GenerateFromFile(x.FullName));

                ReleaseEntry.WriteReleaseFile(entries, Path.Combine(releaseDirectoryInfo.FullName, "RELEASES"));
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
            if (segments.Count() != 3) {
                throw new Exception("Repo URL must be to the root URL of the repo e.g. https://github.com/myuser/myrepo");
            }

            return Tuple.Create(segments[1], segments[2]);
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
