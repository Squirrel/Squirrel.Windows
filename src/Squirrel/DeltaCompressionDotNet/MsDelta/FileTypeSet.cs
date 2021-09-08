using System;

namespace DeltaCompressionDotNet.MsDelta
{
    /// <remarks>
    ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#filetypesets
    /// </remarks>
    [Flags]
    internal enum FileTypeSet : long
    {
        /// <summary>
        ///     File type set that includes I386, IA64 and AMD64 Portable Executable (PE) files. Others are treated as raw.
        /// </summary>
        Executables = 0x0FL
    }
}