using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Squirrel.MarkdownSharp;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;
using System.Threading.Tasks;
using SharpCompress.Archives.Zip;
using SharpCompress.Readers;

namespace Squirrel
{
    internal interface IReleasePackage
    {
        string InputPackageFile { get; }
        string ReleasePackageFile { get; }
        string SuggestedReleaseFileName { get; }
        SemanticVersion Version { get; }
    }

    internal class ReleasePackage : IEnableLogger, IReleasePackage
    {
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

        public SemanticVersion Version => ReleaseEntry.ParseEntryFileName(InputPackageFile).Version;

#if NET5_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        internal string CreateReleasePackage(string outputFile, Func<string, string> releaseNotesProcessor = null, Action<string, ZipPackage> contentsPostProcessHook = null)
        {
            Contract.Requires(!String.IsNullOrEmpty(outputFile));
            releaseNotesProcessor = releaseNotesProcessor ?? (x => (new Markdown()).Transform(x));

            if (ReleasePackageFile != null) {
                return ReleasePackageFile;
            }

            var package = new ZipPackage(InputPackageFile);

            // just in-case our parsing is more-strict than nuget.exe and
            // the 'releasify' command was used instead of 'pack'.
            NugetUtil.ThrowIfInvalidNugetId(package.Id);

            // NB: Our test fixtures use packages that aren't SemVer compliant, 
            // we don't really care that they aren't valid
            if (!ModeDetector.InUnitTestRunner()) {
                // verify that the .nuspec version is semver compliant
                NugetUtil.ThrowIfVersionNotSemverCompliant(package.Version.ToString());

                // verify that the suggested filename can be round-tripped as an assurance 
                // someone won't run across an edge case and install a broken app somehow
                var idtest = ReleaseEntry.ParseEntryFileName(SuggestedReleaseFileName);
                if (idtest.PackageName != package.Id || idtest.Version != package.Version) {
                    throw new Exception($"The package id/version could not be properly parsed, are you using special characters?");
                }
            }

            // we can tell from here what platform(s) the package targets
            // but given this is a simple package we only
            // ever expect one entry here (crash hard otherwise)
            var frameworks = package.GetFrameworks();
            if (frameworks.Count() > 1) {
                var platforms = frameworks
                    .Aggregate(new StringBuilder(), (sb, f) => sb.Append(f.ToString() + "; "));

                throw new InvalidOperationException(String.Format(
                    "The input package file {0} targets multiple platforms - {1} - and cannot be transformed into a release package.", InputPackageFile, platforms));

            } else if (!frameworks.Any()) {
                throw new InvalidOperationException(String.Format(
                    "The input package file {0} targets no platform and cannot be transformed into a release package.", InputPackageFile));
            }

            // CS - docs say we don't support dependencies. I can't think of any reason allowing this is useful.
            if (package.DependencySets.Any()) {
                throw new InvalidOperationException(String.Format(
                     "The input package file {0} must have no dependencies.", InputPackageFile));
            }

            //var targetFramework = frameworks.Single();

            this.Log().Info("Creating release package: {0} => {1}", InputPackageFile, outputFile);

            string tempPath = null;

            using (Utility.WithTempDirectory(out tempPath, null)) {
                var tempDir = new DirectoryInfo(tempPath);

                extractZipWithEscaping(InputPackageFile, tempPath).Wait();

                var specPath = tempDir.GetFiles("*.nuspec").First().FullName;

                this.Log().Info("Removing unnecessary data");
                removeDependenciesFromPackageSpec(specPath);

                if (releaseNotesProcessor != null) {
                    renderReleaseNotesMarkdown(specPath, releaseNotesProcessor);
                }

                addDeltaFilesToContentTypes(tempDir.FullName);

                contentsPostProcessHook?.Invoke(tempPath, package);

                HelperExe.CreateZipFromDirectory(outputFile, tempPath).Wait();

                ReleasePackageFile = outputFile;
                return ReleasePackageFile;
            }
        }

        static Task extractZipWithEscaping(string zipFilePath, string outFolder)
        {
            return Task.Run(() => {
                using (var za = ZipArchive.Open(zipFilePath))
                using (var reader = za.ExtractAllEntries()) {
                    while (reader.MoveToNextEntry()) {
                        var parts = reader.Entry.Key.Split('\\', '/').Select(x => Uri.UnescapeDataString(x));
                        var decoded = String.Join(Path.DirectorySeparatorChar.ToString(), parts);

                        var fullTargetFile = Path.Combine(outFolder, decoded);
                        var fullTargetDir = Path.GetDirectoryName(fullTargetFile);
                        Directory.CreateDirectory(fullTargetDir);

                        Utility.Retry(() => {
                            if (reader.Entry.IsDirectory) {
                                Directory.CreateDirectory(Path.Combine(outFolder, decoded));
                            } else {
                                reader.WriteEntryToFile(Path.Combine(outFolder, decoded));
                            }
                        }, 5);
                    }
                }
            });
        }

        public static Task ExtractZipForInstall(string zipFilePath, string outFolder, string rootPackageFolder)
        {
            return ExtractZipForInstall(zipFilePath, outFolder, rootPackageFolder, x => { });
        }

        public static Task ExtractZipForInstall(string zipFilePath, string outFolder, string rootPackageFolder, Action<int> progress)
        {
            var re = new Regex(@"lib[\\\/][^\\\/]*[\\\/]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            return Task.Run(() => {
                using (var za = ZipArchive.Open(zipFilePath))
                using (var reader = za.ExtractAllEntries()) {
                    var totalItems = za.Entries.Count;
                    var currentItem = 0;

                    while (reader.MoveToNextEntry()) {
                        // Report progress early since we might be need to continue for non-matches
                        currentItem++;
                        var percentage = (currentItem * 100d) / totalItems;
                        progress((int) percentage);

                        var parts = reader.Entry.Key.Split('\\', '/').Select(x => Uri.UnescapeDataString(x));
                        var decoded = String.Join(Path.DirectorySeparatorChar.ToString(), parts);

                        if (!re.IsMatch(decoded)) continue;
                        decoded = re.Replace(decoded, "", 1);

                        var fullTargetFile = Path.Combine(outFolder, decoded);
                        var fullTargetDir = Path.GetDirectoryName(fullTargetFile);
                        Directory.CreateDirectory(fullTargetDir);

                        var failureIsOkay = false;
                        if (!reader.Entry.IsDirectory && decoded.Contains("_ExecutionStub.exe")) {
                            // NB: On upgrade, many of these stubs will be in-use, nbd tho.
                            failureIsOkay = true;

                            fullTargetFile = Path.Combine(
                                rootPackageFolder,
                                Path.GetFileName(decoded).Replace("_ExecutionStub.exe", ".exe"));

                            LogHost.Default.Info("Rigging execution stub for {0} to {1}", decoded, fullTargetFile);
                        }

                        try {
                            Utility.Retry(() => {
                                if (reader.Entry.IsDirectory) {
                                    Directory.CreateDirectory(fullTargetFile);
                                } else {
                                    reader.WriteEntryToFile(fullTargetFile);
                                }
                            }, 5);
                        } catch (Exception e) {
                            if (!failureIsOkay) throw;
                            LogHost.Default.WarnException("Can't write execution stub, probably in use", e);
                        }
                    }
                }

                progress(100);
            });
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

        static internal void addDeltaFilesToContentTypes(string rootDirectory)
        {
            var doc = new XmlDocument();
            var path = Path.Combine(rootDirectory, ContentType.ContentTypeFileName);
            doc.Load(path);

            ContentType.Merge(doc);
            ContentType.Clean(doc);

            using (var sw = new StreamWriter(path, false, Encoding.UTF8)) {
                doc.Save(sw);
            }
        }
    }
}
