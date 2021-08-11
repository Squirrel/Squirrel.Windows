using System.Net;

namespace NuGet
{
    public class CredentialResult
    {
        /// <summary>
        /// Represents the state of credentials that are being returned.
        /// </summary>
        public CredentialState State
        {
            get;
            set;
        }
        /// <summary>
        /// Credentials that the consumer was asking for.
        /// </summary>
        public ICredentials Credentials
        {
            get;
            set;
        }
        /// <summary>
        /// Creates a new instance of the CredentialResult object with populated properties.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="credentials"></param>
        /// <returns></returns>
        public static CredentialResult Create(CredentialState state, ICredentials credentials)
        {
            return new CredentialResult
            {
                State = state,
                Credentials = credentials
            };
        }
    }
}
