namespace DeltaCompressionDotNet.MsDelta
{
    /// <remarks>
    ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#deltaflagtypeflags
    /// </remarks>
    internal enum ApplyFlags : long
    {
        /// <summary>Indicates no special handling.</summary>
        None = 0,

        /// <summary>Allow MSDelta to apply deltas created using PatchAPI.</summary>
        AllowLegacy = 1
    }
}