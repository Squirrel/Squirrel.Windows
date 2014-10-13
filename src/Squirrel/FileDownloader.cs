using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
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
        public Task DownloadFile(string url, string targetFile)
        {
            var wc = new WebClient();

            url = url.GetFinalUrl();

            this.Log().Info("Downloading file: " + url);

            return this.WarnIfThrows(() => wc.DownloadFileTaskAsync(url, targetFile),
                "Failed downloading URL: " + url);
        }

        public Task<byte[]> DownloadUrl(string url)
        {
            var wc = new WebClient();

            url = url.GetFinalUrl();

            this.Log().Info("Downloading url: " + url);

            return this.WarnIfThrows(() => wc.DownloadDataTaskAsync(url),
                "Failed to download url: " + url);
        }
    }
}
