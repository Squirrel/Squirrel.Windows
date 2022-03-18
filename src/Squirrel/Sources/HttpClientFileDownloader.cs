using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Squirrel.Sources
{

    /// <inheritdoc cref="IFileDownloader"/>
    public class HttpClientFileDownloader : IFileDownloader
    {
        /// <summary>
        /// The User-Agent sent with Squirrel requests
        /// </summary>
        public static ProductInfoHeaderValue UserAgent => new("Squirrel", SquirrelRuntimeInfo.ExecutingAssemblyName.Version.ToString());

        /// <inheritdoc />
        public virtual async Task DownloadFile(string url, string targetFile, Action<int> progress, string authorization, string accept)
        {
            using var client = CreateHttpClient(authorization, accept);
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
        public virtual async Task<byte[]> DownloadBytes(string url, string authorization, string accept)
        {
            using var client = CreateHttpClient(authorization, accept);
            try {
                return await client.GetByteArrayAsync(url).ConfigureAwait(false);
            } catch {
                // NB: Some super brain-dead services are case-sensitive yet 
                // corrupt case on upload. I can't even.
                return await client.GetByteArrayAsync(url.ToLower()).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public virtual async Task<string> DownloadString(string url, string authorization, string accept)
        {
            using var client = CreateHttpClient(authorization, accept);
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

            if (lastProgress < 100)
                progress(100);
        }

        /// <summary>
        /// Creates a new <see cref="HttpClient"/> for every request. Override this
        /// function to add a custom proxy or other http configuration.
        /// </summary>
        protected virtual HttpClient CreateHttpClient(string authorization, string accept)
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

            if (accept != null)
                client.DefaultRequestHeaders.Add("Accept", accept);

            return client;
        }
    }
}
