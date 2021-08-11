using System;
using System.Net;

namespace NuGet
{
    /// <summary>
    /// This interface represents the basic interface that one needs to implement in order to
    /// support repository authentication. 
    /// </summary>
    public interface ICredentialProvider
    {
        /// <summary>
        /// Returns CredentialState state that let's the consumer know if ICredentials
        /// were discovered by the ICredentialProvider. The credentials argument is then
        /// populated with the discovered valid credentials that can be used for the given Uri.
        /// The proxy instance if passed will be used to ensure that the request goes through the proxy
        /// to ensure successful connection to the destination Uri.
        /// </summary>
        ICredentials GetCredentials(Uri uri, IWebProxy proxy, CredentialType credentialType, bool retrying);
    }
}