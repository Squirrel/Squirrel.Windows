using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives.Zip;

namespace Squirrel.NuGet
{
    internal interface IZipPackage : IPackage
    {
        IEnumerable<string> Frameworks { get; }
        IEnumerable<ZipPackageFile> Files { get; }
    }

    internal class ZipPackage : NuspecManifest, IZipPackage
    {
        public IEnumerable<string> Frameworks { get; private set; } = Enumerable.Empty<string>();
        public IEnumerable<ZipPackageFile> Files { get; private set; } = Enumerable.Empty<ZipPackageFile>();

        public byte[] SetupSplashBytes { get; private set; }
        public byte[] SetupIconBytes { get; private set; }
        public byte[] AppIconBytes { get; private set; }

        public ZipPackage(string filePath) : this(File.OpenRead(filePath))
        { }

        public ZipPackage(Stream zipStream, bool leaveOpen = false)
        {
            using var zip = ZipArchive.Open(zipStream, new() { LeaveStreamOpen = leaveOpen });
            using var manifest = GetManifestEntry(zip).OpenEntryStream();
            ReadManifest(manifest);
            Files = GetPackageFiles(zip).ToArray();
            Frameworks = GetFrameworks(Files);

            // we pre-load some images so the zip doesn't need to be opened again later
            SetupSplashBytes = ReadFileToBytes(zip, z => Path.GetFileNameWithoutExtension(z.Key) == "splashimage");
            SetupIconBytes = ReadFileToBytes(zip, z => z.Key == "setup.ico");
            AppIconBytes = ReadFileToBytes(zip, z => z.Key == "app.ico") ?? ReadFileToBytes(zip, z => z.Key.EndsWith("app.ico"));
        }

        private byte[] ReadFileToBytes(ZipArchive archive, Func<ZipArchiveEntry, bool> predicate)
        {
            var f = archive.Entries.FirstOrDefault(predicate);
            if (f == null)
                return null;

            using var stream = f.OpenEntryStream();
            if (stream == null)
                return null;

            var ms = new MemoryStream();
            stream.CopyTo(ms);

            return ms.ToArray();
        }

        private ZipArchiveEntry GetManifestEntry(ZipArchive zip)
        {
            var manifest = zip.Entries
              .FirstOrDefault(f => f.Key.EndsWith(NugetUtil.ManifestExtension, StringComparison.OrdinalIgnoreCase));

            if (manifest == null)
                throw new InvalidDataException("Invalid nupkg. Does not contain required '.nuspec' manifest.");

            return manifest;
        }

        private IEnumerable<ZipPackageFile> GetPackageFiles(ZipArchive zip)
        {
            return from entry in zip.Entries
                   where !entry.IsDirectory
                   let uri = new Uri(entry.Key, UriKind.Relative)
                   let path = NugetUtil.GetPath(uri)
                   where IsPackageFile(path)
                   select new ZipPackageFile(uri);
        }

        private string[] GetFrameworks(IEnumerable<ZipPackageFile> files)
        {
            return FrameworkAssemblies
                .SelectMany(f => f.SupportedFrameworks)
                .Concat(files.Select(z => z.TargetFramework))
                .Where(f => f != null)
                .Distinct()
                .ToArray();
        }
    }
}
