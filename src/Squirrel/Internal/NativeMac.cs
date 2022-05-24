using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Squirrel
{
    [SupportedOSPlatform("osx")]
    internal static class NativeMac
    {
        private const string SystemLib = "libSystem.dylib";
        
        [DllImport(SystemLib)]
        public static extern int getppid();
        
        [DllImport(SystemLib, SetLastError = true)]
        private static extern int chmod(string pathname, int mode);
        
        public static void ChmodAsExe(string filePath)
        {
            var filePermissionOctal = Convert.ToInt32("777", 8);
            const int EINTR = 4;
            int chmodReturnCode = 0;

            do
            {
                chmodReturnCode = chmod(filePath, filePermissionOctal);
            }
            while (chmodReturnCode == -1 && Marshal.GetLastWin32Error() == EINTR);

            if (chmodReturnCode == -1)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not set file permission {filePermissionOctal} for {filePath}.");
            }
        }
    }
}