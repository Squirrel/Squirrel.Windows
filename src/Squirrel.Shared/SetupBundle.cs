using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using Microsoft.NET.HostModel;
using Microsoft.NET.HostModel.AppHost;

namespace Squirrel.Shared
{
    public static class SetupBundle
    {
        public static bool IsBundle(string setupPath, out long bundleOffset, out long bundleLength)
        {
            byte[] bundleSignature = {
                // 64 bytes represent the bundle signature: SHA-256 for "squirrel bundle"
                0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29,
                0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4,
                0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5,
                0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d
            };

            long offset = 0;
            long length = 0;
            void FindBundleHeader()
            {
                using (var memoryMappedFile = MemoryMappedFile.CreateFromFile(setupPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
                using (MemoryMappedViewAccessor accessor = memoryMappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read)) {
                    int position = BinaryUtils.SearchInFile(accessor, bundleSignature);
                    if (position == -1) {
                        throw new PlaceHolderNotFoundInAppHostException(bundleSignature);
                    }

                    offset = accessor.ReadInt64(position - 16);
                    length = accessor.ReadInt64(position - 8);
                }
            }

            Utility.Retry(FindBundleHeader);

            bundleOffset = offset;
            bundleLength = length;

            return bundleOffset != 0 && bundleLength != 0;
        }

        public static void CreatePackageBundle(string setupPath, string packagePath)
        {
            long bundleOffset, bundleLength;
            using (var pkgStream = File.OpenRead(packagePath))
            using (var setupStream = File.Open(setupPath, FileMode.Append, FileAccess.Write)) {
                bundleOffset = setupStream.Position;
                bundleLength = pkgStream.Length;
                pkgStream.CopyTo(setupStream);
            }

            byte[] placeholder = {
                // 8 bytes represent the package offset 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                // 8 bytes represent the package length 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                // 64 bytes represent the bundle signature: SHA-256 for "squirrel bundle"
                0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29,
                0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4,
                0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5,
                0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d
            };

            var data = new byte[16];
            Array.Copy(BitConverter.GetBytes(bundleOffset), data, 8);
            Array.Copy(BitConverter.GetBytes(bundleLength), 0, data, 8, 8);

            // replace the beginning of the placeholder with the bytes from 'data'
            RetryUtil.RetryOnIOError(() =>
                BinaryUtils.SearchAndReplace(setupPath, placeholder, data, pad0s: false));

            // memory-mapped write does not updating last write time
            RetryUtil.RetryOnIOError(() =>
                File.SetLastWriteTimeUtc(setupPath, DateTime.UtcNow));

            if (!IsBundle(setupPath, out var offset, out var length))
                throw new InvalidOperationException("Internal logic error writing setup bundle.");
        }
    }
}
