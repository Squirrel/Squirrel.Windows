using System;
using System.Collections.Specialized;
using System.Net;

namespace NuGet
{
    // For unit testing
    public interface IHttpWebResponse : IDisposable
    {
        HttpStatusCode StatusCode { get; }
        Uri ResponseUri { get; }
        string AuthType { get; }
        NameValueCollection Headers { get; }
    }
}
