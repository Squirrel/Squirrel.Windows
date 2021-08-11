using System;
using System.Collections.Concurrent;
using System.Net;

namespace NuGet
{
    internal class CredentialStore : ICredentialCache
    {
        private readonly ConcurrentDictionary<Uri, ICredentials> _credentialCache = new ConcurrentDictionary<Uri, ICredentials>();

        private static readonly CredentialStore _instance = new CredentialStore();

        public static CredentialStore Instance
        {
            get
            {
                return _instance;
            }
        }

        public ICredentials GetCredentials(Uri uri)
        {
            Uri rootUri = GetRootUri(uri);

            ICredentials credentials;
            if (_credentialCache.TryGetValue(uri, out credentials) ||
                _credentialCache.TryGetValue(rootUri, out credentials))
            {
                return credentials;
            }

            return null;
        }

        public void Add(Uri uri, ICredentials credentials)
        {
            Uri rootUri = GetRootUri(uri);
            _credentialCache.TryAdd(uri, credentials);
            _credentialCache.AddOrUpdate(rootUri, credentials, (u, c) => credentials);
        }

        internal static Uri GetRootUri(Uri uri)
        {
            return new Uri(uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped));
        }
    }
}
