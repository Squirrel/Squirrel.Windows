using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives.Zip;

namespace Squirrel.NuGet
{
    internal interface IPackageFile : IFrameworkTargetable
    {
        string Path { get; }
        string EffectivePath { get; }
        string TargetFramework { get; }
        bool IsLibFile();
        bool IsContentFile();
        Stream GetEntryStream(Stream archiveStream);
    }

    internal class ZipPackageFile : IPackageFile, IEquatable<ZipPackageFile>
    {
        public string EffectivePath { get; }
        public string TargetFramework { get; }
        public string Path { get; }

        IEnumerable<string> IFrameworkTargetable.SupportedFrameworks {
            get {
                if (TargetFramework != null) {
                    yield return TargetFramework;
                }
                yield break;
            }
        }

        private readonly Uri _entryKey;

        public ZipPackageFile(Uri relpath)
        {
            _entryKey = relpath;
            Path = NugetUtil.GetPath(relpath);
            TargetFramework = NugetUtil.ParseFrameworkNameFromFilePath(Path, out var effectivePath);
            EffectivePath = effectivePath;
        }

        public Stream GetEntryStream(Stream archiveStream)
        {
            using var zip = ZipArchive.Open(archiveStream, new() { LeaveStreamOpen = true });
            var entry = zip.Entries.FirstOrDefault(f => new Uri(f.Key, UriKind.Relative) == _entryKey);
            return entry?.OpenEntryStream();
        }

        public bool IsLibFile() => IsFileInTopDirectory(NugetUtil.LibDirectory);
        public bool IsContentFile() => IsFileInTopDirectory(NugetUtil.ContentDirectory);

        public bool IsFileInTopDirectory(string directory)
        {
            string folderPrefix = directory + System.IO.Path.DirectorySeparatorChar;
            return Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() => Path;

        public override int GetHashCode() => Path.GetHashCode();

        public override bool Equals(object obj)
        {
            if (obj is ZipPackageFile zpf)
                return Equals(zpf);
            return false;
        }

        public bool Equals(ZipPackageFile other)
        {
            if (other == null) return false;
            return Path.Equals(other.Path);
        }
    }
}