using System;
using System.Linq;
using System.Net;
using NuGet.Resources;

namespace NuGet
{
    public class SettingsCredentialProvider : ICredentialProvider
    {
        private readonly ICredentialProvider _credentialProvider;
        private readonly IPackageSourceProvider _packageSourceProvider;
        private readonly ILogger _logger;

        public SettingsCredentialProvider(ICredentialProvider credentialProvider, IPackageSourceProvider packageSourceProvider)
            : this(credentialProvider, packageSourceProvider, NullLogger.Instance)
        {
        }
        
        public SettingsCredentialProvider(ICredentialProvider credentialProvider, IPackageSourceProvider packageSourceProvider, ILogger logger)
        {
            if (credentialProvider == null)
            {
                throw new ArgumentNullException("credentialProvider");
            }

            if (packageSourceProvider == null)
            {
                throw new ArgumentNullException("packageSourceProvider");
            }
            
            _credentialProvider = credentialProvider;
            _packageSourceProvider = packageSourceProvider;
            _logger = logger;
        }

        public ICredentials GetCredentials(Uri uri, IWebProxy proxy, CredentialType credentialType, bool retrying)
        {
            NetworkCredential credentials;
            // If we are retrying, the stored credentials must be invalid. 
            if (!retrying && (credentialType == CredentialType.RequestCredentials) && TryGetCredentials(uri, out credentials))
            {
                _logger.Log(MessageLevel.Info, NuGetResources.SettingsCredentials_UsingSavedCredentials, credentials.UserName);
                return credentials;
            }
            return _credentialProvider.GetCredentials(uri, proxy, credentialType, retrying);
        }

        private bool TryGetCredentials(Uri uri, out NetworkCredential configurationCredentials)
        {
            var source = _packageSourceProvider.LoadPackageSources().FirstOrDefault(p =>
            {
                Uri sourceUri;
                return !String.IsNullOrEmpty(p.UserName) 
                    && !String.IsNullOrEmpty(p.Password) 
                    && Uri.TryCreate(p.Source, UriKind.Absolute, out sourceUri) 
                    && UriUtility.UriStartsWith(sourceUri, uri);
            });
            if (source == null)
            {
                // The source is not in the config file
                configurationCredentials = null;
                return false;
            }
            configurationCredentials = new NetworkCredential(source.UserName, source.Password);
            return true;
        }
    }
}
