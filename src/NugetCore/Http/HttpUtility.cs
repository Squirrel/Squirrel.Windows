using System;
using System.Globalization;
using System.Net;

namespace NuGet
{
    public static class HttpUtility
    {
        private const string UserAgentTemplate = "{0}/{1} ({2})";
        private const string UserAgentWithHostTemplate = "{0}/{1} ({2}, {3})";
        private const string ProjectGuidsHeader = "NuGet-ProjectGuids";

        public static string CreateUserAgentString(string client)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            var version = typeof(HttpUtility).Assembly.GetName().Version;
            return String.Format(CultureInfo.InvariantCulture, UserAgentTemplate, client, version, Environment.OSVersion);
        }

        public static string CreateUserAgentString(string client, string host)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            if (host == null)
            {
                throw new ArgumentNullException("host");
            }

            var version = typeof(HttpUtility).Assembly.GetName().Version;
            return String.Format(
                CultureInfo.InvariantCulture, 
                UserAgentWithHostTemplate, 
                client, 
                version /* NuGetnuget version */, 
                Environment.OSVersion /* OS version */, 
                host /* VS SKU + version */);
        }

        public static void SetUserAgent(WebRequest request, string userAgent, string projectGuids = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (userAgent == null)
            {
                throw new ArgumentNullException("userAgent");
            }

            var httpRequest = request as HttpWebRequest;
            if (httpRequest != null)
            {
                httpRequest.UserAgent = userAgent;
            }
            else
            {
                request.Headers[HttpRequestHeader.UserAgent] = userAgent;
            }

            if (!String.IsNullOrEmpty(projectGuids))
            {
                request.Headers[ProjectGuidsHeader] = projectGuids;
            }
            else
            {
                // this Request instance may be reused from the previous request. 
                // thus we clear the header to avoid sending project types of the previous request, if any
                request.Headers.Remove(ProjectGuidsHeader);
            }
        }
    }
}