using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        public static void CreateZipFromDirectory(string outputFile, string directoryToCompress, bool nestDirectory = false, CompressionLevel level = CompressionLevel.BestSpeed)
        {
            Log.Info($"Compressing '{directoryToCompress}' to '{outputFile}' using SharpCompress (DEFLATE)...");
            using var archive = ZipArchive.Create();
            archive.DeflateCompressionLevel = level;
            if (nestDirectory) {
                AddAllFromDirectoryInNestedDir(archive, directoryToCompress);
            } else {
                archive.AddAllFromDirectory(directoryToCompress);
            }
            archive.SaveTo(outputFile, level == CompressionLevel.None ? CompressionType.None : CompressionType.Deflate);
        }
        
        private static void AddAllFromDirectoryInNestedDir(
            IWritableArchive writableArchive,
            string filePath, string searchPattern = "*.*", SearchOption searchOption = SearchOption.AllDirectories)
        {
            var di = new DirectoryInfo(filePath);
            var parent = di.Parent;
            
            using (writableArchive.PauseEntryRebuilding())
            {
                foreach (var path in Directory.EnumerateFiles(filePath, searchPattern, searchOption))
                {
                    var fileInfo = new FileInfo(path);
                    writableArchive.AddEntry(fileInfo.FullName.Substring(parent.FullName.Length), fileInfo.OpenRead(), true, fileInfo.Length,
                        fileInfo.LastWriteTime);
                }
            }
        }
    }
}
