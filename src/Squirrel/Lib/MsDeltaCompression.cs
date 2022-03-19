#nullable enable

using System;
using System.ComponentModel;

namespace Squirrel
{
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    internal class MsDeltaCompression
    {
        //public void CreateDelta(string oldFilePath, string newFilePath, string deltaFilePath)
        //{
        //    const string? sourceOptionsName = null;
        //    const string? targetOptionsName = null;
        //    var globalOptions = new DeltaInput();
        //    var targetFileTime = IntPtr.Zero;

        //    if (!NativeMethods.CreateDelta(
        //        FileTypeSet.Executables, CreateFlags.IgnoreFileSizeLimit, CreateFlags.None, oldFilePath, newFilePath,
        //        sourceOptionsName, targetOptionsName, globalOptions, targetFileTime, HashAlgId.Crc32, deltaFilePath))
        //    {
        //        throw new Win32Exception();
        //    }
        //}

        public static void ApplyDelta(string deltaFilePath, string oldFilePath, string newFilePath)
        {
            if (!NativeMethods.ApplyDelta(ApplyFlags.AllowLegacy, oldFilePath, deltaFilePath, newFilePath))
                throw new Win32Exception();
        }
    }
}
