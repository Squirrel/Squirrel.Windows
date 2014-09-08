using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Ionic.Zip;
using MarkdownSharp;
using NuGet;
using Splat;

namespace Squirrel
{
    internal static class FrameworkTargetVersion
    {
        public static FrameworkName Net40 = new FrameworkName(".NETFramework,Version=v4.0");
        public static FrameworkName Net45 = new FrameworkName(".NETFramework,Version=v4.5");
    }

    public interface IReleasePackage
    {
        string InputPackageFile { get; }
        string ReleasePackageFile { get; }
        string SuggestedReleaseFileName { get; }

        string CreateReleasePackage(string outputFile, string packagesRootDir = null, Func<string, string> releaseNotesProcessor = null);
    }

    public static class VersionComparer
    {
        public static bool Matches(IVersionSpec versionSpec, SemanticVersion version)
        {
            if (versionSpec == null)
                return true; // I CAN'T DEAL WITH THIS

            bool minVersion;
            if (versionSpec.MinVersion == null) {
                minVersion = true; // no preconditon? LET'S DO IT
            } else if (versionSpec.IsMinInclusive) {
                minVersion = version >= versionSpec.MinVersion;
            } else {
                minVersion = version > versionSpec.MinVersion;
            }

            bool maxVersion;
            if (versionSpec.MaxVersion == null) {
                maxVersion = true; // no preconditon? LET'S DO IT
            } else if (versionSpec.IsMaxInclusive) {
                maxVersion = version <= versionSpec.MaxVersion;
            } else {
                maxVersion = version < versionSpec.MaxVersion;
            }

            return maxVersion && minVersion;
        }
    }

    public class ReleasePackage : IEnableLogger, IReleasePackage
    {
        IEnumerable<IPackage> localPackageCache;

        public ReleasePackage(string inputPackageFile, bool isReleasePackage = false)
        {
            InputPackageFile = inputPackageFile;

            if (isReleasePackage) {
                ReleasePackageFile = inputPackageFile;
            }
        }

        public string InputPackageFile { get; protected set; }
        public string ReleasePackageFile { get; protected set; }

        public string SuggestedReleaseFileName {
            get {
                var zp = new ZipPackage(InputPackageFile);
                return String.Format("{0}-{1}-full.nupkg", zp.Id, zp.Version);
            }
        }

        public Version Version { get { return InputPackageFile.ToVersion(); } }

        public string CreateReleasePackage(string outputFile, string packagesRootDir = null, Func<string, string> releaseNotesProcessor = null)
        {
            Contract.Requires(!String.IsNullOrEmpty(outputFile));
            releaseNotesProcessor = releaseNotesProcessor ?? (x => (new Markdown()).Transform(x));

            if (ReleasePackageFile != null) {
                return ReleasePackageFile;
            }

            var package = new ZipPackage(InputPackageFile);

            // we can tell from here what platform(s) the package targets
            // but given this is a simple package we only
            // ever expect one entry here (crash hard otherwise)
            var frameworks = package.GetSupportedFrameworks();
            if (frameworks.Count() > 1) {

                var platforms = frameworks
                    .Aggregate(new StringBuilder(), (sb, f) => sb.Append(f.ToString() + "; "));

                throw new InvalidOperationException(String.Format(
                    "The input package file {0} targets multiple platforms - {1} - and cannot be transformed into a release package.", InputPackageFile, platforms));
            }

            var targetFramework = frameworks.Single();

            // Recursively walk the dependency tree and extract all of the
            // dependent packages into the a temporary directory
            this.Log().Info("Creating release package: {0} => {1}", InputPackageFile, outputFile);
            var dependencies = findAllDependentPackages(
                package,
                new LocalPackageRepository(packagesRootDir),
                frameworkName: targetFramework);

            string tempPath = null;

            using (Utility.WithTempDirectory(out tempPath)) {
                var tempDir = new DirectoryInfo(tempPath);

                using(var zf = new ZipFile(InputPackageFile)) {
                    zf.ExtractAll(tempPath);
                }

                this.Log().Info("Extracting dependent packages: [{0}]", String.Join(",", dependencies.Select(x => x.Id)));
                extractDependentPackages(dependencies, tempDir, targetFramework);

                var specPath = tempDir.GetFiles("*.nuspec").First().FullName;

                this.Log().Info("Removing unnecessary data");
                removeDependenciesFromPackageSpec(specPath);
                removeDeveloperDocumentation(tempDir);

                if (releaseNotesProcessor != null) {
                    renderReleaseNotesMarkdown(specPath, releaseNotesProcessor);
                }

                addDeltaFilesToContentTypes(tempDir.FullName);

                using (var zf = new ZipFile(outputFile)) {
                    this.Log().Info("Succeeeded, saving to " + outputFile);
                    zf.AddDirectory(tempPath);
                    zf.Save();
                }

                ReleasePackageFile = outputFile;
                return ReleasePackageFile;
            }
        }

        void extractDependentPackages(IEnumerable<IPackage> dependencies, DirectoryInfo tempPath, FrameworkName framework)
        {
            dependencies.ForEach(pkg => {
                this.Log().Info("Scanning {0}", pkg.Id);

                pkg.GetLibFiles().ForEach(file => {
                    var outPath = new FileInfo(Path.Combine(tempPath.FullName, file.Path));

                    if (!VersionUtility.IsCompatible(framework , new[] { file.TargetFramework }))
                    {
                        this.Log().Info("Ignoring {0} as the target framework is not compatible", outPath);
                        return;
                    }

                    Directory.CreateDirectory(outPath.Directory.FullName);

                    using (var of = File.Create(outPath.FullName)) {
                        this.Log().Info("Writing {0} to {1}", file.Path, outPath);
                        file.GetStream().CopyTo(of);
                    }
                });
            });
        }

        void removeDeveloperDocumentation(DirectoryInfo expandedRepoPath)
        {
            expandedRepoPath.GetAllFilesRecursively()
                .Where(x => x.Name.EndsWith(".dll", true, CultureInfo.InvariantCulture))
                .Select(x => new FileInfo(x.FullName.ToLowerInvariant().Replace(".dll", ".xml")))
                .Where(x => x.Exists)
                .ForEach(x => x.Delete());
        }

        void renderReleaseNotesMarkdown(string specPath, Func<string, string> releaseNotesProcessor)
        {
            var doc = new XmlDocument();
            doc.Load(specPath);

            // XXX: This code looks full tart
            var metadata = doc.DocumentElement.ChildNodes
                .OfType<XmlElement>()
                .First(x => x.Name.ToLowerInvariant() == "metadata");

            var releaseNotes = metadata.ChildNodes
                .OfType<XmlElement>()
                .FirstOrDefault(x => x.Name.ToLowerInvariant() == "releasenotes");

            if (releaseNotes == null) {
                this.Log().Info("No release notes found in {0}", specPath);
                return;
            }

            releaseNotes.InnerText = String.Format("<![CDATA[\n" + "{0}\n" + "]]>",
                releaseNotesProcessor(releaseNotes.InnerText));

            doc.Save(specPath);
        }

        void removeDependenciesFromPackageSpec(string specPath)
        {
            var xdoc = new XmlDocument();
            xdoc.Load(specPath);

            var metadata = xdoc.DocumentElement.FirstChild;
            var dependenciesNode = metadata.ChildNodes.OfType<XmlElement>().FirstOrDefault(x => x.Name.ToLowerInvariant() == "dependencies");
            if (dependenciesNode != null) {
                metadata.RemoveChild(dependenciesNode);
            }

            xdoc.Save(specPath);
        }

        internal IEnumerable<IPackage> findAllDependentPackages(
            IPackage package = null,
            IPackageRepository packageRepository = null,
            HashSet<string> packageCache = null,
            FrameworkName frameworkName = null)
        {
            package = package ?? new ZipPackage(InputPackageFile);
            packageCache = packageCache ?? new HashSet<string>();

            var deps = package.DependencySets
                .Where(x => x.TargetFramework == null
                            || x.TargetFramework == frameworkName)
                .SelectMany(x => x.Dependencies);

            return deps.SelectMany(dependency => {
                var ret = matchPackage(packageRepository, dependency.Id, dependency.VersionSpec);

                if (ret == null) {
                    var message = String.Format("Couldn't find file for package in {1}: {0}", dependency.Id, packageRepository.Source);
                    this.Log().Error(message);
                    throw new Exception(message);
                }

                if (packageCache.Contains(ret.GetFullName())) {
                    return Enumerable.Empty<IPackage>();
                }

                packageCache.Add(ret.GetFullName());

                return findAllDependentPackages(ret, packageRepository, packageCache, frameworkName).StartWith(ret).Distinct(y => y.GetFullName());
            }).ToArray();
        }

        IPackage matchPackage(IPackageRepository packageRepository, string id, IVersionSpec version)
        {
            return packageRepository.FindPackagesById(id).FirstOrDefault(x => VersionComparer.Matches(version, x.Version));
        }


        static internal void addDeltaFilesToContentTypes(string rootDirectory)
        {
            var doc = new XmlDocument();
            var path = Path.Combine(rootDirectory, "[Content_Types].xml");
            doc.Load(path);

            ContentType.Merge(doc);

            using (var sw = new StreamWriter(path, false, Encoding.UTF8)) {
                doc.Save(sw);
            }
        }
    }

    public class ChecksumFailedException : Exception
    {
        public string Filename { get; set; }
    }
}
