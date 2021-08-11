using System;
using System.Net;

namespace NuGet
{
    internal static class CredentialProviderExtensions
    {
        private static readonly string[] _authenticationSchemes = new[] { "Basic", "NTLM", "Negotiate" };

        internal static ICredentials GetCredentials(this ICredentialProvider provider, WebRequest request, CredentialType credentialType, bool retrying = false)
        {
            return provider.GetCredentials(request.RequestUri, request.Proxy, credentialType, retrying);
        }

        internal static ICredentials AsCredentialCache(this ICredentials credentials, Uri uri)
        {
            // No credentials then bail
            if (credentials == null)
            {
                return null;
            }

            // Do nothing with default credentials
            if (credentials == CredentialCache.DefaultCredentials ||
                credentials == CredentialCache.DefaultNetworkCredentials)
            {
                return credentials;
            }

            // If this isn't a NetworkCredential then leave it alone
            var networkCredentials = credentials as NetworkCredential;
            if (networkCredentials == null)
            {
                return credentials;
            }

            // Set this up for each authentication scheme we support
            // The reason we're using a credential cache is so that the HttpWebRequest will forward our
            // credentials if there happened to be any redirects in the chain of requests.
            var cache = new CredentialCache();
            foreach (var scheme in _authenticationSchemes)
            {
                cache.Add(uri, scheme, networkCredentials);
            }
            return cache;
        }
    }
}
