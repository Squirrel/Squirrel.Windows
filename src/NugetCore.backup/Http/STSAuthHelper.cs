using System;
using System.Globalization;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Security;
using System.Text;
using NuGet.Resources;

namespace NuGet
{
    public static class STSAuthHelper
    {
        /// <summary>
        /// Response header that specifies the WSTrust13 Windows Transport endpoint.
        /// </summary>
        /// <remarks>
        /// TODO: Is there a way to discover this \ negotiate this endpoint?
        /// </remarks>
        private const string STSEndPointHeader = "X-NuGet-STS-EndPoint";

        /// <summary>
        /// Response header that specifies the realm to authenticate for. In most cases this would be the gallery we are going up against.
        /// </summary>
        private const string STSRealmHeader = "X-NuGet-STS-Realm";

        /// <summary>
        /// Request header that contains the SAML token.
        /// </summary>
        private const string STSTokenHeader = "X-NuGet-STS-Token";

        /// <summary>
        /// Adds the SAML token as a header to the request if it is already cached for this host.
        /// </summary>
        public static void PrepareSTSRequest(WebRequest request)
        {
            string cacheKey = GetCacheKey(request.RequestUri);
            string token;
            if (MemoryCache.Instance.TryGetValue(cacheKey, out token))
            {
                request.Headers[STSTokenHeader] = EncodeHeader(token);
            }
        }

        /// <summary>
        /// Attempts to retrieve a SAML token if the response indicates that server requires STS-based auth. 
        /// </summary>
        /// <param name="requestUri">The feed URI we were connecting to.</param>
        /// <param name="response">The 401 response we receieved from the server.</param>
        /// <returns>True if we were able to successfully retrieve a SAML token from the STS specified in the response headers.</returns>
        public static bool TryRetrieveSTSToken(Uri requestUri, IHttpWebResponse response)
        {
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                // We only care to do STS auth if the server returned a 401
                return false;
            }

            string endPoint = GetSTSEndPoint(response);
            string realm = response.Headers[STSRealmHeader];
            if (String.IsNullOrEmpty(endPoint) || String.IsNullOrEmpty(realm))
            {
                // The server does not conform to our STS-auth requirements. 
                return false;
            }

            string cacheKey = GetCacheKey(requestUri);
            
            // TODO: We need to figure out a way to cache the token for the duration of the token's validity (which is available as part of it's result).
            MemoryCache.Instance.GetOrAdd(cacheKey,
                                    () => GetSTSToken(requestUri, endPoint, realm),
                                    TimeSpan.FromMinutes(30),
                                    absoluteExpiration: true);
            return true;
        }

        private static string GetSTSToken(Uri requestUri, string endPoint, string appliesTo)
        {
            var typeProvider = WIFTypeProvider.GetWIFTypes();
            if (typeProvider == null)
            {
                throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture, NuGetResources.UnableToLocateWIF, requestUri));
            }

            var binding = new WS2007HttpBinding(SecurityMode.Transport);
            dynamic factory = Activator.CreateInstance(typeProvider.ChannelFactory, binding, endPoint);
            factory.TrustVersion = TrustVersion.WSTrust13;

            // Check if we can create 4.5 types. 
            dynamic rst = Activator.CreateInstance(typeProvider.RequestSecurityToken);
            rst.RequestType = GetFieldValue<string>(typeProvider.RequestTypes, "Issue");
            rst.KeyType = GetFieldValue<string>(typeProvider.KeyTypes, "Bearer");

            // Dynamic verifies the type of the instance so we cannot use it to assign a value for this property. 
            var endPointAddress = Activator.CreateInstance(typeProvider.EndPoint, appliesTo);
            SetProperty(rst, "AppliesTo", endPointAddress);

            dynamic channel = factory.CreateChannel();
            dynamic securityToken = channel.Issue(rst);
            return securityToken.TokenXml.OuterXml;
        }

        private static void SetProperty(object instance, string propertyName, object value)
        {
            var type = instance.GetType();
            var property = type.GetProperty(propertyName);

            var propertySetter = property.GetSetMethod();
            propertySetter.Invoke(instance, new[] { value });
        }

        private static TVal GetFieldValue<TVal>(Type type, string fieldName)
        {
            return (TVal)type.GetField(fieldName).GetValue(obj: null);
        }

        private static string GetSTSEndPoint(IHttpWebResponse response)
        {
            return response.Headers[STSEndPointHeader].SafeTrim();
        }

        private static string GetCacheKey(Uri requestUri)
        {
            return STSTokenHeader + "|" + requestUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped);
        }

        private static string EncodeHeader(string token)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        }
    }
}
