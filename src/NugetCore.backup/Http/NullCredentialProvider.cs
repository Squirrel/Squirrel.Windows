using System;
using System.Net;

namespace NuGet
{
    public class NullCredentialProvider : ICredentialProvider
    {
        private static readonly NullCredentialProvider _instance = new NullCredentialProvider();

        public static ICredentialProvider Instance
        {
            get
            {
                return _instance;
            }
        }

        private NullCredentialProvider()
        {

        }

        public ICredentials GetCredentials(Uri uri, IWebProxy proxy, CredentialType credentialType, bool retrying)
        {
            return null;
        }
    }
}
