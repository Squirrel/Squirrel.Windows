using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Squirrel.SimpleSplat;
using Squirrel.Bsdiff;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Compressors.Deflate;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

namespace Squirrel
{
    internal interface IDeltaPackageBuilder
    {
        ReleasePackage CreateDeltaPackage(ReleasePackage basePackage, ReleasePackage newPackage, string outputFile);
        ReleasePackage ApplyDeltaPackage(ReleasePackage basePackage, ReleasePackage deltaPackage, string outputFile);
    }

    internal class DeltaPackageBuilder : IEnableLogger, IDeltaPackageBuilder
    {
        readonly string localAppDirectory;
        public DeltaPackageBuilder(string localAppDataOverride = null)
        {
            this.localAppDirectory = localAppDataOverride;
        }

        public ReleasePackage CreateDeltaPackage(ReleasePackage basePackage, ReleasePackage newPackage, string outputFile)
        {
            Contract.Requires(basePackage != null);
            Contract.Requires(!String.IsNullOrEmpty(outputFile) && !File.Exists(outputFile));

            if (basePackage.Version > newPackage.Version) {
                var message = String.Format(
                    "You cannot create a delta package based on version {0} as it is a later version than {1}",
                    basePackage.Version,
                    newPackage.Version);
                throw new InvalidOperationException(message);
            }

            if (basePackage.ReleasePackageFile == null) {
                throw new ArgumentException("The base package's release file is null", "basePackage");
            }

            if (!File.Exists(basePackage.ReleasePackageFile)) {
                throw new FileNotFoundException("The base package release does not exist", basePackage.ReleasePackageFile);
            }

            if (!File.Exists(newPackage.ReleasePackageFile)) {
                throw new FileNotFoundException("The new package release does not exist", newPackage.ReleasePackageFile);
            }

            string baseTempPath = null;
            string tempPath = null;

            using (Utility.WithTempDirectory(out baseTempPath, null))
            using (Utility.WithTempDirectory(out tempPath, null)) {
                var baseTempInfo = new DirectoryInfo(baseTempPath);
                var tempInfo = new DirectoryInfo(tempPath);

                const int numParallel = 4;

                this.Log().Info($"Creating delta for {basePackage.Version} -> {newPackage.Version} with {numParallel} parallel threads.");

                this.Log().Debug("Extracting {0} and {1} into {2}",
                    basePackage.ReleasePackageFile, newPackage.ReleasePackageFile, tempPath);

                EasyZip.ExtractZipToDirectory(basePackage.ReleasePackageFile, baseTempInfo.FullName);
                EasyZip.ExtractZipToDirectory(newPackage.ReleasePackageFile, tempInfo.FullName);

                // Collect a list of relative paths under 'lib' and map them
                // to their full name. We'll use this later to determine in
                // the new version of the package whether the file exists or
                // not.
                var baseLibFiles = baseTempInfo.GetAllFilesRecursively()
                    .Where(x => x.FullName.ToLowerInvariant().Contains("lib" + Path.DirectorySeparatorChar))
                    .ToDictionary(k => k.FullName.Replace(baseTempInfo.FullName, ""), v => v.FullName);

                var newLibDir = tempInfo.GetDirectories().First(x => x.Name.ToLowerInvariant() == "lib");
                var newLibFiles = newLibDir.GetAllFilesRecursively().ToArray();

                int fNew = 0, fSame = 0, fChanged = 0;

                bool bytesAreIdentical(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
                {
                    return a1.SequenceEqual(a2);
                }

                void createDeltaForSingleFile(FileInfo targetFile, DirectoryInfo workingDirectory, Dictionary<string, string> baseFileListing)
                {
                    // NB: There are three cases here that we'll handle:
                    //
                    // 1. Exists only in new => leave it alone, we'll use it directly.
                    // 2. Exists in both old and new => write a dummy file so we know
                    //    to keep it.
                    // 3. Exists in old but changed in new => create a delta file
                    //
                    // The fourth case of "Exists only in old => delete it in new"
                    // is handled when we apply the delta package
                    var relativePath = targetFile.FullName.Replace(workingDirectory.FullName, "");

                    if (!baseFileListing.ContainsKey(relativePath)) {
                        this.Log().Debug("{0} not found in base package, marking as new", relativePath);
                        fNew++;
                        return;
                    }

                    var oldFilePath = baseFileListing[relativePath];
                    baseFileListing.Remove(relativePath);

                    var oldData = File.ReadAllBytes(oldFilePath);
                    var newData = File.ReadAllBytes(targetFile.FullName);

                    if (bytesAreIdentical(oldData, newData)) {
                        this.Log().Debug("{0} hasn't changed, writing dummy file", relativePath);
                        File.Create(targetFile.FullName + ".bsdiff").Dispose();
                        File.Create(targetFile.FullName + ".shasum").Dispose();
                        targetFile.Delete();
                        fSame++;
                        return;
                    }

                    this.Log().Debug("Delta patching {0} => {1}", oldFilePath, targetFile.FullName);

                    try {
                        using (FileStream of = File.Create(targetFile.FullName + ".bsdiff")) {
                            BinaryPatchUtility.Create(oldData, newData, of);
                        }
                        var rl = ReleaseEntry.GenerateFromFile(new MemoryStream(newData), targetFile.Name + ".shasum");
                        File.WriteAllText(targetFile.FullName + ".shasum", rl.EntryAsString, Encoding.UTF8);
                        targetFile.Delete();
                        fChanged++;
                    } catch (Exception ex) {
                        this.Log().DebugException(String.Format("We couldn't create a delta for {0}", targetFile.Name), ex);
                        Utility.DeleteFileOrDirectoryHardOrGiveUp(targetFile.FullName + ".bsdiff");
                        Utility.DeleteFileOrDirectoryHardOrGiveUp(targetFile.FullName + ".diff");
                        Utility.DeleteFileOrDirectoryHardOrGiveUp(targetFile.FullName + ".shasum");
                        throw;
                    }
                }

                var tResult = Task.Run(() => {
                    Parallel.ForEach(newLibFiles, new ParallelOptions() { MaxDegreeOfParallelism = numParallel }, (f) => {
                        try {
                            createDeltaForSingleFile(f, tempInfo, baseLibFiles);
                        } catch (Exception ex) {
                            this.Log().WarnException($"Unable to create delta for '{f}', retrying...", ex);
                            createDeltaForSingleFile(f, tempInfo, baseLibFiles);
                        }
                    });
                });

                int prevCount = 0;
                while (!tResult.IsCompleted) {
                    // sleep for 3 seconds (in 100ms intervals)
                    for (int i = 0; i < 30 && !tResult.IsCompleted; i++)
                        Thread.Sleep(100);

                    int processed = fNew + fChanged + fSame;
                    if (prevCount == processed) {
                        // if there has been no progress, do not print another message
                        continue;
                    }

                    this.Log().Info($"Processed {processed}/{newLibFiles.Length} files. {fChanged} patched, {fNew} new, {fSame} unchanged.");
                    prevCount = processed;
                }

                if (tResult.Exception != null)
                    throw new Exception("Unable to create delta package.", tResult.Exception);

                ReleasePackage.addDeltaFilesToContentTypes(tempInfo.FullName);
                EasyZip.CreateZipFromDirectory(outputFile, tempInfo.FullName);

                this.Log().Info($"Successfully created delta package for {basePackage.Version} -> {newPackage.Version}.");
                this.Log().Info($"{newLibFiles.Length} files processed. {fChanged} patched, {fNew} new, {fSame} unchanged, {baseLibFiles.Count} removed.");
            }

            return new ReleasePackage(outputFile);
        }

        public ReleasePackage ApplyDeltaPackage(ReleasePackage basePackage, ReleasePackage deltaPackage, string outputFile)
        {
            return ApplyDeltaPackage(basePackage, deltaPackage, outputFile, x => { });
        }

        public ReleasePackage ApplyDeltaPackage(ReleasePackage basePackage, ReleasePackage deltaPackage, string outputFile, Action<int> progress)
        {
            Contract.Requires(deltaPackage != null);
            Contract.Requires(!String.IsNullOrEmpty(outputFile) && !File.Exists(outputFile));

            string workingPath;
            string deltaPath;

            using (Utility.WithTempDirectory(out deltaPath, localAppDirectory))
            using (Utility.WithTempDirectory(out workingPath, localAppDirectory)) {
                var opts = new ExtractionOptions() { ExtractFullPath = true, Overwrite = true, PreserveFileTime = true };

                using (var za = ZipArchive.Open(deltaPackage.InputPackageFile))
                using (var reader = za.ExtractAllEntries()) {
                    reader.WriteAllToDirectory(deltaPath, opts);
                }

                progress(25);

                using (var za = ZipArchive.Open(basePackage.InputPackageFile))
                using (var reader = za.ExtractAllEntries()) {
                    reader.WriteAllToDirectory(workingPath, opts);
                }

                progress(50);

                var pathsVisited = new List<string>();

                var deltaPathRelativePaths = new DirectoryInfo(deltaPath).GetAllFilesRecursively()
                    .Select(x => x.FullName.Replace(deltaPath + Path.DirectorySeparatorChar, ""))
                    .ToArray();

                // Apply all of the .diff files
                deltaPathRelativePaths
                    .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                    .Where(x => !x.EndsWith(".shasum", StringComparison.InvariantCultureIgnoreCase))
                    .Where(x => !x.EndsWith(".diff", StringComparison.InvariantCultureIgnoreCase) ||
                                !deltaPathRelativePaths.Contains(x.Replace(".diff", ".bsdiff")))
                    .ForEach(file => {
                        pathsVisited.Add(Regex.Replace(file, @"\.(bs)?diff$", "").ToLowerInvariant());
                        applyDiffToFile(deltaPath, file, workingPath);
                    });

                progress(75);

                // Delete all of the files that were in the old package but
                // not in the new one.
                new DirectoryInfo(workingPath).GetAllFilesRecursively()
                    .Select(x => x.FullName.Replace(workingPath + Path.DirectorySeparatorChar, "").ToLowerInvariant())
                    .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase) && !pathsVisited.Contains(x))
                    .ForEach(x => {
                        this.Log().Info("{0} was in old package but not in new one, deleting", x);
                        File.Delete(Path.Combine(workingPath, x));
                    });

                progress(80);

                // Update all the files that aren't in 'lib' with the delta
                // package's versions (i.e. the nuspec file, etc etc).
                deltaPathRelativePaths
                    .Where(x => !x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                    .ForEach(x => {
                        this.Log().Info("Updating metadata file: {0}", x);
                        File.Copy(Path.Combine(deltaPath, x), Path.Combine(workingPath, x), true);
                    });

                this.Log().Info("Repacking into full package: {0}", outputFile);
                using (var za = ZipArchive.Create())
                using (var tgt = File.OpenWrite(outputFile)) {
                    za.DeflateCompressionLevel = CompressionLevel.BestSpeed;
                    za.AddAllFromDirectory(workingPath);
                    za.SaveTo(tgt);
                }

                progress(100);
            }

            return new ReleasePackage(outputFile);
        }

        void applyDiffToFile(string deltaPath, string relativeFilePath, string workingDirectory)
        {
            var inputFile = Path.Combine(deltaPath, relativeFilePath);
            var finalTarget = Path.Combine(workingDirectory, Regex.Replace(relativeFilePath, @"\.(bs)?diff$", ""));

            var tempTargetFile = default(string);
            Utility.WithTempFile(out tempTargetFile, localAppDirectory);

            try {
                // NB: Zero-length diffs indicate the file hasn't actually changed
                if (new FileInfo(inputFile).Length == 0) {
                    this.Log().Info("{0} exists unchanged, skipping", relativeFilePath);
                    return;
                }

                if (relativeFilePath.EndsWith(".bsdiff", StringComparison.InvariantCultureIgnoreCase)) {
                    using (var of = File.OpenWrite(tempTargetFile))
                    using (var inf = File.OpenRead(finalTarget)) {
                        this.Log().Info("Applying bsdiff to {0}", relativeFilePath);
                        BinaryPatchUtility.Apply(inf, () => File.OpenRead(inputFile), of);
                    }

                    verifyPatchedFile(relativeFilePath, inputFile, tempTargetFile);
                } else if (relativeFilePath.EndsWith(".diff", StringComparison.InvariantCultureIgnoreCase)) {
                    this.Log().Info("Applying msdiff to {0}", relativeFilePath);

#if NETFRAMEWORK
                    if (true) {
#else
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
#endif
                        MsDeltaCompression.ApplyDelta(inputFile, finalTarget, tempTargetFile);
                    } else {
                        throw new InvalidOperationException("msdiff is not supported on non-windows platforms.");
                    }
                    verifyPatchedFile(relativeFilePath, inputFile, tempTargetFile);
                } else {
                    using (var of = File.OpenWrite(tempTargetFile))
                    using (var inf = File.OpenRead(inputFile)) {
                        this.Log().Info("Adding new file: {0}", relativeFilePath);
                        inf.CopyTo(of);
                    }
                }

                if (File.Exists(finalTarget)) File.Delete(finalTarget);

                var targetPath = Directory.GetParent(finalTarget);
                if (!targetPath.Exists) targetPath.Create();

                File.Move(tempTargetFile, finalTarget);
            } finally {
                if (File.Exists(tempTargetFile)) Utility.DeleteFileOrDirectoryHardOrGiveUp(tempTargetFile);
            }
        }

        void verifyPatchedFile(string relativeFilePath, string inputFile, string tempTargetFile)
        {
            var shaFile = Regex.Replace(inputFile, @"\.(bs)?diff$", ".shasum");
            var expectedReleaseEntry = ReleaseEntry.ParseReleaseEntry(File.ReadAllText(shaFile, Encoding.UTF8));
            var actualReleaseEntry = ReleaseEntry.GenerateFromFile(tempTargetFile);

            if (expectedReleaseEntry.Filesize != actualReleaseEntry.Filesize) {
                this.Log().Warn("Patched file {0} has incorrect size, expected {1}, got {2}", relativeFilePath,
                    expectedReleaseEntry.Filesize, actualReleaseEntry.Filesize);
                throw new ChecksumFailedException() { Filename = relativeFilePath };
            }

            if (expectedReleaseEntry.SHA1 != actualReleaseEntry.SHA1) {
                this.Log().Warn("Patched file {0} has incorrect SHA1, expected {1}, got {2}", relativeFilePath,
                    expectedReleaseEntry.SHA1, actualReleaseEntry.SHA1);
                throw new ChecksumFailedException() { Filename = relativeFilePath };
            }
        }
    }
}
