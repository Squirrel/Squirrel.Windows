using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SharpCompress.Archives.Zip;
using SharpCompress.Readers;
using Squirrel.SimpleSplat;

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
        {
        }

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

        public static Task ExtractZipReleaseForInstall(string zipFilePath, string outFolder, string rootPackageFolder, Action<int> progress)
        {
            if (SquirrelRuntimeInfo.IsWindows)
                return ExtractZipReleaseForInstallWindows(zipFilePath, outFolder, rootPackageFolder, progress);

            if (SquirrelRuntimeInfo.IsOSX)
                return ExtractZipReleaseForInstallOSX(zipFilePath, outFolder, progress);

            throw new NotSupportedException("Platform not supported.");
        }

        private static readonly Regex libFolderPattern = new Regex(@"lib[\\\/][^\\\/]*[\\\/]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [SupportedOSPlatform("macos")]
        public static Task ExtractZipReleaseForInstallOSX(string zipFilePath, string outFolder, Action<int> progress)
        {
            progress ??= ((_) => { });
            Directory.CreateDirectory(outFolder);
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

                        if (!libFolderPattern.IsMatch(decoded)) continue;
                        decoded = libFolderPattern.Replace(decoded, "", 1);

                        var fullTargetFile = Path.Combine(outFolder, decoded);
                        var fullTargetDir = Path.GetDirectoryName(fullTargetFile);
                        Directory.CreateDirectory(fullTargetDir);

                        Utility.Retry(() => {
                            if (reader.Entry.IsDirectory) {
                                Directory.CreateDirectory(fullTargetFile);
                            } else {
                                reader.WriteEntryToFile(fullTargetFile);
                            }
                        }, 5);
                    }
                }

                progress(100);
            });
        }

        [SupportedOSPlatform("windows")]
        public static Task ExtractZipReleaseForInstallWindows(string zipFilePath, string outFolder, string rootPackageFolder, Action<int> progress)
        {
            progress ??= ((_) => { });
            Directory.CreateDirectory(outFolder);

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

                        // extract .nuspec to app directory as '.version'
                        if (Utility.FileHasExtension(reader.Entry.Key, NugetUtil.ManifestExtension)) {
                            Utility.Retry(() => reader.WriteEntryToFile(Path.Combine(outFolder, Utility.SpecVersionFileName)));
                            continue;
                        }

                        var parts = reader.Entry.Key.Split('\\', '/').Select(x => Uri.UnescapeDataString(x));
                        var decoded = String.Join(Path.DirectorySeparatorChar.ToString(), parts);

                        if (!libFolderPattern.IsMatch(decoded)) continue;
                        decoded = libFolderPattern.Replace(decoded, "", 1);

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

                        if (Utility.PathPartEquals(parts.Last(), "app.ico")) {
                            failureIsOkay = true;
                            fullTargetFile = Path.Combine(rootPackageFolder, "app.ico");
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
    }
}