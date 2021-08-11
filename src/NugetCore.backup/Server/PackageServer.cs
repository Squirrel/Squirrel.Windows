using System;
using System.Globalization;
using System.IO;
using System.Net;
using NuGet.Resources;

namespace NuGet
{
    public class PackageServer
    {
        private const string ServiceEndpoint = "/api/v2/package";
        private const string ApiKeyHeader = "X-NuGet-ApiKey";
        private const int MaxRediretionCount = 20;

        private Lazy<Uri> _baseUri;
        private readonly string _source;
        private readonly string _userAgent;

        public event EventHandler<WebRequestEventArgs> SendingRequest = delegate { };

        public PackageServer(string source, string userAgent)
        {
            if (String.IsNullOrEmpty(source))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "source");
            }
            _source = source;
            _userAgent = userAgent;
            _baseUri = new Lazy<Uri>(ResolveBaseUrl);
        }

        public string Source
        {
            get { return _source; }
        }

        /// <summary>
        /// Pushes a package to the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to push the package.</param>
        /// <param name="package">The package to be pushed.</param>
        /// <param name="timeout">Time in milliseconds to timeout the server request.</param>
        /// <param name="disableBuffering">Indicates if HttpWebRequest buffering should be disabled.</param>
        public void PushPackage(string apiKey, IPackage package, long packageSize, int timeout, bool disableBuffering) 
        {
            var sourceUri = new Uri(Source);
            if (sourceUri.IsFile)
            {
                PushPackageToFileSystem(
                    new PhysicalFileSystem(sourceUri.LocalPath),
                    package);
            }
            else
            {
                PushPackageToServer(apiKey, package.GetStream, packageSize, timeout, disableBuffering);
            }
        }

        /// <summary>
        /// Pushes a package to the server that is represented by the stream.
        /// </summary>
        /// <param name="apiKey">API key to be used to push the package.</param>
        /// <param name="packageStreamFactory">A delegate which can be used to open a stream for the package file.</param>
        /// <param name="contentLength">Size of the package to be pushed.</param>
        /// <param name="timeout">Time in milliseconds to timeout the server request.</param>
        /// <param name="disableBuffering">Disable buffering.</param>
        private void PushPackageToServer(
            string apiKey, 
            Func<Stream> packageStreamFactory, 
            long packageSize,
            int timeout,
            bool disableBuffering) 
        {
            int redirectionCount = 0;

            while (true)
            {
                HttpClient client = GetClient("", "PUT", "application/octet-stream");
                client.DisableBuffering = disableBuffering;

                client.SendingRequest += (sender, e) =>
                {
                    SendingRequest(this, e);
                    var request = (HttpWebRequest)e.Request;
                    
                    // Set the timeout
                    if (timeout <= 0)
                    {
                        timeout = request.ReadWriteTimeout; // Default to 5 minutes if the value is invalid.
                    }

                    request.Timeout = timeout;
                    request.ReadWriteTimeout = timeout;
                    if (!String.IsNullOrEmpty(apiKey))
                    {
                        request.Headers.Add(ApiKeyHeader, apiKey);
                    }

                    var multiPartRequest = new MultipartWebRequest();
                    multiPartRequest.AddFile(packageStreamFactory, "package", packageSize);

                    multiPartRequest.CreateMultipartRequest(request);
                };

                // When AllowWriteStreamBuffering is set to false, redirection will not be handled
                // automatically by HttpWebRequest. So we need to check redirect status code and
                // update _baseUri and retry if redirection happens.
                if (EnsureSuccessfulResponse(client))
                {
                    return;
                }

                ++redirectionCount;
                if (redirectionCount > MaxRediretionCount)
                {
                    throw new WebException(NuGetResources.Error_TooManyRedirections);
                }
            }
        }

        /// <summary>
        /// Pushes a package to a FileSystem.
        /// </summary>
        /// <param name="fileSystem">The FileSystem that the package is pushed to.</param>
        /// <param name="package">The package to be pushed.</param>
        private static void PushPackageToFileSystem(IFileSystem fileSystem, IPackage package)
        {
            var pathResolver = new DefaultPackagePathResolver(fileSystem);
            var packageFileName = pathResolver.GetPackageFileName(package);
            using (var stream = package.GetStream())
            {
                fileSystem.AddFile(packageFileName, stream);
            }
        }

        /// <summary>
        /// Deletes a package from the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to delete the package.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package version.</param>
        public void DeletePackage(string apiKey, string packageId, string packageVersion)
        {
            var sourceUri = new Uri(Source);
            if (sourceUri.IsFile)
            {
                DeletePackageFromFileSystem(
                    new PhysicalFileSystem(sourceUri.LocalPath),
                    packageId,
                    packageVersion);
            }
            else
            {
                DeletePackageFromServer(apiKey, packageId, packageVersion);
            }
        }

        /// <summary>
        /// Deletes a package from the server represented by the Source.
        /// </summary>
        /// <param name="apiKey">API key to be used to delete the package.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package Id.</param>
        private void DeletePackageFromServer(string apiKey, string packageId, string packageVersion)
        {
            // Review: Do these values need to be encoded in any way?
            var url = String.Join("/", packageId, packageVersion);
            HttpClient client = GetClient(url, "DELETE", "text/html");
            
            client.SendingRequest += (sender, e) =>
            {
                SendingRequest(this, e);
                var request = (HttpWebRequest)e.Request;
                request.Headers.Add(ApiKeyHeader, apiKey);
            };
            EnsureSuccessfulResponse(client);
        }

        /// <summary>
        /// Deletes a package from a FileSystem.
        /// </summary>
        /// <param name="fileSystem">The FileSystem where the specified package is deleted.</param>
        /// <param name="packageId">The package Id.</param>
        /// <param name="packageVersion">The package Id.</param>
        private static void DeletePackageFromFileSystem(IFileSystem fileSystem, string packageId, string packageVersion)
        {
            var pathResolver = new DefaultPackagePathResolver(fileSystem);
            var packageFileName = pathResolver.GetPackageFileName(packageId, new SemanticVersion(packageVersion));
            fileSystem.DeleteFile(packageFileName);
        }
        
        private HttpClient GetClient(string path, string method, string contentType)
        {
            var baseUrl = _baseUri.Value;
            Uri requestUri = GetServiceEndpointUrl(baseUrl, path);

            var client = new HttpClient(requestUri)
            {
                ContentType = contentType,
                Method = method
            };

            if (!String.IsNullOrEmpty(_userAgent))
            {
                client.UserAgent = HttpUtility.CreateUserAgentString(_userAgent);
            }

            return client;
        }

        internal static Uri GetServiceEndpointUrl(Uri baseUrl, string path)
        {
            Uri requestUri;
            if (String.IsNullOrEmpty(baseUrl.AbsolutePath.TrimStart('/')))
            {
                // If there's no host portion specified, append the url to the client.
                requestUri = new Uri(baseUrl, ServiceEndpoint + '/' + path);
            }
            else
            {
                requestUri = new Uri(baseUrl, path);
            }
            return requestUri;
        }

        /// <summary>
        /// Ensures that success response is received. 
        /// </summary>
        /// <param name="client">The client that is making the request.</param>
        /// <param name="expectedStatusCode">The exected status code.</param>
        /// <returns>True if success response is received; false if redirection response is received. 
        /// In this case, _baseUri will be updated to be the new redirected Uri and the requrest 
        /// should be retried.</returns>
        private bool EnsureSuccessfulResponse(HttpClient client, HttpStatusCode? expectedStatusCode = null)
        {
            HttpWebResponse response = null;
            try
            {
                response = (HttpWebResponse)client.GetResponse();
                if (response != null && 
                    ((expectedStatusCode.HasValue && expectedStatusCode.Value != response.StatusCode) || 

                    // If expected status code isn't provided, just look for anything 400 (Client Errors) or higher (incl. 500-series, Server Errors)
                    // 100-series is protocol changes, 200-series is success, 300-series is redirect.
                    (!expectedStatusCode.HasValue && (int)response.StatusCode >= 400)))
                {
                    throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture, NuGetResources.PackageServerError, response.StatusDescription, String.Empty));
                }

                return true;
            }
            catch (WebException e)
            {
                if (e.Response == null)
                {
                    throw;
                }
                response = (HttpWebResponse)e.Response;

                // Check if the error is caused by redirection
                if (response.StatusCode == HttpStatusCode.MultipleChoices ||
                    response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.SeeOther || 
                    response.StatusCode == HttpStatusCode.TemporaryRedirect)
                {
                    var location = response.Headers["Location"];
                    Uri newUri;
                    if (!Uri.TryCreate(client.Uri, location, out newUri))
                    {
                        throw;
                    }
                    
                    _baseUri = new Lazy<Uri>(() => newUri);
                    return false;
                } 

                if (expectedStatusCode != response.StatusCode)
                {
                    throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture, NuGetResources.PackageServerError, response.StatusDescription, e.Message), e);
                }

                return true;
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                    response = null;
                }
            }
        }

        private Uri ResolveBaseUrl()
        {
            Uri uri;

            try
            {
                var client = new RedirectedHttpClient(new Uri(Source));
                uri = client.Uri;
            }
            catch (WebException ex)
            {
                var response = (HttpWebResponse)ex.Response;
                if (response == null)
                {
                    throw;
                }

                uri = response.ResponseUri;
            }

            return EnsureTrailingSlash(uri);
        }

        private static Uri EnsureTrailingSlash(Uri uri)
        {
            string value = uri.OriginalString;
            if (!value.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                value += "/";
            }
            return new Uri(value);
        }
    }
}
