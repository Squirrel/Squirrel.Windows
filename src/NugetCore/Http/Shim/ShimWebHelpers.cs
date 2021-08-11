using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NuGet
{
    public static class ShimWebHelpers
    {

        public static HttpWebRequest AddHeaders(HttpWebRequest response, IEnumerable<KeyValuePair<string, string>> headers)
        {
            foreach (var header in headers)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(header.Key, "accept"))
                {
                    response.Accept = header.Value;
                }
                else if (StringComparer.OrdinalIgnoreCase.Equals(header.Key, "user-agent"))
                {
                    response.UserAgent = header.Value;
                }
                else if (StringComparer.OrdinalIgnoreCase.Equals(header.Key, "content-type"))
                {
                    response.ContentType = header.Value;
                }
                else
                {
                    response.Headers.Set(header.Key, header.Value);
                }
            }

            return response;
        }

        public static HttpWebRequest AddHeaders(HttpWebRequest response, IDictionary<string, string> headers)
        {
            return AddHeaders(response, headers.AsEnumerable());
        }

    }
}
