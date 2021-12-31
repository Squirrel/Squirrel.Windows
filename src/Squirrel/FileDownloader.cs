using System;
using System.Net;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    /// <summary>
    /// A simple abstractable file downloader
    /// </summary>
    public interface IFileDownloader
    {

        /// <summary>
        /// Download a file at the specified url to the specified local file
        /// </summary>
        Task DownloadFile(string url, string targetFile, Action<int> progress);

        /// <summary>
        /// Returns a byte array containing the contents of the file at the specified url
        /// </summary>
        Task<byte[]> DownloadUrl(string url);
    }

    /// <inheritdoc cref="IFileDownloader"/>
    public class FileDownloader : IFileDownloader, IEnableLogger
    {
        private readonly WebClient _providedClient;

        /// <summary>
        /// Create a new <see cref="FileDownloader"/>, optionally providing a custom WebClient
        /// </summary>
        public FileDownloader(WebClient providedClient = null)
        {
            _providedClient = providedClient;
        }

        /// <inheritdoc />
        public async Task DownloadFile(string url, string targetFile, Action<int> progress)
        {
            using (var wc = _providedClient ?? Utility.CreateWebClient()) {
                var failedUrl = default(string);

                var lastSignalled = DateTime.MinValue;
                wc.DownloadProgressChanged += (sender, args) => {
                    var now = DateTime.Now;

                    if (now - lastSignalled > TimeSpan.FromMilliseconds(500)) {
                        lastSignalled = now;
                        progress(args.ProgressPercentage);
                    }
                };

            retry:
                try {
                    this.Log().Info("Downloading file: " + (failedUrl ?? url));

                    await this.WarnIfThrows(
                        async () => {
                            await wc.DownloadFileTaskAsync(failedUrl ?? url, targetFile).ConfigureAwait(false);
                            progress(100);
                        },
                        "Failed downloading URL: " + (failedUrl ?? url)).ConfigureAwait(false);
                } catch (Exception) {
                    // NB: Some super brain-dead services are case-sensitive yet 
                    // corrupt case on upload. I can't even.
                    if (failedUrl != null) throw;

                    failedUrl = url.ToLower();
                    progress(0);
                    goto retry;
                }
            }
        }

        /// <inheritdoc />
        public async Task<byte[]> DownloadUrl(string url)
        {
            using (var wc = _providedClient ?? Utility.CreateWebClient()) {
                var failedUrl = default(string);

            retry:
                try {
                    this.Log().Info("Downloading url: " + (failedUrl ?? url));

                    return await this.WarnIfThrows(() => wc.DownloadDataTaskAsync(failedUrl ?? url),
                        "Failed to download url: " + (failedUrl ?? url)).ConfigureAwait(false);
                } catch (Exception) {
                    // NB: Some super brain-dead services are case-sensitive yet 
                    // corrupt case on upload. I can't even.
                    if (failedUrl != null) throw;

                    failedUrl = url.ToLower();
                    goto retry;
                }
            }
        }
    }
}
