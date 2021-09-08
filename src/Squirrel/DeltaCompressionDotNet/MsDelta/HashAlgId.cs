namespace DeltaCompressionDotNet.MsDelta
{
    internal enum HashAlgId
    {
        /// <summary>No signature.</summary>
        None = 0,

        /// <summary>32-bit CRC defined in msdelta.dll.</summary>
        Crc32 = 32
    }
}