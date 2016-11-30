﻿using System;
using System.Net;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
    public interface IFileDownloader
    {
        Task DownloadFile(string url, string targetFile, Action<int> progress);
        Task<byte[]> DownloadUrl(string url);
    }

    public class FileDownloader : IFileDownloader, IEnableLogger
    {
        private readonly Func<WebClient> _webClientFactory;

        public FileDownloader(Func<WebClient> webClientFactory)
        {
            _webClientFactory = () => (webClientFactory ?? Utility.CreateWebClient)() ?? Utility.CreateWebClient();
        }

        public FileDownloader(WebClient providedClient = null)
             : this(() => providedClient ?? Utility.CreateWebClient())
        {
        }

        public async Task DownloadFile(string url, string targetFile, Action<int> progress)
        {
            using (var wc = _webClientFactory()) {
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

                    await this.WarnIfThrows(() => wc.DownloadFileTaskAsync(failedUrl ?? url, targetFile),
                        "Failed downloading URL: " + (failedUrl ?? url));
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

        public async Task<byte[]> DownloadUrl(string url)
        {
            using (var wc = _webClientFactory()) {
            var failedUrl = default(string);

        retry:
            try {
                this.Log().Info("Downloading url: " + (failedUrl ?? url));

                return await this.WarnIfThrows(() => wc.DownloadDataTaskAsync(failedUrl ?? url),
                    "Failed to download url: " + (failedUrl ?? url));
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
