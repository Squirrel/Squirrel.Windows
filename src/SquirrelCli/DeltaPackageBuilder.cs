using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squirrel;
using Squirrel.Bsdiff;
using Squirrel.SimpleSplat;

namespace SquirrelCli
{
    internal class DeltaPackageBuilder : IEnableLogger
    {
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

                HelperExe.ExtractZipToDirectory(basePackage.ReleasePackageFile, baseTempInfo.FullName).Wait();
                HelperExe.ExtractZipToDirectory(newPackage.ReleasePackageFile, tempInfo.FullName).Wait();

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

                ReleasePackageBuilder.addDeltaFilesToContentTypes(tempInfo.FullName);
                HelperExe.CreateZipFromDirectory(outputFile, tempInfo.FullName).Wait();
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

            if (targetFile.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                targetFile.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                targetFile.Extension.Equals(".node", StringComparison.OrdinalIgnoreCase)) {
                try {
                    msDelta.CreateDelta(baseFileListing[relativePath], targetFile.FullName, targetFile.FullName + ".diff");
                    goto exit;
                } catch (Exception) {
                    this.Log().Warn("We couldn't create a delta for {0}, attempting to create bsdiff", targetFile.Name);
                }
            }

            try {
                using (FileStream of = File.Create(targetFile.FullName + ".bsdiff")) {
                    BinaryPatchUtility.Create(oldData, newData, of);

                    // NB: Create a dummy corrupt .diff file so that older 
                    // versions which don't understand bsdiff will fail out
                    // until they get upgraded, instead of seeing the missing
                    // file and just removing it.
                    File.WriteAllText(targetFile.FullName + ".diff", "1");
                }
            } catch (Exception ex) {
                this.Log().WarnException(String.Format("We really couldn't create a delta for {0}", targetFile.Name), ex);

                Utility.DeleteFileHarder(targetFile.FullName + ".bsdiff", true);
                Utility.DeleteFileHarder(targetFile.FullName + ".diff", true);
                return;
            }

        exit:

            var rl = ReleaseEntry.GenerateFromFile(new MemoryStream(newData), targetFile.Name + ".shasum");
            File.WriteAllText(targetFile.FullName + ".shasum", rl.EntryAsString, Encoding.UTF8);
            targetFile.Delete();
        }

        static bool bytesAreIdentical(byte[] oldData, byte[] newData)
        {
            if (oldData == null || newData == null) {
                return oldData == newData;
            }
            if (oldData.LongLength != newData.LongLength) {
                return false;
            }

            for (long i = 0; i < newData.LongLength; i++) {
                if (oldData[i] != newData[i]) {
                    return false;
                }
            }

            return true;
        }
    }
}
