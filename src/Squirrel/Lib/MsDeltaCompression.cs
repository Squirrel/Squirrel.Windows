#nullable enable

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Squirrel
{
    [SupportedOSPlatform("windows")]
    internal class MsDeltaCompression
    {
        /// <summary>
        ///     The ApplyDelta function use the specified delta and source files to create a new copy of the target file.
        /// </summary>
        /// <param name="applyFlags">Either DELTA_FLAG_NONE or DELTA_APPLY_FLAG_ALLOW_PA19.</param>
        /// <param name="sourceName">The name of the source file to which the delta is to be applied.</param>
        /// <param name="deltaName">The name of the delta to be applied to the source file.</param>
        /// <param name="targetName">The name of the target file that is to be created.</param>
        /// <returns>
        ///     Returns TRUE on success or FALSE otherwise.
        /// </returns>
        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#applydeltaaw
        /// </remarks>
        [DllImport("msdelta.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ApplyDelta(
            [MarshalAs(UnmanagedType.I8)] ApplyFlags applyFlags,
            string sourceName,
            string deltaName,
            string targetName);

        /// <summary>
        ///     The CreateDelta function creates a delta from the specified source and target files and write the output delta to the designated file name.
        /// </summary>
        /// <param name="fileTypeSet">The file type set used for Create.</param>
        /// <param name="setFlags">The file type set used for Create.</param>
        /// <param name="resetFlags">The file type set used for Create.</param>
        /// <param name="sourceName">The file type set used for Create.</param>
        /// <param name="targetName">The name of the target against which the source is compared.</param>
        /// <param name="sourceOptionsName">Reserved. Pass NULL.</param>
        /// <param name="targetOptionsName">Reserved. Pass NULL.</param>
        /// <param name="globalOptions">Reserved. Pass a DELTA_INPUT structure with lpStart set to NULL and uSize set to 0.</param>
        /// <param name="targetFileTime">The time stamp set on the target file after delta Apply. If NULL, the timestamp of the target file during delta Create will be used.</param>
        /// <param name="hashAlgId">ALG_ID of the algorithm to be used to generate the target signature.</param>
        /// <param name="deltaName">The name of the delta file to be created.</param>
        /// <returns>
        ///     Returns TRUE on success or FALSE otherwise.
        /// </returns>
        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#createdeltaaw
        /// </remarks>
        [DllImport("msdelta.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDelta(
            [MarshalAs(UnmanagedType.I8)] FileTypeSet fileTypeSet,
            [MarshalAs(UnmanagedType.I8)] CreateFlags setFlags,
            [MarshalAs(UnmanagedType.I8)] CreateFlags resetFlags,
            string sourceName,
            string targetName,
            string? sourceOptionsName,
            string? targetOptionsName,
            DeltaInput globalOptions,
            IntPtr targetFileTime,
            [MarshalAs(UnmanagedType.U4)] HashAlgId hashAlgId,
            string deltaName);
        
        private enum HashAlgId
        {
            /// <summary>No signature.</summary>
            None = 0,

            /// <summary>32-bit CRC defined in msdelta.dll.</summary>
            Crc32 = 32,
        }
        
        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#deltaflagtypeflags
        /// </remarks>
        private enum ApplyFlags : long
        {
            /// <summary>Indicates no special handling.</summary>
            None = 0,

            /// <summary>Allow MSDelta to apply deltas created using PatchAPI.</summary>
            AllowLegacy = 1,
        }

        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#filetypesets
        /// </remarks>
        [Flags]
        private enum FileTypeSet : long
        {
            /// <summary>
            ///     File type set that includes I386, IA64 and AMD64 Portable Executable (PE) files. Others are treated as raw.
            /// </summary>
            Executables = 0x0FL,
        }

        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#deltaflagtypeflags
        /// </remarks>
        private enum CreateFlags : long
        {
            /// <summary>Indicates no special handling.</summary>
            None = 0,

            /// <summary>Allow the source, target and delta files to exceed the default size limit.</summary>
            IgnoreFileSizeLimit = 1 << 17,
        }

        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#deltainputstructure
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct DeltaInput
        {
            /// <summary>Memory address non-editable input buffer.</summary>
            public IntPtr Start;

            /// <summary>Size of the memory buffer in bytes.</summary>
            public IntPtr Size;

            /// <summary>
            ///     Defines whether MSDelta is allowed to edit the input buffer. If you make the input editable, the buffer will
            ///     be zeroed at function return. However this will cause most MSDelta functions to use less memory.
            /// </summary>
            [MarshalAs(UnmanagedType.Bool)] public bool Editable;
        }
        
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
            if (!ApplyDelta(ApplyFlags.AllowLegacy, oldFilePath, deltaFilePath, newFilePath))
                throw new Win32Exception();
        }
    }
}
