using System;
using System.Net;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel.Sources
{
    /// This class is obsolete. Use <see cref="HttpClientFileDownloader"/> instead.
    [Obsolete("Use HttpClientFileDownloader")]
    public class WebClientFileDownloader : IFileDownloader
    {
        /// <inheritdoc />
        public virtual async Task DownloadFile(string url, string targetFile, Action<int> progress, string authorization, string accept)
        {
            using (var wc = CreateWebClient(authorization, accept)) {
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
        public virtual async Task<byte[]> DownloadBytes(string url, string authorization, string accept)
        {
            using (var wc = CreateWebClient(authorization, accept)) {
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

        /// <inheritdoc />
        public virtual async Task<string> DownloadString(string url, string authorization, string accept)
        {
            using (var wc = CreateWebClient(authorization, accept)) {
                var failedUrl = default(string);

            retry:
                try {
                    this.Log().Info("Downloading url: " + (failedUrl ?? url));

                    return await this.WarnIfThrows(() => wc.DownloadStringTaskAsync(failedUrl ?? url),
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

        /// <summary>
        /// Creates and returns a new WebClient for every requst
        /// </summary>
        protected virtual WebClient CreateWebClient(string authorization, string accept)
        {
            var ret = new WebClient();
            var wp = WebRequest.DefaultWebProxy;
            if (wp != null) {
                wp.Credentials = CredentialCache.DefaultCredentials;
                ret.Proxy = wp;
            }

            if (authorization != null)
                ret.Headers.Add("Authorization", authorization);

            if (accept != null)
                ret.Headers.Add("Accept", accept);

            return ret;
        }
    }
}
