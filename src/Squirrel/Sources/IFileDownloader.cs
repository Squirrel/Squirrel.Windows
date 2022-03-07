using System;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel.Sources
{
    /// <summary>
    /// A simple abstractable file downloader
    /// </summary>
    public interface IFileDownloader : IEnableLogger
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
        /// <param name="accept">
        /// Text to be sent in the 'Accept' header of the request.
        /// </param>
        Task DownloadFile(string url, string targetFile, Action<int> progress, string authorization = null, string accept = null);

        /// <summary>
        /// Returns a byte array containing the contents of the file at the specified url
        /// </summary>
        Task<byte[]> DownloadBytes(string url, string authorization = null, string accept = null);

        /// <summary>
        /// Returns a string containing the contents of the specified url
        /// </summary>
        Task<string> DownloadString(string url, string authorization = null, string accept = null);
    }
}
