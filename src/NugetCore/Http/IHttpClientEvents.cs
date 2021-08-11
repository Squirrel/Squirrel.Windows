using System;

namespace NuGet
{
    public interface IHttpClientEvents : IProgressProvider
    {
        event EventHandler<WebRequestEventArgs> SendingRequest;
    }
}