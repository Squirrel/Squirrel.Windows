using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Writers;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    internal class EasyZip
    {
        private static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(EasyZip));

        public static void ExtractZipToDirectory(string inputFile, string outputDirectory)
        {
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
            // 7z is much faster, and produces much better compression results
            // so we will use it if it is available
            if (Compress7z(outputFile, directoryToCompress).GetAwaiter().GetResult())
                return;

            Log.Info($"Compressing '{directoryToCompress}' to '{outputFile}' using SharpCompress...");
            using var archive = ZipArchive.Create();
            archive.DeflateCompressionLevel = CompressionLevel.BestSpeed;
            archive.AddAllFromDirectory(directoryToCompress);
            archive.SaveTo(outputFile, CompressionType.Deflate);
        }

        private static string _7zPath;

        private static async Task<string> Get7zPath()
        {
            if (_7zPath != null) return _7zPath;

            var findCommand = SquirrelRuntimeInfo.IsWindows ? "where" : "which";

            // search for the 7z or 7za on the path
            var result = await Utility.InvokeProcessUnsafeAsync(Utility.CreateProcessStartInfo(findCommand, "7z"), CancellationToken.None).ConfigureAwait(false);
            if (result.ExitCode == 0) {
                _7zPath = "7z";
                return _7zPath;
            }

            result = await Utility.InvokeProcessUnsafeAsync(Utility.CreateProcessStartInfo(findCommand, "7za"), CancellationToken.None).ConfigureAwait(false);
            if (result.ExitCode == 0) {
                _7zPath = "7za";
                return _7zPath;
            }

            // we only bundle the windows version currently
            if (SquirrelRuntimeInfo.IsWindows) {
                _7zPath = HelperExe.SevenZipPath;
                return _7zPath;
            }

            return null;
        }

        private static async Task<bool> Compress7z(string zipFilePath, string inFolder)
        {
            var path = await Get7zPath();

            if (path == null) {
                Log.Warn("7z not found on path. Will fallback to SharpCompress.");
                return false;
            }

            Log.Info($"Compressing '{inFolder}' to '{zipFilePath}' using 7z (LZMA)...");
            try {
                var args = String.Format("a \"{0}\" -tzip -m0=LZMA -aoa -y *", zipFilePath);
                var psi = Utility.CreateProcessStartInfo(path, args, inFolder);
                var result = await Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None).ConfigureAwait(false);
                if (result.ExitCode != 0) throw new Exception(result.StdOutput);
                return true;
            } catch (Exception ex) {
                Log.Warn("Unable to create archive with 7z.exe\n" + ex.Message);
                return false;
            }
        }
    }
}
