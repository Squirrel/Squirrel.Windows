using System;
using System.Runtime.InteropServices;

namespace DeltaCompressionDotNet.PatchApi
{
    internal static class NativeMethods
    {
        /// <summary>
        ///     The ApplyPatchToFile function applies the specified delta to the specified source file. The output file is saved
        ///     under the designated new file name.
        /// </summary>
        /// <param name="patchFileName">The name of the delta to be applied to the source file.</param>
        /// <param name="oldFileName">The name of the source file to which the delta is to be applied.</param>
        /// <param name="newFileName">The name of the target file that is to be created.</param>
        /// <param name="applyOptionFlags">ApplyPatch Flags.</param>
        /// <returns>Returns TRUE on success or FALSE otherwise.</returns>
        /// <remarks>http://msdn.microsoft.com/en-us/library/bb417345.aspx#applypatchtofileaw</remarks>
        [DllImport("mspatcha.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ApplyPatchToFile(
            string patchFileName, string oldFileName, string newFileName, uint applyOptionFlags);

        /// <summary>
        ///     The CreatePatchFile function creates a delta from the specified source and target files and write the delta to the
        ///     designated file name.
        /// </summary>
        /// <param name="oldFileName">The name of the source file.</param>
        /// <param name="newFileName">The name of the target file.</param>
        /// <param name="patchFileName">The name of the output delta file.</param>
        /// <param name="optionFlags">Creation Flags.</param>
        /// <param name="optionData">Not used. Pass NULL. Pointer to a structure of type PATCH_OPTION_DATA.</param>
        /// <returns>Returns TRUE on success or FALSE otherwise.</returns>
        /// <remarks>http://msdn.microsoft.com/en-us/library/bb417345.aspx#createpatchfileaw</remarks>
        [DllImport("mspatchc.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePatchFile(
            string oldFileName, string newFileName, string patchFileName, uint optionFlags, IntPtr optionData);
    }
}