using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    internal class EasyZip
    {
        private static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(EasyZip));

        public static void ExtractZipToDirectory(string inputFile, string outputDirectory)
        {
            // 7z is faster, use it if it's available
            if (SquirrelRuntimeInfo.IsWindows && Extract7z(inputFile, outputDirectory))
                return;

            Log.Info($"Extracting '{inputFile}' to '{outputDirectory}' using SharpCompress...");
            using var archive = ZipArchive.Open(inputFile);
            archive.WriteToDirectory(outputDirectory, new() {
                PreserveFileTime = false,
                Overwrite = true,
                ExtractFullPath = true
            });
        }

        public static void CreateZipFromDirectory(string outputFile, string directoryToCompress)
        {
            // 7z is faster, use it if it's available
            if (SquirrelRuntimeInfo.IsWindows && Compress7z(outputFile, directoryToCompress))
                return;

            Log.Info($"Compressing '{directoryToCompress}' to '{outputFile}' using SharpCompress...");
            using var archive = ZipArchive.Create();
            archive.DeflateCompressionLevel = CompressionLevel.BestSpeed;
            archive.AddAllFromDirectory(directoryToCompress);
            archive.SaveTo(outputFile, CompressionType.Deflate);
        }

        private static bool Extract7z(string zipFilePath, string outFolder)
        {
            Log.Info($"Extracting '{zipFilePath}' to '{outFolder}' using 7z...");
            try {
                var args = String.Format("x \"{0}\" -tzip -mmt on -aoa -y -o\"{1}\" *", zipFilePath, outFolder);
                var psi = Utility.CreateProcessStartInfo(HelperExe.SevenZipPath, args);
                var result = Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                if (result.ExitCode != 0) throw new Exception(result.StdOutput);
                return true;
            } catch (Exception ex) {
                Log.Warn("Unable to extract archive with 7z.exe\n" + ex.Message);
                return false;
            }
        }

        private static bool Compress7z(string zipFilePath, string inFolder)
        {
            Log.Info($"Compressing '{inFolder}' to '{zipFilePath}' using 7z...");
            try {
                var args = String.Format("a \"{0}\" -tzip -aoa -y -mmt on *", zipFilePath);
                var psi = Utility.CreateProcessStartInfo(HelperExe.SevenZipPath, args, inFolder);
                var result = Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                if (result.ExitCode != 0) throw new Exception(result.StdOutput);
                return true;
            } catch (Exception ex) {
                Log.Warn("Unable to create archive with 7z.exe\n" + ex.Message);
                return false;
            }
        }
    }
}
