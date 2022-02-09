using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using SharpCompress.Archives.Zip;

namespace Squirrel.NuGet
{
    internal interface IPackage
    {
        string Id { get; }
        string ProductName { get; }
        string ProductDescription { get; }
        string ProductCompany { get; }
        string ProductCopyright { get; }
        string Language { get; }
        SemanticVersion Version { get; }
        IEnumerable<string> Frameworks { get; }
        IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; }
        IEnumerable<PackageDependencySet> DependencySets { get; }
        Uri ProjectUrl { get; }
        string ReleaseNotes { get; }
        Uri IconUrl { get; }
        IEnumerable<string> Tags { get; }
        IEnumerable<string> RuntimeDependencies { get; }
        RuntimeCpu MachineArchitecture { get; }
    }

    internal class ZipPackage : IPackage
    {
        public string ProductName => Title ?? Id;
        public string ProductDescription => Description ?? Summary ?? Title ?? Id;
        public string ProductCompany => (Authors.Any() ? String.Join(", ", Authors) : Owners) ?? ProductName;
        public string ProductCopyright => Copyright ?? "Copyright © " + DateTime.Now.Year.ToString() + " " + ProductCompany;

        public string Id { get; private set; }
        public SemanticVersion Version { get; private set; }
        public IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; private set; } = Enumerable.Empty<FrameworkAssemblyReference>();
        public IEnumerable<PackageDependencySet> DependencySets { get; private set; } = Enumerable.Empty<PackageDependencySet>();
        public Uri ProjectUrl { get; private set; }
        public string ReleaseNotes { get; private set; }
        public Uri IconUrl { get; private set; }
        public string Language { get; private set; }
        public IEnumerable<string> Tags { get; private set; } = Enumerable.Empty<string>();
        public IEnumerable<string> RuntimeDependencies { get; private set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Frameworks { get; private set; }
        public RuntimeCpu MachineArchitecture { get; private set; }

        protected string Description { get; private set; }
        protected IEnumerable<string> Authors { get; private set; } = Enumerable.Empty<string>();
        protected string Owners { get; private set; }
        protected string Title { get; private set; }
        protected string Summary { get; private set; }
        protected string Copyright { get; private set; }

        private readonly Func<Stream> _streamFactory;
        private static readonly string[] ExcludePaths = new[] { "_rels", "package" };

        public ZipPackage(Stream zipStream)
        {
            using var zip = ZipArchive.Open(zipStream, new() { LeaveStreamOpen = true });
            using var manifest = GetManifestEntry(zip).OpenEntryStream();
            ReadManifest(manifest);
            Frameworks = GetFrameworks(zip);
        }

        public ZipPackage(string filePath)
        {
            if (String.IsNullOrEmpty(filePath)) {
                throw new ArgumentException("Argument_Cannot_Be_Null_Or_Empty", "filePath");
            }

            _streamFactory = () => File.OpenRead(filePath);

            using var stream = _streamFactory();
            using var zip = ZipArchive.Open(stream);
            using var manifest = GetManifestEntry(zip).OpenEntryStream();
            ReadManifest(manifest);
            Frameworks = GetFrameworks(zip);
        }

        private string[] GetFrameworks(ZipArchive zip)
        {
            var fileFrameworks = GetPackageFiles(zip)
                .Select(z => NugetUtil.ParseFrameworkNameFromFilePath(z.Path, out var _));

            return FrameworkAssemblies
                .SelectMany(f => f.SupportedFrameworks)
                .Concat(fileFrameworks)
                .Where(f => f != null)
                .Distinct()
                .ToArray();
        }

        public Stream ReadLibFileStream(string fileName)
        {
            using var stream = _streamFactory();
            using var zip = ZipArchive.Open(stream);

            string folderPrefix = NugetUtil.LibDirectory + Path.DirectorySeparatorChar;
            var files = GetPackageFiles(zip)
                .Where(z => z.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(z => Path.GetFileName(z.Path).Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (!files.Any()) {
                return null;
            }

            var f = files.First();
            using var fstream = f.Entry.OpenEntryStream();
            var ms = new MemoryStream();
            fstream.CopyTo(ms);
            return ms;
        }

        public IEnumerable<IPackageFile> GetLibFiles()
        {
            return GetFiles(NugetUtil.LibDirectory);
        }

        public IEnumerable<IPackageFile> GetContentFiles()
        {
            return GetFiles(NugetUtil.ContentDirectory);
        }

        public IEnumerable<IPackageFile> GetFiles(string directory)
        {
            string folderPrefix = directory + Path.DirectorySeparatorChar;
            return GetFiles().Where(file => file.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<IPackageFile> GetFiles()
        {
            using var stream = _streamFactory();
            using var zip = ZipArchive.Open(stream);
            return GetPackageFiles(zip)
                .Select(z => (IPackageFile) new ZipPackageFile(z.Path))
                .ToArray();
        }

        private IEnumerable<(string Path, ZipArchiveEntry Entry)> GetPackageFiles(ZipArchive zip)
        {
            return from entry in zip.Entries
                   where !entry.IsDirectory
                   let uri = new Uri(entry.Key, UriKind.Relative)
                   let path = NugetUtil.GetPath(uri)
                   where IsPackageFile(path)
                   select (path, entry);
        }

        private ZipArchiveEntry GetManifestEntry(ZipArchive zip)
        {
            var manifest = zip.Entries
              .FirstOrDefault(f => f.Key.EndsWith(NugetUtil.ManifestExtension, StringComparison.OrdinalIgnoreCase));

            if (manifest == null)
                throw new InvalidDataException("PackageDoesNotContainManifest");

            return manifest;
        }

        private void ReadManifest(Stream manifestStream)
        {
            var document = NugetUtil.LoadSafe(manifestStream, ignoreWhiteSpace: true);

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

        public static void SetSquirrelMetadata(string nuspecPath, RuntimeCpu architecture, IEnumerable<string> runtimes)
        {
            Dictionary<string, string> toSet = new();
            if (architecture != RuntimeCpu.Unknown)
                toSet.Add("machineArchitecture", architecture.ToString());
            if (runtimes.Any())
                toSet.Add("runtimeDependencies", String.Join(", ", runtimes));

            if (!toSet.Any())
                return;

            XDocument document;
            using (var fs = File.OpenRead(nuspecPath))
                document = NugetUtil.LoadSafe(fs, ignoreWhiteSpace: true);

            var metadataElement = document.Root.ElementsNoNamespace("metadata").FirstOrDefault();
            if (metadataElement == null) {
                throw new InvalidDataException(
                    String.Format(CultureInfo.CurrentCulture, "Manifest_RequiredElementMissing", "metadata"));
            }

            foreach (var el in toSet) {
                var elName = XName.Get(el.Key, document.Root.GetDefaultNamespace().NamespaceName);
                metadataElement.SetElementValue(elName, el.Value);
            }

            document.Save(nuspecPath);
        }

        private void ReadMetadataValue(XElement element, HashSet<string> allElements)
        {
            if (element.Value == null) {
                return;
            }

            allElements.Add(element.Name.LocalName);

            IEnumerable<string> getCommaDelimitedValue(string v)
            {
                return v?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()) ?? Enumerable.Empty<string>();
            }

            string value = element.Value.SafeTrim();
            switch (element.Name.LocalName) {
            case "id":
                Id = value;
                break;
            case "version":
                Version = new SemanticVersion(value);
                break;
            case "authors":
                Authors = getCommaDelimitedValue(value);
                break;
            case "owners":
                Owners = value;
                break;
            case "projectUrl":
                ProjectUrl = new Uri(value);
                break;
            case "iconUrl":
                IconUrl = new Uri(value);
                break;
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
            case "tags":
                Tags = getCommaDelimitedValue(value);
                break;
            case "dependencies":
                DependencySets = ReadDependencySets(element);
                break;
            case "frameworkAssemblies":
                FrameworkAssemblies = ReadFrameworkAssemblies(element);
                break;

            // ===
            // the following metadata elements are added by squirrel and are not
            // used by nuget.
            case "machineArchitecture":
                if (Enum.TryParse(value, true, out RuntimeCpu ma)) {
                    MachineArchitecture = ma;
                }
                break;
            case "runtimeDependencies":
                RuntimeDependencies = getCommaDelimitedValue(value);
                break;
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
                            element.GetOptionalAttributeValue("targetFramework").SafeTrim(),
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
                        element.GetOptionalAttributeValue("version").SafeTrim()
                    )).ToList();
        }

        private IEnumerable<string> ParseFrameworkNames(string frameworkNames)
        {
            if (String.IsNullOrEmpty(frameworkNames)) {
                return Enumerable.Empty<string>();
            }

            return frameworkNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private bool IsPackageFile(string partPath)
        {
            if (Path.GetFileName(partPath).Equals(ContentType.ContentTypeFileName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (Path.GetExtension(partPath).Equals(NugetUtil.ManifestExtension, StringComparison.OrdinalIgnoreCase))
                return false;

            string directory = Path.GetDirectoryName(partPath);
            return !ExcludePaths.Any(p => directory.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
