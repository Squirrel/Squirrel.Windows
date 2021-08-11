using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;

namespace NuGet
{
    public interface IHttpClient : IHttpClientEvents
    {
        string UserAgent
        {
            get;
            set;
        }

        Uri Uri
        {
            get;
        }

        Uri OriginalUri
        {
            get;
        }

        bool AcceptCompression
        {
            get;
            set;
        }

        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "This is expensive")]
        WebResponse GetResponse();
        void InitializeRequest(WebRequest request);
        void DownloadData(Stream targetStream);
    }
}