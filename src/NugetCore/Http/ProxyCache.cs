using System;
using System.Collections.Concurrent;
using System.Net;

namespace NuGet
{
    internal class ProxyCache : IProxyCache
    {
        private const string HostKey = "http_proxy";
        private const string UserKey = "http_proxy.user";
        private const string PasswordKey = "http_proxy.password";

        /// <summary>
        /// Capture the default System Proxy so that it can be re-used by the IProxyFinder
        /// because we can't rely on WebRequest.DefaultWebProxy since someone can modify the DefaultWebProxy
        /// property and we can't tell if it was modified and if we are still using System Proxy Settings or not.
        /// One limitation of this method is that it does not look at the config file to get the defined proxy
        /// settings.
        /// </summary>
        private static readonly IWebProxy _originalSystemProxy = WebRequest.GetSystemWebProxy();

        private readonly ConcurrentDictionary<Uri, WebProxy> _cache = new ConcurrentDictionary<Uri, WebProxy>();

#if BOOTSTRAPPER
        // Temporarily commenting these out until we can figure out a nicer way of doing this in the bootstrapper.

        private static readonly Lazy<ProxyCache> _instance = new Lazy<ProxyCache>(() => new ProxyCache());
                public ProxyCache()
        {

        }
#else 
        // It's not likely that http proxy settings are set in machine wide settings, 
        // so not passing machine wide settings to Settings.LoadDefaultSettings() should be fine.
        private static readonly Lazy<ProxyCache> _instance = new Lazy<ProxyCache>(() => new ProxyCache(Settings.LoadDefaultSettings(fileSystem: null, configFileName: null, machineWideSettings: null ), new EnvironmentVariableWrapper()));

        private readonly ISettings _settings;
        private readonly IEnvironmentVariableReader _environment;

        public ProxyCache(ISettings settings, IEnvironmentVariableReader environment)
        {
            _settings = settings;
            _environment = environment;
        }
#endif

        internal static ProxyCache Instance
        {
            get
            {
                return _instance.Value;
            }
        }

        public IWebProxy GetProxy(Uri uri)
        {
#if !BOOTSTRAPPER
            // Check if the user has configured proxy details in settings or in the environment.
            WebProxy configuredProxy = GetUserConfiguredProxy();
            if (configuredProxy != null)
            {
                // If a proxy was cached, it means the stored credentials are incorrect. Use the cached one in this case.
                WebProxy actualProxy;
                if (_cache.TryGetValue(configuredProxy.Address, out actualProxy))
                {
                    return actualProxy;
                }
                return configuredProxy;
            }
#endif 
            if (!IsSystemProxySet(uri))
            {
                return null;
            }

            WebProxy systemProxy = GetSystemProxy(uri);

            WebProxy effectiveProxy;
            // See if we have a proxy instance cached for this proxy address
            if (_cache.TryGetValue(systemProxy.Address, out effectiveProxy))
            {
                return effectiveProxy;
            }

            return systemProxy;
        }

#if !BOOTSTRAPPER
        internal WebProxy GetUserConfiguredProxy()
        {
            // Try reading from the settings. The values are stored as 3 config values http_proxy, http_proxy_user, http_proxy_password
            var host = _settings.GetConfigValue(HostKey);
            if (!String.IsNullOrEmpty(host))
            {
                // The host is the minimal value we need to assume a user configured proxy. 
                var webProxy = new WebProxy(host);
                string userName = _settings.GetConfigValue(UserKey);
                string password = _settings.GetConfigValue(PasswordKey, decrypt: true);

                if (!String.IsNullOrEmpty(userName) && !String.IsNullOrEmpty(password))
                {
                    webProxy.Credentials = new NetworkCredential(userName, password);
                }
                return webProxy;
            }

            // Next try reading from the environment variable http_proxy. This would be specified as http://<username>:<password>@proxy.com
            host = _environment.GetEnvironmentVariable(HostKey);
            Uri uri;
            if (!String.IsNullOrEmpty(host) && Uri.TryCreate(host, UriKind.Absolute, out uri))
            {
                var webProxy = new WebProxy(uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped));
                if (!String.IsNullOrEmpty(uri.UserInfo))
                {
                    var credentials = uri.UserInfo.Split(':');
                    if (credentials.Length > 1)
                    {
                        webProxy.Credentials = new NetworkCredential(userName: credentials[0], password: credentials[1]);
                    }
                }
                return webProxy;
            }
            return null;
        }
#endif

        public void Add(IWebProxy proxy)
        {
            var webProxy = proxy as WebProxy;
            if (webProxy != null)
            {
                _cache.TryAdd(webProxy.Address, webProxy);
            }
        }

        private static WebProxy GetSystemProxy(Uri uri)
        {
            // WebRequest.DefaultWebProxy seems to be more capable in terms of getting the default
            // proxy settings instead of the WebRequest.GetSystemProxy()
            var proxyUri = _originalSystemProxy.GetProxy(uri);
            return new WebProxy(proxyUri);
        }

        /// <summary>
        /// Return true or false if connecting through a proxy server
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        private static bool IsSystemProxySet(Uri uri)
        {
            // The reason for not calling the GetSystemProxy is because the object
            // that will be returned is no longer going to be the proxy that is set by the settings
            // on the users machine only the Address is going to be the same.
            // Not sure why the .NET team did not want to expose all of the useful settings like
            // ByPass list and other settings that we can't get because of it.
            // Anyway the reason why we need the DefaultWebProxy is to see if the uri that we are
            // getting the proxy for to should be bypassed or not. If it should be bypassed then
            // return that we don't need a proxy and we should try to connect directly.
            IWebProxy proxy = WebRequest.DefaultWebProxy;
            if (proxy != null)
            {
                Uri proxyUri = proxy.GetProxy(uri);
                if (proxyUri != null)
                {
                    Uri proxyAddress = new Uri(proxyUri.AbsoluteUri);
                    if (String.Equals(proxyAddress.AbsoluteUri, uri.AbsoluteUri))
                    {
                        return false;
                    }
                    bool bypassUri = proxy.IsBypassed(uri);
                    if (bypassUri)
                    {
                        return false;
                    }
                    proxy = new WebProxy(proxyAddress);
                }
            }

            return proxy != null;
        }
    }

}
