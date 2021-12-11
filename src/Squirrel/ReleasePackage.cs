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
using Squirrel.MarkdownSharp;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;
using System.Threading.Tasks;
using SharpCompress.Archives.Zip;
using SharpCompress.Readers;

namespace Squirrel
{
    public interface IReleasePackage
    {
        string InputPackageFile { get; }
        string ReleasePackageFile { get; }
        string SuggestedReleaseFileName { get; }
    }

    public class ReleasePackage : IEnableLogger, IReleasePackage
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

        public SemanticVersion Version { get { return InputPackageFile.ToSemanticVersion(); } }

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

                        var parts = reader.Entry.Key.Split('\\', '/');
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
    }

    public class ChecksumFailedException : Exception
    {
        public string Filename { get; set; }
    }
}
