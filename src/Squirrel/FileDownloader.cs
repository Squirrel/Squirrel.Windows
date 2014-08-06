using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel
{
    public interface IFileDownloader
    {
        Task DownloadFile(string url, string targetFile);
        Task<byte[]> DownloadUrl(string url);
    }

    class FileDownloader : IFileDownloader
    {
        public async Task DownloadFile(string url, string targetFile)
        {
            var wc = new WebClient();
            await wc.DownloadFileTaskAsync(url, targetFile);
        }

        public Task<byte[]> DownloadUrl(string url)
        {
            var wc = new WebClient();
            return wc.DownloadDataTaskAsync(url);
        }
    }
}
