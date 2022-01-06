using System;
using System.Collections.Generic;

namespace Squirrel.NuGet
{
    internal interface IPackageFile : IFrameworkTargetable
    {
        string Path { get; }
        string EffectivePath { get; }
        string TargetFramework { get; }
    }

    internal class ZipPackageFile : IPackageFile, IEquatable<ZipPackageFile>
    {
        private readonly string _targetFramework;

        public ZipPackageFile(string path)
        {
            Path = path;
            string effectivePath;
            _targetFramework = NugetUtil.ParseFrameworkNameFromFilePath(path, out effectivePath);
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

        public override string ToString()
        {
            return Path;
        }

        public override int GetHashCode()
        {
            unchecked {
                int hash = 17;
                hash = hash * 23 + Path.GetHashCode();
                hash = hash * 23 + EffectivePath.GetHashCode();
                hash = hash * 23 + TargetFramework.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is ZipPackageFile zpf)
                return Equals(zpf);
            return false;
        }

        public bool Equals(ZipPackageFile other)
        {
            if (other == null) return false;
            return
                Path.Equals(other.Path) &&
                EffectivePath.Equals(other.EffectivePath) &&
                TargetFramework.Equals(other.TargetFramework);
        }
    }
}