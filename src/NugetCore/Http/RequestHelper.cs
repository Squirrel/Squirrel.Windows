using System;
using System.Collections.Specialized;
using System.Net;

namespace NuGet
{
    /// <summary>
    /// This class is used to keep sending requests until a response code that doesn't require 
    /// authentication happens or if the request requires authentication and 
    /// the user has stopped trying to enter them (i.e. they hit cancel when they are prompted).
    /// </summary>
    internal class RequestHelper
    {
        private Func<WebRequest> _createRequest;
        private Action<WebRequest> _prepareRequest;
        private IProxyCache _proxyCache;
        private ICredentialCache _credentialCache;
        private ICredentialProvider _credentialProvider;

        HttpWebRequest _previousRequest;
        IHttpWebResponse _previousResponse;
        HttpStatusCode? _previousStatusCode;
        int _credentialsRetryCount;
        bool _usingSTSAuth;
        bool _continueIfFailed;
        int _proxyCredentialsRetryCount;
        bool _basicAuthIsUsedInPreviousRequest;
        bool _disableBuffering;

        public RequestHelper(Func<WebRequest> createRequest,
            Action<WebRequest> prepareRequest,
            IProxyCache proxyCache,
            ICredentialCache credentialCache,
            ICredentialProvider credentialProvider,
            bool disableBuffering)
        {
            _createRequest = createRequest;
            _prepareRequest = prepareRequest;
            _proxyCache = proxyCache;
            _credentialCache = credentialCache;
            _credentialProvider = credentialProvider;
            _disableBuffering = disableBuffering;
        }

        public WebResponse GetResponse()
        {
            _previousRequest = null;
            _previousResponse = null;
            _previousStatusCode = null;
            _usingSTSAuth = false;
            _continueIfFailed = true;
            _proxyCredentialsRetryCount = 0;
            _credentialsRetryCount = 0;
            int failureCount = 0;
            const int MaxFailureCount = 10;            

            while (true)
            {
                // Create the request
                var request = (HttpWebRequest)_createRequest();
                ConfigureRequest(request);

                try
                {
                    if (_disableBuffering)
                    {
                        request.AllowWriteStreamBuffering = false;

                        // When buffering is disabled, we need to add the Authorization header 
                        // for basic authentication by ourselves.
                        bool basicAuth = _previousResponse != null &&
                            _previousResponse.AuthType != null &&
                            _previousResponse.AuthType.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) != -1;
                        var networkCredentials = request.Credentials.GetCredential(request.RequestUri, "Basic");
                        if (networkCredentials != null && basicAuth)
                        {
                            string authInfo = networkCredentials.UserName + ":" + networkCredentials.Password;
                            authInfo = Convert.ToBase64String(System.Text.Encoding.Default.GetBytes(authInfo));
                            request.Headers["Authorization"] = "Basic " + authInfo;
                            _basicAuthIsUsedInPreviousRequest = true;
                        }
                    }

                    // Prepare the request, we do something like write to the request stream
                    // which needs to happen last before the request goes out
                    _prepareRequest(request);

                    // Shim and replace this request if needed for v3
                    WebResponse response = HttpShim.Instance.ShimWebRequest(request);

                    // Cache the proxy and credentials
                    _proxyCache.Add(request.Proxy);

                    ICredentials credentials = request.Credentials;
                    _credentialCache.Add(request.RequestUri, credentials);
                    _credentialCache.Add(response.ResponseUri, credentials);

                    return response;
                }
                catch (WebException ex)
                {
                    ++failureCount;
                    if (failureCount >= MaxFailureCount)
                    {
                        throw;
                    }

                    using (IHttpWebResponse response = GetResponse(ex.Response))
                    {
                        if (response == null &&
                            ex.Status != WebExceptionStatus.SecureChannelFailure)
                        {
                            // No response, something went wrong so just rethrow
                            throw;
                        }

                        // Special case https connections that might require authentication
                        if (ex.Status == WebExceptionStatus.SecureChannelFailure)
                        {
                            if (_continueIfFailed)
                            {
                                // Act like we got a 401 so that we prompt for credentials on the next request
                                _previousStatusCode = HttpStatusCode.Unauthorized;
                                continue;
                            }
                            throw;
                        }

                        // If we were trying to authenticate the proxy or the request and succeeded, cache the result.
                        if (_previousStatusCode == HttpStatusCode.ProxyAuthenticationRequired &&
                            response.StatusCode != HttpStatusCode.ProxyAuthenticationRequired)
                        {
                            _proxyCache.Add(request.Proxy);
                        }
                        else if (_previousStatusCode == HttpStatusCode.Unauthorized &&
                                 response.StatusCode != HttpStatusCode.Unauthorized)
                        {
                            _credentialCache.Add(request.RequestUri, request.Credentials);
                            _credentialCache.Add(response.ResponseUri, request.Credentials);
                        }

                        _usingSTSAuth = STSAuthHelper.TryRetrieveSTSToken(request.RequestUri, response);

                        if (!IsAuthenticationResponse(response) || !_continueIfFailed)
                        {
                            throw;
                        }

                        if (!EnvironmentUtility.IsNet45Installed &&
                            request.AllowWriteStreamBuffering == false &&
                            response.AuthType != null &&
                            IsNtlmOrKerberos(response.AuthType))
                        {
                            // integrated windows authentication does not work when buffering is 
                            // disabled on .net 4.0.
                            throw;
                        }

                        _previousRequest = request;
                        _previousResponse = response;
                        _previousStatusCode = _previousResponse.StatusCode;
                    }
                }
            }
        }

        private void ConfigureRequest(HttpWebRequest request)
        {
            request.Proxy = _proxyCache.GetProxy(request.RequestUri);
            if (request.Proxy != null && request.Proxy.Credentials == null)
            {
                request.Proxy.Credentials = CredentialCache.DefaultCredentials;
            }

            if (_previousResponse == null || ShouldKeepAliveBeUsedInRequest(_previousRequest, _previousResponse))
            {
                // Try to use the cached credentials (if any, for the first request)
                request.Credentials = _credentialCache.GetCredentials(request.RequestUri);

                // If there are no cached credentials, use the default ones
                if (request.Credentials == null)
                {
                    request.UseDefaultCredentials = true;
                }
            }
            else if (_previousStatusCode == HttpStatusCode.ProxyAuthenticationRequired)
            {
                request.Proxy.Credentials = _credentialProvider.GetCredentials(
                    request, CredentialType.ProxyCredentials, retrying: _proxyCredentialsRetryCount > 0);
                _continueIfFailed = request.Proxy.Credentials != null;
                _proxyCredentialsRetryCount++;
            }
            else if (_previousStatusCode == HttpStatusCode.Unauthorized)
            {
                SetCredentialsOnAuthorizationError(request);
            }            

            SetKeepAliveHeaders(request, _previousResponse);
            if (_usingSTSAuth)
            {
                // Add request headers if the server requires STS based auth.
                STSAuthHelper.PrepareSTSRequest(request);
            }

            // Wrap the credentials in a CredentialCache in case there is a redirect
            // and credentials need to be kept around.
            request.Credentials = request.Credentials.AsCredentialCache(request.RequestUri);
        }

        private void SetCredentialsOnAuthorizationError(HttpWebRequest request)
        {
            if (_usingSTSAuth)
            {
                // If we are using STS, the auth's being performed by a request header. 
                // We do not need to ask the user for credentials at this point.
                return;
            }

            // When buffering is disabled, we need to handle basic auth ourselves.
            bool basicAuth = _previousResponse.AuthType != null &&
                _previousResponse.AuthType.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) != -1;
            if (_disableBuffering && basicAuth && !_basicAuthIsUsedInPreviousRequest)
            {
                // The basic auth credentials were not sent in the last request. 
                // We need to try with cached credentials in this request.        
                request.Credentials = _credentialCache.GetCredentials(request.RequestUri);
            }            

            if (request.Credentials == null)
            {
                request.Credentials = _credentialProvider.GetCredentials(
                    request, CredentialType.RequestCredentials, retrying: _credentialsRetryCount > 0);
            }

            _continueIfFailed = request.Credentials != null;
            _credentialsRetryCount++;
        }

        private static IHttpWebResponse GetResponse(WebResponse response)
        {
            var httpWebResponse = response as IHttpWebResponse;
            if (httpWebResponse == null)
            {
                var webResponse = response as HttpWebResponse;
                if (webResponse == null)
                {
                    return null;
                }
                return new HttpWebResponseWrapper(webResponse);
            }

            return httpWebResponse;
        }

        private static bool IsAuthenticationResponse(IHttpWebResponse response)
        {
            return response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired;
        }

        private static void SetKeepAliveHeaders(HttpWebRequest request, IHttpWebResponse previousResponse)
        {
            // KeepAlive is required for NTLM and Kerberos authentication. If we've never been authenticated or are using a different auth, we 
            // should not require KeepAlive.
            // REVIEW: The WWW-Authenticate header is tricky to parse so a Equals might not be correct. 
            if (previousResponse == null ||
                !IsNtlmOrKerberos(previousResponse.AuthType))
            {
                // This is to work around the "The underlying connection was closed: An unexpected error occurred on a receive."
                // exception.
                request.KeepAlive = false;
                request.ProtocolVersion = HttpVersion.Version10;
            }
        }

        private static bool ShouldKeepAliveBeUsedInRequest(HttpWebRequest request, IHttpWebResponse response)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (response == null)
            {
                throw new ArgumentNullException("response");
            }

            return !request.KeepAlive && IsNtlmOrKerberos(response.AuthType);
        }

        private static bool IsNtlmOrKerberos(string authType)
        {
            if (String.IsNullOrEmpty(authType))
            {
                return false;
            }

            return authType.IndexOf("NTLM", StringComparison.OrdinalIgnoreCase) != -1
                || authType.IndexOf("Kerberos", StringComparison.OrdinalIgnoreCase) != -1;
        }

        private class HttpWebResponseWrapper : IHttpWebResponse
        {
            private readonly HttpWebResponse _response;
            public HttpWebResponseWrapper(HttpWebResponse response)
            {
                _response = response;
            }

            public string AuthType
            {
                get
                {
                    return _response.Headers[HttpResponseHeader.WwwAuthenticate];
                }
            }

            public HttpStatusCode StatusCode
            {
                get
                {
                    return _response.StatusCode;
                }
            }

            public Uri ResponseUri
            {
                get
                {
                    return _response.ResponseUri;
                }
            }

            public NameValueCollection Headers
            {
                get
                {
                    return _response.Headers;
                }
            }

            public void Dispose()
            {
                if (_response != null)
                {
                    _response.Close();
                }
            }
        }
    }
}
