using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
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
        IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; }
        IEnumerable<PackageDependencySet> DependencySets { get; }
        SemanticVersion Version { get; }
        IEnumerable<FrameworkName> GetSupportedFrameworks();
        IEnumerable<IPackageFile> GetLibFiles();
        string GetFullName();
    }

    internal class ZipPackage : IPackage
    {
        public string Id { get; private set; }
        public string Description { get; private set; }
        public IEnumerable<string> Authors { get; private set; }
        public string Title { get; private set; }
        public string Summary { get; private set; }
        public string Language { get; private set; }
        public string Copyright { get; private set; }
        public SemanticVersion Version { get; private set; }
        public IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; }
        public IEnumerable<PackageDependencySet> DependencySets { get; private set; }
        public Uri ProjectUrl { get; private set; }
        public string ReleaseNotes { get; private set; }
        public Uri IconUrl { get; private set; }

        private string _filePath;
        private readonly Func<Stream> _streamFactory;
        private static readonly string[] ExcludePaths = new[] { "_rels", "package" };
        private const string ManifestRelationType = "manifest";

        public ZipPackage(string filePath)
        {
            if (String.IsNullOrEmpty(filePath)) {
                throw new ArgumentException("Argument_Cannot_Be_Null_Or_Empty", "filePath");
            }

            _filePath = filePath;
            _streamFactory = () => File.OpenRead(filePath);
            ParseManifest();
        }

        public IEnumerable<FrameworkName> GetSupportedFrameworks()
        {
            IEnumerable<FrameworkName> fileFrameworks;

            using (Stream stream = _streamFactory()) {
                var package = Package.Open(stream);

                string effectivePath;
                fileFrameworks = from part in package.GetParts()
                                 where IsPackageFile(part)
                                 select VersionUtility.ParseFrameworkNameFromFilePath(UriUtility.GetPath(part.Uri), out effectivePath);
            }

            return FrameworkAssemblies.SelectMany(f => f.SupportedFrameworks)
                       .Concat(fileFrameworks)
                       .Where(f => f != null)
                       .Distinct();
        }

        public IEnumerable<IPackageFile> GetLibFiles()
        {
            return GetFiles(Constants.LibDirectory);
        }

        public IEnumerable<IPackageFile> GetFiles(string directory)
        {
            string folderPrefix = directory + Path.DirectorySeparatorChar;
            return GetFilesNoCache().Where(file => file.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
        }

        private List<IPackageFile> GetFilesNoCache()
        {
            using (Stream stream = _streamFactory()) {
                Package package = Package.Open(stream);

                return (from part in package.GetParts()
                        where IsPackageFile(part)
                        select (IPackageFile) new ZipPackageFile(part)).ToList();
            }
        }

        public string GetFullName()
        {
            return Id + " " + Version;
        }

        private void ParseManifest()
        {
            using (Stream stream = _streamFactory()) {
                Package package = Package.Open(stream);

                PackageRelationship relationshipType = package.GetRelationshipsByType(Constants.PackageRelationshipNamespace + ManifestRelationType).SingleOrDefault();

                if (relationshipType == null) {
                    throw new InvalidOperationException("PackageDoesNotContainManifest");
                }

                PackagePart manifestPart = package.GetPart(relationshipType.TargetUri);

                if (manifestPart == null) {
                    throw new InvalidOperationException("PackageDoesNotContainManifest");
                }

                using (Stream manifestStream = manifestPart.GetStream()) {
                    ReadManifest(manifestStream);
                }
            }
        }

        void ReadManifest(Stream manifestStream)
        {
            throw new NotImplementedException();
            //Manifest manifest = Manifest.ReadFrom(manifestStream, validateSchema: false);

            //IPackageMetadata metadata = manifest.Metadata;

            //Id = metadata.Id;
            //Version = metadata.Version;
            //Title = metadata.Title;
            //Authors = metadata.Authors;
            //Owners = metadata.Owners;
            //IconUrl = metadata.IconUrl;
            //LicenseUrl = metadata.LicenseUrl;
            //ProjectUrl = metadata.ProjectUrl;
            //RequireLicenseAcceptance = metadata.RequireLicenseAcceptance;
            //DevelopmentDependency = metadata.DevelopmentDependency;
            //Description = metadata.Description;
            //Summary = metadata.Summary;
            //ReleaseNotes = metadata.ReleaseNotes;
            //Language = metadata.Language;
            //Tags = metadata.Tags;
            //DependencySets = metadata.DependencySets;
            //FrameworkAssemblies = metadata.FrameworkAssemblies;
            //Copyright = metadata.Copyright;
            //PackageAssemblyReferences = metadata.PackageAssemblyReferences;
            //MinClientVersion = metadata.MinClientVersion;

            //// Ensure tags start and end with an empty " " so we can do contains filtering reliably
            //if (!String.IsNullOrEmpty(Tags)) {
            //    Tags = " " + Tags + " ";
            //}
        }

        bool IsPackageFile(PackagePart part)
        {
            string path = UriUtility.GetPath(part.Uri);
            string directory = Path.GetDirectoryName(path);

            // We exclude any opc files and the manifest file (.nuspec)
            return !ExcludePaths.Any(p => directory.StartsWith(p, StringComparison.OrdinalIgnoreCase)) &&
                   !PackageHelper.IsManifest(path);
        }
    }
}
