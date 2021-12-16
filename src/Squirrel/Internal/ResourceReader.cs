using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using static Squirrel.NativeMethods;

namespace Squirrel.Lib
{
    internal class ResourceReader : IDisposable
    {
        private IntPtr hModule;
        const uint LOAD_LIBRARY_AS_DATAFILE = 2;
        private bool _disposed;

        public ResourceReader(string peFile)
        {
            var hModule = LoadLibraryEx(peFile, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (hModule == IntPtr.Zero) {
                throw new Win32Exception();
            }
        }

        ~ResourceReader()
        {
            Dispose();
        }

        public byte[] ReadResource(string resourceName, string resourceType)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceReader));

            var hResource = FindResource(hModule, resourceName, resourceType);
            if (hResource == IntPtr.Zero)
                return null;

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

        public byte[] ReadResource(IntPtr resourceName, string resourceType)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceReader));

            var hResource = FindResource(hModule, resourceName, resourceType);
            if (hResource == IntPtr.Zero)
                return null;

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
            return ReadResource("#1", "#24");
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
