using System;
using System.Net;

namespace NuGet
{
    public interface IProxyCache
    {
        void Add(IWebProxy proxy);
        IWebProxy GetProxy(Uri uri);
    }
}
