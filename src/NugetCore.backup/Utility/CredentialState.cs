namespace NuGet
{
    /// <summary>
    /// Represents the return state of the credentials that would come back from the ICredentialProvider
    /// </summary>
    public enum CredentialState
    {
        HasCredentials,
        Abort
    }
}
