using System;
using System.ComponentModel;

namespace DeltaCompressionDotNet.PatchApi
{
    public sealed class PatchApiCompression : IDeltaCompression
    {
        public void CreateDelta(string oldFilePath, string newFilePath, string deltaFilePath)
        {
            const int optionFlags = 0;
            var optionData = IntPtr.Zero;

            if (!NativeMethods.CreatePatchFile(oldFilePath, newFilePath, deltaFilePath, optionFlags, optionData))
                throw new Win32Exception();
        }

        public void ApplyDelta(string deltaFilePath, string oldFilePath, string newFilePath)
        {
            const int applyOptionFlags = 0;

            if (!NativeMethods.ApplyPatchToFile(deltaFilePath, oldFilePath, newFilePath, applyOptionFlags))
                throw new Win32Exception();
        }
    }
}