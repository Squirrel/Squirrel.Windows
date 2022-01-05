using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel
{
    // https://stackoverflow.com/a/43229358/184746
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    internal class StringFileInfo
    {
        [DllImport("version.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetFileVersionInfoSize(string lptstrFilename, out int lpdwHandle);

        [DllImport("version.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GetFileVersionInfo(string lptstrFilename, int dwHandle, int dwLen, byte[] lpData);

        [DllImport("version.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool VerQueryValue(byte[] pBlock, string lpSubBlock, out IntPtr lplpBuffer, out int puLen);

        public readonly Version FileVersion;
        public readonly Version ProductVersion;
        public readonly uint FileFlagsMask;
        public readonly uint FileFlags;
        public readonly uint FileOS;
        public readonly uint FileType;
        public readonly uint FileSubtype;
        // Always null
        public readonly DateTime? FileDate;

        protected StringFileInfo(Version fileVersion, Version productVersion, uint fileFlagsMask, uint fileFlags, uint fileOS, uint fileType, uint fileSubtype, DateTime? fileDate)
        {
            FileVersion = fileVersion;
            ProductVersion = productVersion;
            FileFlagsMask = fileFlagsMask;
            FileFlags = fileFlags;
            FileOS = fileOS;
            FileType = fileType;
            FileSubtype = fileSubtype;
            FileDate = fileDate;
        }

        // vi can be null on exit
        // Item1 = language | codepage
        // Item2 = Key
        // Item3 = Value
        public static IEnumerable<(uint CodePage, string Key, string Value)> ReadVersionInfo(string fileName, out StringFileInfo vi)
        {
            int num;
            int size = GetFileVersionInfoSize(fileName, out num);

            if (size == 0) {
                throw new Win32Exception();
            }

            var buffer = new byte[size];
            bool success = GetFileVersionInfo(fileName, 0, size, buffer);

            if (!success) {
                throw new Win32Exception();
            }

            return ReadVersionInfo(buffer, out vi);

        }

        // vi can be null on exit
        // Item1 = language | codepage
        // Item2 = Key
        // Item3 = Value
        public static IEnumerable<(uint CodePage, string Key, string Value)> ReadVersionInfo(byte[] buffer, out StringFileInfo vi)
        {
            int offset;
            // The offset calculated here is unused
            var fibs = ReadFileInfoBaseStruct(buffer, 0, out offset);

            if (fibs.Key != "VS_VERSION_INFO") {
                throw new Exception(fibs.Key);
            }

            // Value = VS_FIXEDFILEINFO
            if (fibs.ValueLength != 0) {
                uint signature = BitConverter.ToUInt32(buffer, fibs.ValueOffset);

                if (signature != 0xFEEF04BD) {
                    throw new Exception(signature.ToString("X8"));
                }

                uint strucVersion = BitConverter.ToUInt32(buffer, fibs.ValueOffset + 4);

                var fileVersion = new Version(BitConverter.ToUInt16(buffer, fibs.ValueOffset + 10), BitConverter.ToUInt16(buffer, fibs.ValueOffset + 8), BitConverter.ToUInt16(buffer, fibs.ValueOffset + 14), BitConverter.ToUInt16(buffer, fibs.ValueOffset + 12));
                var productVersion = new Version(BitConverter.ToUInt16(buffer, fibs.ValueOffset + 18), BitConverter.ToUInt16(buffer, fibs.ValueOffset + 16), BitConverter.ToUInt16(buffer, fibs.ValueOffset + 22), BitConverter.ToUInt16(buffer, fibs.ValueOffset + 20));

                uint fileFlagsMask = BitConverter.ToUInt32(buffer, fibs.ValueOffset + 24);
                uint fileFlags = BitConverter.ToUInt32(buffer, fibs.ValueOffset + 28);
                uint fileOS = BitConverter.ToUInt32(buffer, fibs.ValueOffset + 32);
                uint fileType = BitConverter.ToUInt32(buffer, fibs.ValueOffset + 36);
                uint fileSubtype = BitConverter.ToUInt32(buffer, fibs.ValueOffset + 40);

                uint fileDateMS = BitConverter.ToUInt32(buffer, fibs.ValueOffset + 44);
                uint fileDateLS = BitConverter.ToUInt32(buffer, fibs.ValueOffset + 48);
                DateTime? fileDate = fileDateMS != 0 || fileDateLS != 0 ?
                    (DateTime?) DateTime.FromFileTime((long) fileDateMS << 32 | fileDateLS) :
                    null;

                vi = new StringFileInfo(fileVersion, productVersion, fileFlagsMask, fileFlags, fileOS, fileType, fileSubtype, fileDate);
            } else {
                vi = null;
            }

            return ReadVersionInfoInternal(buffer, fibs);
        }

        protected static IEnumerable<(uint CodePage, string Key, string Value)> ReadVersionInfoInternal(byte[] buffer, FileInfoBaseStruct fibs)
        {
            int sfiOrValOffset = (fibs.ValueOffset + fibs.ValueLength + 3) & (~3);

            while (sfiOrValOffset < fibs.Length) {
                int nextSfiOrValOffset;

                var sfiOrVal = ReadFileInfoBaseStruct(buffer, sfiOrValOffset, out nextSfiOrValOffset);

                if (sfiOrVal.Key == "StringFileInfo") {
                    int stOffset = sfiOrVal.ValueOffset;

                    while (stOffset < sfiOrVal.EndOffset) {
                        int nextStOffset;

                        var st = ReadFileInfoBaseStruct(buffer, stOffset, out nextStOffset);

                        uint langCharset = uint.Parse(st.Key, NumberStyles.HexNumber);

                        int striOffset = st.ValueOffset;

                        while (striOffset < st.EndOffset) {
                            int nextStriOffset;

                            var stri = ReadFileInfoBaseStruct(buffer, striOffset, out nextStriOffset);

                            // Here stri.ValueLength is in words!
                            int len = FindLengthUnicodeSZ(buffer, stri.ValueOffset, stri.ValueOffset + (stri.ValueLength * 2));
                            string value = Encoding.Unicode.GetString(buffer, stri.ValueOffset, len * 2);

                            yield return (langCharset, stri.Key, value);

                            striOffset = nextStriOffset;
                        }

                        stOffset = nextStOffset;
                    }
                } else if (sfiOrVal.Key == "VarFileInfo") {
                    int varOffset = sfiOrVal.ValueOffset;

                    while (varOffset < sfiOrVal.EndOffset) {
                        int nextVarOffset;

                        var var = ReadFileInfoBaseStruct(buffer, varOffset, out nextVarOffset);

                        if (var.Key != "Translation") {
                            throw new Exception(var.Key);
                        }

                        int langOffset = var.ValueOffset;

                        while (langOffset < var.EndOffset) {
                            unchecked {
                                // We invert the order suggested by the Var description!
                                uint high = (uint) BitConverter.ToInt16(buffer, langOffset);
                                uint low = (uint) BitConverter.ToInt16(buffer, langOffset + 2);
                                uint lang = (high << 16) | low;

                                langOffset += 4;
                            }
                        }

                        varOffset = nextVarOffset;
                    }
                } else {
                    Debug.WriteLine("Unrecognized " + sfiOrVal.Key);
                }

                sfiOrValOffset = nextSfiOrValOffset;
            }
        }

        protected static FileInfoBaseStruct ReadFileInfoBaseStruct(byte[] buffer, int offset, out int nextOffset)
        {
            var fibs = new FileInfoBaseStruct {
                Length = BitConverter.ToInt16(buffer, offset),
                ValueLength = BitConverter.ToInt16(buffer, offset + 2),
                Type = BitConverter.ToInt16(buffer, offset + 4)
            };

            int len = FindLengthUnicodeSZ(buffer, offset + 6, offset + fibs.Length);
            fibs.Key = Encoding.Unicode.GetString(buffer, offset + 6, len * 2);

            // Padding
            fibs.ValueOffset = ((offset + 6 + (len + 1) * 2) + 3) & (~3);

            fibs.EndOffset = offset + fibs.Length;
            nextOffset = (fibs.EndOffset + 3) & (~3);

            return fibs;
        }

        protected static int FindLengthUnicodeSZ(byte[] buffer, int offset, int endOffset)
        {
            int offset2 = offset;
            while (offset2 < endOffset && BitConverter.ToInt16(buffer, offset2) != 0) {
                offset2 += 2;
            }

            // In chars
            return (offset2 - offset) / 2;
        }

        // Used internally
        protected class FileInfoBaseStruct
        {
            public short Length { get; set; }
            public short ValueLength { get; set; }
            public short Type { get; set; }
            public string Key { get; set; }
            public int ValueOffset { get; set; }
            public int EndOffset { get; set; }
        }
    }
}
