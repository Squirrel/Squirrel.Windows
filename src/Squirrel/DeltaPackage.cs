using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Squirrel.SimpleSplat;
using System.ComponentModel;
using Squirrel.Bsdiff;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Writers;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Compressors.Deflate;

namespace Squirrel
{
    public class DeltaPackage : IEnableLogger
    {
        readonly string localAppDirectory;
        public DeltaPackage(string localAppDataOverride = null)
        {
            this.localAppDirectory = localAppDataOverride;
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
