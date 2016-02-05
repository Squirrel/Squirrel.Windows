using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel
{
    static class NativeMethods
    {
        [DllImport("version.dll", SetLastError = true)]
        [return:MarshalAs(UnmanagedType.Bool)] public static extern bool GetFileVersionInfo(
            string lpszFileName, 
            IntPtr dwHandleIgnored,
            int dwLen, 
            [MarshalAs(UnmanagedType.LPArray)] byte[] lpData);

        [DllImport("version.dll", SetLastError = true)]
        public static extern int GetFileVersionInfoSize(
            string lpszFileName,
            IntPtr dwHandleIgnored);

        [DllImport("version.dll")]
        [return:MarshalAs(UnmanagedType.Bool)] public static extern bool VerQueryValue(
            byte[] pBlock, 
            string pSubBlock, 
            out IntPtr pValue, 
            out int len);

        [DllImport("psapi.dll", SetLastError=true)]
        public static extern bool EnumProcesses(
            IntPtr pProcessIds, // pointer to allocated DWORD array
            int cb,
            out int pBytesReturned);

        [DllImport("psapi.dll", SetLastError=true)]
        public static extern int GetProcessImageFileName(
            IntPtr hProcess, 
            [Out] StringBuilder lpImageFileName, 
            [In] [MarshalAs(UnmanagedType.U4)] int nSize);

        [DllImport("kernel32.dll", SetLastError=true)]
        public static extern IntPtr OpenProcess(
            ProcessAccessFlags processAccess,
            bool bInheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hHandle);

        [Flags]
        public enum ProcessAccessFlags : uint {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VirtualMemoryOperation = 0x00000008,
            VirtualMemoryRead = 0x00000010,
            VirtualMemoryWrite = 0x00000020,
            DuplicateHandle = 0x00000040,
            CreateProcess = 0x000000080,
            SetQuota = 0x00000100,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400,
            QueryLimitedInformation = 0x00001000,
            Synchronize = 0x00100000
        }
    }
}
