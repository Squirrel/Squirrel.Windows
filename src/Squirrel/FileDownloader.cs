using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
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
        /// Downloads a remote file to the specified local path
        /// </summary>
        /// <param name="url">The url which will be downloaded.</param>
        /// <param name="targetFile">
        /// The local path where the file will be stored
        /// If a file exists at this path, it will be overritten.</param>
        /// <param name="progress">
        /// A delegate for reporting download progress, with expected values from 0-100.
        /// </param>
        /// <param name="authorization">
        /// Text to be sent in the 'Authorization' header of the request.
        /// </param>
        Task DownloadFile(string url, string targetFile, Action<int> progress, string authorization = null);

        /// <summary>
        /// Returns a byte array containing the contents of the file at the specified url
        /// </summary>
        Task<byte[]> DownloadBytes(string url, string authorization = null);

        /// <summary>
        /// Returns a string containing the contents of the specified url
        /// </summary>
        Task<string> DownloadString(string url, string authorization = null);
    }

    /// <inheritdoc cref="IFileDownloader"/>
    public class HttpClientFileDownloader : IFileDownloader
    {
        /// <summary>
        /// The User-Agent sent with Squirrel requests
        /// </summary>
        public static ProductInfoHeaderValue UserAgent => new("Squirrel", AssemblyRuntimeInfo.ExecutingAssemblyName.Version.ToString());

        /// <inheritdoc />
        public virtual async Task DownloadFile(string url, string targetFile, Action<int> progress, string authorization)
        {
            using var client = CreateHttpClient(authorization);
            try {
                using (var fs = File.Open(targetFile, FileMode.Create)) {
                    await DownloadToStreamInternal(client, url, fs, progress).ConfigureAwait(false);
                }
            } catch {
                // NB: Some super brain-dead services are case-sensitive yet 
                // corrupt case on upload. I can't even.
                using (var fs = File.Open(targetFile, FileMode.Create)) {
                    await DownloadToStreamInternal(client, url.ToLower(), fs, progress).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public virtual async Task<byte[]> DownloadBytes(string url, string authorization)
        {
            using var client = CreateHttpClient(authorization);
            try {
                return await client.GetByteArrayAsync(url).ConfigureAwait(false);
            } catch {
                // NB: Some super brain-dead services are case-sensitive yet 
                // corrupt case on upload. I can't even.
                return await client.GetByteArrayAsync(url.ToLower()).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public virtual async Task<string> DownloadString(string url, string authorization)
        {
            using var client = CreateHttpClient(authorization);
            try {
                return await client.GetStringAsync(url).ConfigureAwait(false);
            } catch {
                // NB: Some super brain-dead services are case-sensitive yet 
                // corrupt case on upload. I can't even.
                return await client.GetStringAsync(url.ToLower()).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Asynchronously downloads a remote url to the specified destination stream while 
        /// providing progress updates.
        /// </summary>
        protected virtual async Task DownloadToStreamInternal(HttpClient client, string requestUri, Stream destination, Action<int> progress = null, CancellationToken cancellationToken = default)
        {
            // https://stackoverflow.com/a/46497896/184746
            // Get the http headers first to examine the content length
            using var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            using var download = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            // Ignore progress reporting when no progress reporter was 
            // passed or when the content length is unknown
            if (progress == null || !contentLength.HasValue) {
                await download.CopyToAsync(destination).ConfigureAwait(false);
                return;
            }

            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;
            int lastProgress = 0;
            while ((bytesRead = await download.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0) {
                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                totalBytesRead += bytesRead;

                // Convert absolute progress (bytes downloaded) into relative progress (0% - 100%)
                // and don't report progress < 3% difference, kind of like a shitty debounce.
                var curProgress = (int) ((double) totalBytesRead / contentLength.Value * 100);
                if (curProgress - lastProgress >= 3) {
                    lastProgress = curProgress;
                    progress(curProgress);
                }
            }

            if (lastProgress != 100)
                progress(100);
        }

        /// <summary>
        /// Creates a new <see cref="HttpClient"/> for every request. Override this
        /// function to add a custom proxy or other http configuration.
        /// </summary>
        protected virtual HttpClient CreateHttpClient(string authorization)
        {
            var handler = new HttpClientHandler() {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var client = new HttpClient(handler, true);
            client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
            if (authorization != null)
                client.DefaultRequestHeaders.Add("Authorization", authorization);
            return client;
        }
    }

    /// <inheritdoc cref="IFileDownloader"/>
    [Obsolete("Use HttpClientFileDownloader")]
    public class FileDownloader : IFileDownloader, IEnableLogger
    {
        /// <inheritdoc />
        public virtual async Task DownloadFile(string url, string targetFile, Action<int> progress, string authorization)
        {
            using (var wc = CreateWebClient()) {
                var failedUrl = default(string);
                wc.Headers.Add("Authorization", authorization);

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
        public virtual async Task<byte[]> DownloadBytes(string url, string authorization)
        {
            using (var wc = CreateWebClient()) {
                var failedUrl = default(string);
                wc.Headers.Add("Authorization", authorization);

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
        public virtual async Task<string> DownloadString(string url, string authorization)
        {
            using (var wc = CreateWebClient()) {
                var failedUrl = default(string);
                wc.Headers.Add("Authorization", authorization);

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
        protected virtual WebClient CreateWebClient()
        {
            var ret = new WebClient();
            var wp = WebRequest.DefaultWebProxy;
            if (wp != null) {
                wp.Credentials = CredentialCache.DefaultCredentials;
                ret.Proxy = wp;
            }

            return ret;
        }
    }
}
