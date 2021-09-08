using System;
using System.Runtime.InteropServices;

namespace DeltaCompressionDotNet.MsDelta
{
    internal static class NativeMethods
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
        public static extern bool ApplyDelta(
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
        public static extern bool CreateDelta(
            [MarshalAs(UnmanagedType.I8)] FileTypeSet fileTypeSet,
            [MarshalAs(UnmanagedType.I8)] CreateFlags setFlags,
            [MarshalAs(UnmanagedType.I8)] CreateFlags resetFlags,
            string sourceName,
            string targetName,
            string sourceOptionsName,
            string targetOptionsName,
            DeltaInput globalOptions,
            IntPtr targetFileTime,
            [MarshalAs(UnmanagedType.U4)] HashAlgId hashAlgId,
            string deltaName);
    }
}