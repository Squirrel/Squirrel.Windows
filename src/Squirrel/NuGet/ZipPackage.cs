using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using NuGet;

namespace NuGet
{
    internal interface IFrameworkTargetable
    {
        IEnumerable<FrameworkName> SupportedFrameworks { get; }
    }

    internal interface IPackageFile : IFrameworkTargetable
    {
        string Path { get; }
        string EffectivePath { get; }
        FrameworkName TargetFramework { get; }
        Stream GetStream();
    }

    internal interface IPackage
    {
        string Id { get; }
        string Description { get; }
        IEnumerable<string> Authors { get; }
        string Title { get; }
        string Summary { get; }
        string Language { get; }
        string Copyright { get; }
        Uri ProjectUrl { get; }
        string ReleaseNotes { get; }
        Uri IconUrl { get; }
        IEnumerable<PackageDependencySet> DependencySets { get; }
        SemanticVersion Version { get; }
        IEnumerable<FrameworkName> GetSupportedFrameworks();
        IEnumerable<IPackageFile> GetLibFiles();
        string GetFullName();
    }

    internal class ZipPackage : IPackage
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public IEnumerable<string> Authors { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public string Language { get; set; }
        public string Copyright { get; set; }
        public SemanticVersion Version { get; set; }
        public IEnumerable<PackageDependencySet> DependencySets { get; }

        public Uri ProjectUrl => throw new NotImplementedException();

        public string ReleaseNotes => throw new NotImplementedException();

        public Uri IconUrl => throw new NotImplementedException();

        public ZipPackage(string filePath)
        {
        }

        public IEnumerable<FrameworkName> GetSupportedFrameworks()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IPackageFile> GetLibFiles()
        {
            throw new NotImplementedException();
        }

        public string GetFullName()
        {
            throw new NotImplementedException();
        }
    }
}
