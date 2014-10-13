using System;
using System.Net;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
    public interface IFileDownloader
    {
        Task DownloadFile(string url, string targetFile);
        Task<byte[]> DownloadUrl(string url);
    }

    class FileDownloader : IFileDownloader, IEnableLogger
    {
        public async Task DownloadFile(string url, string targetFile)
        {
            var wc = new WebClient();

            var failed = false;

            try
            {
                this.Log().Info("Downloading file: " + url);

                await this.WarnIfThrows(() => wc.DownloadFileTaskAsync(url, targetFile),
                    "Failed downloading URL: " + url);
            }
            catch (Exception)
            {
                failed = true;
            }

            if (failed)
            {
                this.Log().Info("Downloading failed, falling back to lowercase url");

                url = url.ToLower();

                await this.WarnIfThrows(() => wc.DownloadFileTaskAsync(url, targetFile),
                    "Failed downloading URL: " + url);
            }
        }

        public async Task<byte[]> DownloadUrl(string url)
        {
            var wc = new WebClient();

            try
            {
                this.Log().Info("Downloading url: " + url);

                return await this.WarnIfThrows(() => wc.DownloadDataTaskAsync(url),
                    "Failed to download url: " + url);
            }
            catch (Exception)
            {
            }

            this.Log().Info("Downloading failed, falling back to lowercase url");

            url = url.ToLower();

            return await this.WarnIfThrows(() => wc.DownloadDataTaskAsync(url),
                "Failed to download url: " + url);
        }
    }
}
