using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Squirrel.Lib
{
    [SupportedOSPlatform("windows")]
    internal class ResourceReader : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpModuleName, IntPtr hFile, uint dwFlags);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr hModule, string lpName, string lpType);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, string lpType);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr FindResourceEx(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort wLanguage);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr FindResourceEx(IntPtr hModule, string lpType, IntPtr lpName, ushort wLanguage);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr FindResourceEx(IntPtr hModule, string lpType, string lpName, ushort wLanguage);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr hModule, IntPtr handle);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr hModule, IntPtr handle);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr hglobal);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);
        
        private IntPtr hModule;
        const uint LOAD_LIBRARY_AS_DATAFILE = 2;
        private bool _disposed;

        public ResourceReader(string peFile)
        {
            hModule = LoadLibraryEx(peFile, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (hModule == IntPtr.Zero) {
                throw new Win32Exception();
            }
        }

        ~ResourceReader()
        {
            Dispose();
        }

        public byte[] ReadResource(string resourceType, string resourceName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceReader));

            var hResource = FindResource(hModule, resourceName, resourceType);
            if (hResource == IntPtr.Zero)
                return null;

            return ReadResourceToBytes(hResource);
        }

        public byte[] ReadResource(string resourceType, string resourceName, ushort lang)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceReader));

            var hResource = FindResourceEx(hModule, resourceType, resourceName, lang);
            if (hResource == IntPtr.Zero)
                return null;

            return ReadResourceToBytes(hResource);
        }

        public byte[] ReadResource(string resourceType, IntPtr resourceName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceReader));

            var hResource = FindResource(hModule, resourceName, resourceType);
            if (hResource == IntPtr.Zero)
                return null;

            return ReadResourceToBytes(hResource);
        }

        public byte[] ReadResource(string resourceType, IntPtr resourceName, ushort lang)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceReader));

            var hResource = FindResourceEx(hModule, resourceType, resourceName, lang);
            if (hResource == IntPtr.Zero)
                return null;

            return ReadResourceToBytes(hResource);
        }

        public byte[] ReadResource(IntPtr resourceType, IntPtr resourceName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceReader));

            var hResource = FindResource(hModule, resourceName, resourceType);
            if (hResource == IntPtr.Zero)
                return null;

            return ReadResourceToBytes(hResource);
        }

        public byte[] ReadResource(IntPtr resourceType, IntPtr resourceName, ushort lang)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceReader));

            var hResource = FindResourceEx(hModule, resourceType, resourceName, lang);
            if (hResource == IntPtr.Zero)
                return null;

            return ReadResourceToBytes(hResource);
        }

        private byte[] ReadResourceToBytes(IntPtr hResource)
        {
            uint size = SizeofResource(hModule, hResource);
            if (size == 0)
                throw new Win32Exception();

            var hGlobal = LoadResource(hModule, hResource);
            if (hGlobal == IntPtr.Zero)
                throw new Win32Exception();

            var data = LockResource(hGlobal);
            if (data == IntPtr.Zero)
                throw new Win32Exception(0x21);

            var buf = new byte[size];
            Marshal.Copy(data, buf, 0, (int) size);
            return buf;
        }

        public byte[] ReadAssemblyManifest()
        {
            return ReadResource(new IntPtr(24) /*RT_MANIFEST*/, new IntPtr(1));
        }

        public void Dispose()
        {
            if (!_disposed) {
                _disposed = true;
                FreeLibrary(hModule);
            }
        }
    }
}
