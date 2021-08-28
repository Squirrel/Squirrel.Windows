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
using Squirrel.SimpleSplat;
using Squirrel;
using Squirrel.Json;
using Squirrel.SyncReleases.Sources;

namespace Squirrel.SyncReleases
{
    class Program : IEnableLogger
    {
        static OptionSet opts;

        public static int Main(string[] args)
        {
            var pg = new Program();
            try {
                return pg.main(args).GetAwaiter().GetResult();
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                Console.Error.WriteLine("> SyncReleases.exe -h for help");
                return -1;
            }
        }

        async Task<int> main(string[] args)
        {
            using (var logger = new SetupLogLogger(false) { Level = Squirrel.SimpleSplat.LogLevel.Info }) {
                Squirrel.SimpleSplat.SquirrelLocator.CurrentMutable.Register(() => logger, typeof(Squirrel.SimpleSplat.ILogger));

                var releaseDir = default(string);
                var repoUrl = default(string);
                var token = default(string);
                var provider = default(string);
                var upload = default(bool);
                var showHelp = default(bool);

                var b2KeyId = default(string);
                var b2AppKey = default(string);
                var bucketId = default(string);

                opts = new OptionSet() {
                    "Usage: SyncReleases.exe  [OPTS]",
                    "Utility to download from or upload packages to a remote package release repository",
                    "Can be used to fully automate the distribution of new releases from CI or other scripts",
                    "",
                    "Options:",
                    { "h|?|help", "Display help and exit", v => showHelp = true },
                    { "r=|releaseDir=", "Path to the local release directory to sync with remote", v => releaseDir = v},
                    { "u=|url=", "A GitHub repository url, or a remote web url", v => repoUrl = v},
                    { "t=|token=", "The OAuth token to use as login credentials", v => token = v},
                    { "p=|provider=", "Specify the release repository type, can be 'github', 'web', or 'b2'", v => provider = v },
                    { "upload", "Upload releases in the releaseDir to the remote repository", v => upload = true },
                    { "bucketId=", "Id or name of the bucket in B2, S3, etc", v => bucketId = v },
                    { "b2keyid=", "B2 Auth Key Id", v => b2KeyId = v },
                    { "b2key=", "B2 Auth Key", v => b2AppKey = v },
                };

                opts.Parse(args);

                if (showHelp) {
                    ShowHelp();
                    return 0;
                }

                if (String.IsNullOrWhiteSpace(provider)) throw new ArgumentNullException(nameof(provider));
                if (String.IsNullOrWhiteSpace(releaseDir)) throw new ArgumentNullException(nameof(releaseDir));
                var releaseDirectoryInfo = new DirectoryInfo(releaseDir ?? Path.Combine(".", "Releases"));
                if (!releaseDirectoryInfo.Exists) releaseDirectoryInfo.Create();

                IPackageRepository repository;

                if (provider.Equals("github", StringComparison.OrdinalIgnoreCase)) {
                    if (String.IsNullOrWhiteSpace(repoUrl)) throw new ArgumentNullException(nameof(repoUrl));
                    if (String.IsNullOrWhiteSpace(token)) throw new ArgumentNullException(nameof(repoUrl));
                    repository = new GitHubRepository(repoUrl, token);
                } else if (provider.Equals("web", StringComparison.OrdinalIgnoreCase)) {
                    if (String.IsNullOrWhiteSpace(repoUrl)) throw new ArgumentNullException(nameof(repoUrl));
                    repository = new SimpleWebRepository(new Uri(repoUrl));
                } else if (provider.Equals("b2", StringComparison.OrdinalIgnoreCase)) {
                    if (String.IsNullOrWhiteSpace(b2KeyId)) throw new ArgumentNullException(nameof(b2KeyId));
                    if (String.IsNullOrWhiteSpace(b2AppKey)) throw new ArgumentNullException(nameof(b2AppKey));
                    if (String.IsNullOrWhiteSpace(bucketId)) throw new ArgumentNullException(nameof(bucketId));
                    repository = new BackblazeRepository(b2KeyId, b2AppKey, bucketId);
                } else {
                    throw new Exception("Release provider missing or invalid");
                }

                var mode = upload ? "Uploading" : "Downloading";
                Console.WriteLine(mode + " using provider " + repository.GetType().Name);

                if (upload) {
                    await repository.UploadMissingPackages(releaseDirectoryInfo);
                } else {
                    await repository.DownloadRecentPackages(releaseDirectoryInfo);
                }
            }

            return 0;
        }

        public void ShowHelp()
        {
            opts.WriteOptionDescriptions(Console.Out);
        }
    }

    class SetupLogLogger : Squirrel.SimpleSplat.ILogger, IDisposable
    {
        StreamWriter inner;
        readonly object gate = 42;
        public Squirrel.SimpleSplat.LogLevel Level { get; set; }

        public SetupLogLogger(bool saveInTemp)
        {
            var dir = saveInTemp ?
                Path.GetTempPath() :
                AppContext.BaseDirectory;

            var file = Path.Combine(dir, "SquirrelSetup.log");
            if (File.Exists(file)) File.Delete(file);

            inner = new StreamWriter(file, false, Encoding.UTF8);
        }

        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            lock (gate) inner.WriteLine(message);
        }

        public void Dispose()
        {
            lock (gate) inner.Dispose();
        }
    }
}
