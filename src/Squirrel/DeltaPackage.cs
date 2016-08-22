using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Splat;
using DeltaCompressionDotNet.MsDelta;
using System.ComponentModel;
using Squirrel.Bsdiff;
using SharpCompress.Archive;
using SharpCompress.Archive.Zip;
using SharpCompress.Writer;
using SharpCompress.Common;
using SharpCompress.Reader;
using SharpCompress.Writer;
using SharpCompress.Compressor.Deflate;

namespace Squirrel
{
    public interface IDeltaPackageBuilder
    {
        ReleasePackage CreateDeltaPackage(ReleasePackage basePackage, ReleasePackage newPackage, string outputFile);
        ReleasePackage ApplyDeltaPackage(ReleasePackage basePackage, ReleasePackage deltaPackage, string outputFile);
    }

    public class DeltaPackageBuilder : IEnableLogger, IDeltaPackageBuilder
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

                this.Log().Info("Extracting {0} and {1} into {2}", 
                    basePackage.ReleasePackageFile, newPackage.ReleasePackageFile, tempPath);

                using (var za = ZipArchive.Open(basePackage.ReleasePackageFile))
                using (var reader = za.ExtractAllEntries()) {
                    reader.WriteAllToDirectory(baseTempInfo.FullName);
                }

                using (var za = ZipArchive.Open(basePackage.ReleasePackageFile))
                using (var reader = za.ExtractAllEntries()) {
                    reader.WriteAllToDirectory(tempInfo.FullName);
                }

                // Collect a list of relative paths under 'lib' and map them
                // to their full name. We'll use this later to determine in
                // the new version of the package whether the file exists or
                // not.
                var baseLibFiles = baseTempInfo.GetAllFilesRecursively()
                    .Where(x => x.FullName.ToLowerInvariant().Contains("lib" + Path.DirectorySeparatorChar))
                    .ToDictionary(k => k.FullName.Replace(baseTempInfo.FullName, ""), v => v.FullName);

                var newLibDir = tempInfo.GetDirectories().First(x => x.Name.ToLowerInvariant() == "lib");

                foreach (var libFile in newLibDir.GetAllFilesRecursively()) {
                    createDeltaForSingleFile(libFile, tempInfo, baseLibFiles);
                }

                ReleasePackage.addDeltaFilesToContentTypes(tempInfo.FullName);
                using (var za = ZipArchive.Create()) {
                    za.AddAllFromDirectory(tempInfo.FullName);
                    za.SaveTo(outputFile, CompressionType.Deflate);
                }
            }

            return new ReleasePackage(outputFile);
        }

        public ReleasePackage ApplyDeltaPackage(ReleasePackage basePackage, ReleasePackage deltaPackage, string outputFile)
        {
            Contract.Requires(deltaPackage != null);
            Contract.Requires(!String.IsNullOrEmpty(outputFile) && !File.Exists(outputFile));

            string workingPath;
            string deltaPath;

            using (Utility.WithTempDirectory(out deltaPath, localAppDirectory))
            using (Utility.WithTempDirectory(out workingPath, localAppDirectory)) {
                using (var za = ZipArchive.Open(deltaPackage.InputPackageFile))
                using (var reader = za.ExtractAllEntries()) {
                    reader.WriteAllToDirectory(deltaPath);
                }
                using (var za = ZipArchive.Open(basePackage.InputPackageFile))
                using (var reader = za.ExtractAllEntries()) {
                    reader.WriteAllToDirectory(workingPath);
                }

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

                // Delete all of the files that were in the old package but
                // not in the new one.
                new DirectoryInfo(workingPath).GetAllFilesRecursively()
                    .Select(x => x.FullName.Replace(workingPath + Path.DirectorySeparatorChar, "").ToLowerInvariant())
                    .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase) && !pathsVisited.Contains(x))
                    .ForEach(x => {
                        this.Log().Info("{0} was in old package but not in new one, deleting", x);
                        File.Delete(Path.Combine(workingPath, x));
                    });

                // Update all the files that aren't in 'lib' with the delta
                // package's versions (i.e. the nuspec file, etc etc).
                deltaPathRelativePaths
                    .Where(x => !x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                    .ForEach(x => {
                        this.Log().Info("Updating metadata file: {0}", x);
                        File.Copy(Path.Combine(deltaPath, x), Path.Combine(workingPath, x), true);
                    });

                this.Log().Info("Repacking into full package: {0}", outputFile);
                using (var za = ZipArchive.Create()) {
                    za.AddAllFromDirectory(workingPath);
                    za.SaveTo(outputFile, new CompressionInfo() { DeflateCompressionLevel = CompressionLevel.BestSpeed, Type = CompressionType.Deflate });
                }
            }

            return new ReleasePackage(outputFile);
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
                this.Log().Info("{0} not found in base package, marking as new", relativePath);
                return;
            }

            var oldData = File.ReadAllBytes(baseFileListing[relativePath]);
            var newData = File.ReadAllBytes(targetFile.FullName);

            if (bytesAreIdentical(oldData, newData)) {
                this.Log().Info("{0} hasn't changed, writing dummy file", relativePath);

                File.Create(targetFile.FullName + ".diff").Dispose();
                File.Create(targetFile.FullName + ".shasum").Dispose();
                targetFile.Delete();
                return;
            }

            this.Log().Info("Delta patching {0} => {1}", baseFileListing[relativePath], targetFile.FullName);
            var msDelta = new MsDeltaCompression();
            try {
                msDelta.CreateDelta(baseFileListing[relativePath], targetFile.FullName, targetFile.FullName + ".diff");
            } catch (Exception) {
                this.Log().Warn("We couldn't create a delta for {0}, attempting to create bsdiff", targetFile.Name);

                var of = default(FileStream);
                try {
                    of = File.Create(targetFile.FullName + ".bsdiff");
                    BinaryPatchUtility.Create(oldData, newData, of);

                    // NB: Create a dummy corrupt .diff file so that older 
                    // versions which don't understand bsdiff will fail out
                    // until they get upgraded, instead of seeing the missing
                    // file and just removing it.
                    File.WriteAllText(targetFile.FullName + ".diff", "1");
                } catch (Exception ex) {
                    this.Log().WarnException(String.Format("We really couldn't create a delta for {0}", targetFile.Name), ex);
                    return;
                } finally {
                    if (of != null) of.Dispose();
                }
            }

            var rl = ReleaseEntry.GenerateFromFile(new MemoryStream(newData), targetFile.Name + ".shasum");
            File.WriteAllText(targetFile.FullName + ".shasum", rl.EntryAsString, Encoding.UTF8);
            targetFile.Delete();
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
                        this.Log().Info("Applying BSDiff to {0}", relativeFilePath);
                        BinaryPatchUtility.Apply(inf, () => File.OpenRead(inputFile), of);
                    }

                    verifyPatchedFile(relativeFilePath, inputFile, tempTargetFile);
                 } else if (relativeFilePath.EndsWith(".diff", StringComparison.InvariantCultureIgnoreCase)) {
                    this.Log().Info("Applying MSDiff to {0}", relativeFilePath);
                    var msDelta = new MsDeltaCompression();
                    msDelta.ApplyDelta(inputFile, finalTarget, tempTargetFile);

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
                if (File.Exists(tempTargetFile)) Utility.DeleteFileHarder(tempTargetFile, true);
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
                throw new ChecksumFailedException() {Filename = relativeFilePath};
            }

            if (expectedReleaseEntry.SHA1 != actualReleaseEntry.SHA1) {
                this.Log().Warn("Patched file {0} has incorrect SHA1, expected {1}, got {2}", relativeFilePath,
                    expectedReleaseEntry.SHA1, actualReleaseEntry.SHA1);
                throw new ChecksumFailedException() {Filename = relativeFilePath};
            }
        }

        bool bytesAreIdentical(byte[] oldData, byte[] newData)
        {
            if (oldData == null || newData == null) {
                return oldData == newData;
            }
            if (oldData.LongLength != newData.LongLength) {
                return false;
            }

            for(long i = 0; i < newData.LongLength; i++) {
                if (oldData[i] != newData[i]) {
                    return false;
                }
            }

            return true;
        }
    }
}
