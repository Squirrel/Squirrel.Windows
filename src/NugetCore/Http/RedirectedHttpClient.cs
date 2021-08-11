using System;
using System.Globalization;
using System.Net;
using NuGet.Resources;

namespace NuGet
{
    /// <summary>
    /// This class should be used when ever you are using a link that is actually
    /// redirecting to the destination link that you want to use as the data source.
    /// A good example of that is a link that forwards like the current nuget link
    /// that is configured as a default location for nuget packages.
    /// </summary>
    public class RedirectedHttpClient : HttpClient
    {
        private const string RedirectedClientCacheKey = "RedirectedHttpClient|";
        private readonly Uri _originalUri;
        private readonly MemoryCache _memoryCache;

        public RedirectedHttpClient(Uri uri)
            : this(uri, MemoryCache.Instance)
        {
        }

        public RedirectedHttpClient(Uri uri, MemoryCache memoryCache) 
            : base(uri)
        {
            _originalUri = uri;
            _memoryCache = memoryCache;
        }

        public override WebResponse GetResponse()
        {
            return CachedClient.GetResponse();
        }

        public override Uri Uri
        {
            get
            {
                return CachedClient.Uri;
            }
        }

        public override Uri OriginalUri
        {
            get
            {
                return _originalUri;
            }
        }

        internal IHttpClient CachedClient
        {
            get
            {
                string cacheKey = GetCacheKey();
                // Reset the IHttpClient instance if we catch an Exception so that
                // subsequent requests are able to try and create it again in case there
                // was some issue with authentication or some other request related configuration
                // If we don't do it here then the exception is always thrown as soon as we
                // try to access the cached value property since it's backed up by Lazy<T>.
                try
                {
                    return _memoryCache.GetOrAdd(cacheKey, EnsureClient, TimeSpan.FromHours(1));
                }
                catch (Exception)
                {
                    // Re-initialize the cache and throw the exception so that we can 
                    // see what happened.
                    _memoryCache.Remove(cacheKey);
                    throw;
                }
            }
        }

        private string GetCacheKey()
        {
            return RedirectedClientCacheKey + _originalUri.OriginalString;
        }

        protected internal virtual IHttpClient EnsureClient()
        {
            var originalClient = new HttpClient(_originalUri);
            return new HttpClient(GetResponseUri(originalClient));
        }

        private Uri GetResponseUri(HttpClient client)
        {
            using (WebResponse response = client.GetResponse())
            {
                if (response == null)
                {
                    throw new InvalidOperationException(
                        String.Format(CultureInfo.CurrentCulture,
                                      NuGetResources.UnableToResolveUri,
                                      Uri));
                }

                return response.ResponseUri;
            }
        }
    }
}