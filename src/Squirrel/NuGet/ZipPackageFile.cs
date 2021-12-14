using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Runtime.Versioning;

namespace Squirrel.NuGet
{
    internal interface IPackageFile : IFrameworkTargetable
    {
        string Path { get; }
        string EffectivePath { get; }
        string TargetFramework { get; }
        Stream GetStream();
    }

    internal class ZipPackageFile : IPackageFile
    {
        private readonly Func<Stream> _streamFactory;
        private readonly string _targetFramework;

        public ZipPackageFile(PackagePart part)
            : this(UriUtility.GetPath(part.Uri), part.GetStream().ToStreamFactory())
        {
        }

        public ZipPackageFile(IPackageFile file)
            : this(file.Path, file.GetStream().ToStreamFactory())
        {
        }

        protected ZipPackageFile(string path, Func<Stream> streamFactory)
        {
            Path = path;
            _streamFactory = streamFactory;

            string effectivePath;
            _targetFramework = VersionUtility.ParseFrameworkNameFromFilePath(path, out effectivePath);
            EffectivePath = effectivePath;
        }

        public string Path {
            get;
            private set;
        }

        public string EffectivePath {
            get;
            private set;
        }

        public string TargetFramework {
            get {
                return _targetFramework;
            }
        }

        IEnumerable<string> IFrameworkTargetable.SupportedFrameworks {
            get {
                if (TargetFramework != null) {
                    yield return TargetFramework;
                }
                yield break;
            }
        }

        public Stream GetStream()
        {
            return _streamFactory();
        }

        public override string ToString()
        {
            return Path;
        }
    }
}