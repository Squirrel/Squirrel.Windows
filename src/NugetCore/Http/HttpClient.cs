using System;
using System.IO;
using System.Net;

namespace NuGet
{
    public class HttpClient : IHttpClient
    {
        public event EventHandler<ProgressEventArgs> ProgressAvailable = delegate { };
        public event EventHandler<WebRequestEventArgs> SendingRequest = delegate { };

        private static ICredentialProvider _credentialProvider;
        private Uri _uri;

        public HttpClient(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException("uri");
            }

            _uri = uri;
        }

        public string UserAgent
        {
            get;
            set;
        }

        public virtual Uri Uri
        {
            get
            {
                return _uri;
            }
            set
            {
                _uri = value;
            }
        }

        public virtual Uri OriginalUri
        {
            get { return _uri; }
        }

        public string Method
        {
            get;
            set;
        }

        public string ContentType
        {
            get;
            set;
        }

        public bool AcceptCompression
        {
            get;
            set;
        }

        public bool DisableBuffering
        {
            get;
            set;
        }

        // TODO: Get rid of this. Horrid to have static properties like this especially in a code path that does not look thread safe.
        public static ICredentialProvider DefaultCredentialProvider
        {
            get
            {
                return _credentialProvider ?? NullCredentialProvider.Instance;
            }
            set
            {
                _credentialProvider = value;
            }
        }

        public virtual WebResponse GetResponse()
        {
            Func<WebRequest> webRequestFactory = () =>
            {
                WebRequest request = WebRequest.Create(Uri);
                InitializeRequestProperties(request);
                return request;
            };

            var requestHelper = new RequestHelper(
                webRequestFactory,
                RaiseSendingRequest,
                ProxyCache.Instance,
                CredentialStore.Instance,
                DefaultCredentialProvider,
                DisableBuffering);
            return requestHelper.GetResponse();
        }

        public void InitializeRequest(WebRequest request)
        {
            // TODO: Clean this up. This is no longer used with the move to WCF 5.6.1, changes to the request are not applied to the actual request.

            // Setup the request properties like content type and compression
            InitializeRequestProperties(request);

            // Use credentials from the cache if any for this uri
            TrySetCredentialsAndProxy(request);

            // Give clients a chance to examine/modify the request object before the request actually goes out.
            RaiseSendingRequest(request);
        }

        private void TrySetCredentialsAndProxy(WebRequest request)
        {
            // Used the cached credentials and proxy we have
            request.Credentials = CredentialStore.Instance.GetCredentials(Uri);
            request.Proxy = ProxyCache.Instance.GetProxy(Uri);
            STSAuthHelper.PrepareSTSRequest(request);
        }

        private void InitializeRequestProperties(WebRequest request)
        {
            var httpRequest = request as HttpWebRequest;
            if (httpRequest != null)
            {
                httpRequest.UserAgent = UserAgent ?? HttpUtility.CreateUserAgentString("NuGet");
                httpRequest.CookieContainer = new CookieContainer();
                if (AcceptCompression)
                {
                    httpRequest.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
                }
            }

            if (!String.IsNullOrEmpty(ContentType))
            {
                request.ContentType = ContentType;
            }

            if (!String.IsNullOrEmpty(Method))
            {
                request.Method = Method;
            }
        }

        public void DownloadData(Stream targetStream)
        {
            const int ChunkSize = 1024 * 4; // 4KB

            using (var response = GetResponse())
            {
                // Total response length
                int length = (int)response.ContentLength;
                using (Stream stream = response.GetResponseStream())
                {
                    // in some circumstances, the Content-Length response header is missing, resulting in
                    // the ContentLength = -1. In which case, we copy the whole stream and do not report progress.
                    if (length < 0)
                    {
                        stream.CopyTo(targetStream);

                        // reporting fake progress as 100%
                        OnProgressAvailable(100);
                    }
                    else
                    {
                        // We read the response stream chunk by chunk (each chunk is 4KB). 
                        // After reading each chunk, we report the progress based on the total number bytes read so far.
                        int totalReadSoFar = 0;
                        byte[] buffer = new byte[ChunkSize];
                        while (totalReadSoFar < length)
                        {
                            int bytesRead = stream.Read(buffer, 0, Math.Min(length - totalReadSoFar, ChunkSize));
                            if (bytesRead == 0)
                            {
                                break;
                            }
                            else
                            {
                                targetStream.Write(buffer, 0, bytesRead);

                                totalReadSoFar += bytesRead;
                                OnProgressAvailable((totalReadSoFar * 100) / length);
                            }
                        }
                    }
                }
            }
        }

        private void OnProgressAvailable(int percentage)
        {
            ProgressAvailable(this, new ProgressEventArgs(percentage));
        }

        private void RaiseSendingRequest(WebRequest webRequest)
        {
            SendingRequest(this, new WebRequestEventArgs(webRequest));
        }
    }
}