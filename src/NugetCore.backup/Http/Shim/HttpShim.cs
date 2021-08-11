using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NuGet
{
    /// <summary>
    /// HttpShim is a singleton that provides an event OnWebRequest for modifying WebRequests before they
    /// are executed.
    /// </summary>
    public sealed class HttpShim
    {
        private static HttpShim _instance;
        private Func<DataServiceClientRequestMessageArgs, DataServiceClientRequestMessage> _dataServiceHandler;
        private Func<WebRequest, WebResponse> _webHandler;

        internal HttpShim()
        {

        }

        /// <summary>
        ///  Static instance of the shim.
        /// </summary>
        public static HttpShim Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HttpShim();
                }

                return _instance;
            }
        }

        internal WebResponse ShimWebRequest(WebRequest request)
        {
            WebResponse response = null;

            InitializeRequest(request);

            if (_webHandler != null)
            {
                response = _webHandler(request);
            }
            else
            {
                response = request.GetResponse();
            }

            return response;
        }

        internal DataServiceClientRequestMessage ShimDataServiceRequest(DataServiceClientRequestMessageArgs args)
        {
            DataServiceClientRequestMessage message = null;

            if (_dataServiceHandler != null)
            {
                message = _dataServiceHandler(args);
            }
            else
            {
                message = new HttpWebRequestMessage(args);
            }

            // apply proxy and credential settings on the core web request
            InitializeMessage(message);

            return message;
        }

        public void SetWebRequestHandler(Func<WebRequest, WebResponse> handler)
        {
            _webHandler = handler;
        }

        public void SetDataServiceRequestHandler(Func<DataServiceClientRequestMessageArgs, DataServiceClientRequestMessage> handler)
        {
            _dataServiceHandler = handler;
        }

        public void ClearHandlers()
        {
            _dataServiceHandler = null;
            _webHandler = null;
        }

        // apply proxy settings and credentials here since they can no longer be applied from the context event
        private static void InitializeMessage(DataServiceClientRequestMessage message)
        {
            IShimWebRequest webRequestMessage = message as IShimWebRequest;
            HttpWebRequestMessage httpMessage = message as HttpWebRequestMessage;

            if (httpMessage != null)
            {
                InitializeRequest(httpMessage.HttpWebRequest);
            }
            else if (webRequestMessage != null)
            {
                InitializeRequest(webRequestMessage.Request);
            }
        }

        private static void InitializeRequest(WebRequest request)
        {
            try
            {
                SetCredentialsAndProxy(request);
                InitializeRequestProperties(request);
            }
            catch (InvalidOperationException)
            {
                // ignore failures here, that can be caused by GetResponse having already been called
            }
        }

        private static void SetCredentialsAndProxy(WebRequest request)
        {
            // Used the cached credentials and proxy we have
            if (request.Credentials == null)
            {
                request.Credentials = CredentialStore.Instance.GetCredentials(request.RequestUri);
            }

            if (request.Proxy == null)
            {
                request.Proxy = ProxyCache.Instance.GetProxy(request.RequestUri);
            }

            STSAuthHelper.PrepareSTSRequest(request);
        }

        private static void InitializeRequestProperties(WebRequest request)
        {
            var httpRequest = request as HttpWebRequest;
            if (httpRequest != null)
            {
                httpRequest.UserAgent = HttpUtility.CreateUserAgentString("NuGet Shim");
                httpRequest.CookieContainer = new CookieContainer();
                httpRequest.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
            }
        }
    }
}
