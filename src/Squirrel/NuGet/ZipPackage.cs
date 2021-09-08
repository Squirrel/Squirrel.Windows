using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Squirrel.NuGet
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
        public IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; private set; }
        public IEnumerable<PackageDependencySet> DependencySets { get; private set; }
        public Uri ProjectUrl { get; private set; }
        public string ReleaseNotes { get; private set; }
        public Uri IconUrl { get; private set; }

        private readonly Func<Stream> _streamFactory;
        private static readonly string[] ExcludePaths = new[] { "_rels", "package" };
        private const string ManifestRelationType = "manifest";

        public ZipPackage(string filePath)
        {
            if (String.IsNullOrEmpty(filePath)) {
                throw new ArgumentException("Argument_Cannot_Be_Null_Or_Empty", "filePath");
            }

            _streamFactory = () => File.OpenRead(filePath);
            EnsureManifest();
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

        private void EnsureManifest()
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
            var document = XmlUtility.LoadSafe(manifestStream, ignoreWhiteSpace: true);

            var metadataElement = document.Root.ElementsNoNamespace("metadata").FirstOrDefault();
            if (metadataElement == null) {
                throw new InvalidDataException(
                    String.Format(CultureInfo.CurrentCulture, "Manifest_RequiredElementMissing", "metadata"));
            }

            var allElements = new HashSet<string>();

            XNode node = metadataElement.FirstNode;
            while (node != null) {
                var element = node as XElement;
                if (element != null) {
                    ReadMetadataValue(element, allElements);
                }
                node = node.NextNode;
            }
        }

        private void ReadMetadataValue(XElement element, HashSet<string> allElements)
        {
            if (element.Value == null) {
                return;
            }

            allElements.Add(element.Name.LocalName);

            string value = element.Value.SafeTrim();
            switch (element.Name.LocalName) {
            case "id":
                Id = value;
                break;
            case "version":
                Version = new SemanticVersion(value);
                break;
            case "authors":
                Authors = value?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>();
                break;
            //case "owners":
            //    Owners = value;
            //    break;
            //case "licenseUrl":
            //    LicenseUrl = value;
            //    break;
            case "projectUrl":
                ProjectUrl = new Uri(value);
                break;
            case "iconUrl":
                IconUrl = new Uri(value);
                break;
            //case "requireLicenseAcceptance":
            //    RequireLicenseAcceptance = XmlConvert.ToBoolean(value);
            //    break;
            //case "developmentDependency":
            //    DevelopmentDependency = XmlConvert.ToBoolean(value);
            //    break;
            case "description":
                Description = value;
                break;
            case "summary":
                Summary = value;
                break;
            case "releaseNotes":
                ReleaseNotes = value;
                break;
            case "copyright":
                Copyright = value;
                break;
            case "language":
                Language = value;
                break;
            case "title":
                Title = value;
                break;
            //case "tags":
            //    Tags = value;
            //    break;
            case "dependencies":
                DependencySets = ReadDependencySets(element);
                break;
            case "frameworkAssemblies":
                FrameworkAssemblies = ReadFrameworkAssemblies(element);
                break;
                //case "references":
                //    ReferenceSets = ReadReferenceSets(element);
                //    break;
            }
        }

        private List<FrameworkAssemblyReference> ReadFrameworkAssemblies(XElement frameworkElement)
        {
            if (!frameworkElement.HasElements) {
                return new List<FrameworkAssemblyReference>(0);
            }

            return (from element in frameworkElement.ElementsNoNamespace("frameworkAssembly")
                    let assemblyNameAttribute = element.Attribute("assemblyName")
                    where assemblyNameAttribute != null && !String.IsNullOrEmpty(assemblyNameAttribute.Value)
                    select new FrameworkAssemblyReference(
                        assemblyNameAttribute.Value.SafeTrim(),
                        ParseFrameworkNames(element.GetOptionalAttributeValue("targetFramework").SafeTrim()))
                    ).ToList();
        }

        private List<PackageDependencySet> ReadDependencySets(XElement dependenciesElement)
        {
            if (!dependenciesElement.HasElements) {
                return new List<PackageDependencySet>();
            }

            // Disallow the <dependencies> element to contain both <dependency> and 
            // <group> child elements. Unfortunately, this cannot be enforced by XSD.
            if (dependenciesElement.ElementsNoNamespace("dependency").Any() &&
                dependenciesElement.ElementsNoNamespace("group").Any()) {
                throw new InvalidDataException("Manifest_DependenciesHasMixedElements");
            }

            var dependencies = ReadDependencies(dependenciesElement);
            if (dependencies.Count > 0) {
                // old format, <dependency> is direct child of <dependencies>
                var dependencySet = new PackageDependencySet(null, dependencies);
                return new List<PackageDependencySet> { dependencySet };
            } else {
                var groups = dependenciesElement.ElementsNoNamespace("group");
                return (from element in groups
                        let fx = ParseFrameworkNames(element.GetOptionalAttributeValue("targetFramework").SafeTrim())
                        select new PackageDependencySet(
                            VersionUtility.ParseFrameworkName(element.GetOptionalAttributeValue("targetFramework").SafeTrim()),
                            ReadDependencies(element))).ToList();
            }
        }

        private List<PackageDependency> ReadDependencies(XElement containerElement)
        {
            // element is <dependency>
            return (from element in containerElement.ElementsNoNamespace("dependency")
                    let idElement = element.Attribute("id")
                    where idElement != null && !String.IsNullOrEmpty(idElement.Value)
                    select new PackageDependency(
                        idElement.Value.SafeTrim(),
                        VersionUtility.ParseVersionSpec(element.GetOptionalAttributeValue("version").SafeTrim())
                    )).ToList();
        }

        private IEnumerable<FrameworkName> ParseFrameworkNames(string frameworkNames)
        {
            if (String.IsNullOrEmpty(frameworkNames)) {
                return Enumerable.Empty<FrameworkName>();
            }

            return frameworkNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(VersionUtility.ParseFrameworkName);
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
