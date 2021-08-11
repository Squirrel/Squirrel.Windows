using System;
using System.Net;

namespace NuGet
{
    public interface ICredentialCache
    {
        void Add(Uri uri, ICredentials credentials);
        ICredentials GetCredentials(Uri uri);
    }
}
