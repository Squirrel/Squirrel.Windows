namespace DeltaCompressionDotNet.MsDelta
{
    /// <remarks>
    ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#deltaflagtypeflags
    /// </remarks>
    internal enum CreateFlags : long
    {
        /// <summary>Indicates no special handling.</summary>
        None = 0,

        /// <summary>Allow the source, target and delta files to exceed the default size limit.</summary>
        IgnoreFileSizeLimit = 1 << 17
    };
}