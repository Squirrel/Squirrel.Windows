using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NuGet;
using Splat;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Squirrel
{
    public interface IReleaseEntry
    {
        string SHA1 { get; }
        string Filename { get; }
        long Filesize { get; }
        bool IsDelta { get; }
        string EntryAsString { get; }
        SemanticVersion Version { get; }
        string PackageName { get; }

        string GetReleaseNotes(string packageDirectory);
        Uri GetIconUrl(string packageDirectory);
    }

    [DataContract]
    public class ReleaseEntry : IEnableLogger, IReleaseEntry
    {
        [DataMember] public string SHA1 { get; protected set; }
        [DataMember] public string BaseUrl { get; protected set; }
        [DataMember] public string Filename { get; protected set; }
        [DataMember] public long Filesize { get; protected set; }
        [DataMember] public bool IsDelta { get; protected set; }

        protected ReleaseEntry(string sha1, string filename, long filesize, bool isDelta, string baseUrl = null)
        {
            Contract.Requires(sha1 != null && sha1.Length == 40);
            Contract.Requires(filename != null);
            Contract.Requires(filename.Contains(Path.DirectorySeparatorChar) == false);
            Contract.Requires(filesize > 0);

            SHA1 = sha1; BaseUrl = baseUrl;  Filename = filename; Filesize = filesize; IsDelta = isDelta;
        }

        [IgnoreDataMember]
        public string EntryAsString {
            get { return String.Format("{0} {1}{2} {3}", SHA1, BaseUrl, Filename, Filesize); }
        }

        [IgnoreDataMember]
        public SemanticVersion Version { get { return Filename.ToSemanticVersion(); } }

        [IgnoreDataMember]
        public string PackageName {
            get { return Filename.Substring(0, Filename.IndexOfAny(new[] { '-', '.' })); }
        }

        public string GetReleaseNotes(string packageDirectory)
        {
            var zp = new ZipPackage(Path.Combine(packageDirectory, Filename));
            var t = zp.Id;

            if (String.IsNullOrWhiteSpace(zp.ReleaseNotes)) {
                throw new Exception(String.Format("Invalid 'ReleaseNotes' value in nuspec file at '{0}'", Path.Combine(packageDirectory, Filename)));
            }

            return zp.ReleaseNotes;
        }

        public Uri GetIconUrl(string packageDirectory)
        {
            var zp = new ZipPackage(Path.Combine(packageDirectory, Filename));
            return zp.IconUrl;
        }

        static readonly Regex entryRegex = new Regex(@"^([0-9a-fA-F]{40})\s+(\S+)\s+(\d+)[\r]*$");
        static readonly Regex commentRegex = new Regex(@"#.*$");
        public static ReleaseEntry ParseReleaseEntry(string entry)
        {
            Contract.Requires(entry != null);

            entry = commentRegex.Replace(entry, "");
            if (String.IsNullOrWhiteSpace(entry)) {
                return null;
            }

            var m = entryRegex.Match(entry);
            if (!m.Success) {
                throw new Exception("Invalid release entry: " + entry);
            }

            if (m.Groups.Count != 4) {
                throw new Exception("Invalid release entry: " + entry);
            }

            string filename = m.Groups[2].Value;

            // Split the base URL and the filename if an URI is provided, 
            // throws if a path is provided
            string baseUrl = null;

            if(Utility.IsHttpUrl(filename)) {
                var indexOfLastPathSeparator = filename.LastIndexOf("/") + 1;

                baseUrl = filename.Substring(0, indexOfLastPathSeparator);
                filename = filename.Substring(indexOfLastPathSeparator);
            } 
            
            if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) > -1) {
                throw new Exception("Filename can either be an absolute HTTP[s] URL, *or* a file name");
            }

            long size = Int64.Parse(m.Groups[3].Value);
            bool isDelta = filenameIsDeltaFile(filename);

            return new ReleaseEntry(m.Groups[1].Value, filename, size, isDelta, baseUrl);
        }

        public static IEnumerable<ReleaseEntry> ParseReleaseFile(string fileContents)
        {
            if (String.IsNullOrEmpty(fileContents)) {
                return new ReleaseEntry[0];
            }

            fileContents = Utility.RemoveByteOrderMarkerIfPresent(fileContents);

            var ret = fileContents.Split('\n')
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(ParseReleaseEntry)
                .Where(x => x != null)
                .ToArray();

            return ret.Any(x => x == null) ? null : ret;
        }

        public static void WriteReleaseFile(IEnumerable<ReleaseEntry> releaseEntries, Stream stream)
        {
            Contract.Requires(releaseEntries != null && releaseEntries.Any());
            Contract.Requires(stream != null);

            using (var sw = new StreamWriter(stream, Encoding.UTF8)) {
                sw.Write(String.Join("\n", releaseEntries
                    .OrderBy(x => x.Version)
                    .ThenByDescending(x => x.IsDelta)
                    .Select(x => x.EntryAsString)));
            }
        }

        public static void WriteReleaseFile(IEnumerable<ReleaseEntry> releaseEntries, string path)
        {
            Contract.Requires(releaseEntries != null && releaseEntries.Any());
            Contract.Requires(!String.IsNullOrEmpty(path));

            using (var f = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                WriteReleaseFile(releaseEntries, f);
            }
        }

        public static ReleaseEntry GenerateFromFile(Stream file, string filename, string baseUrl = null)
        {
            Contract.Requires(file != null && file.CanRead);
            Contract.Requires(!String.IsNullOrEmpty(filename));

            var hash = Utility.CalculateStreamSHA1(file);
            return new ReleaseEntry(hash, filename, file.Length, filenameIsDeltaFile(filename), baseUrl);
        }

        public static ReleaseEntry GenerateFromFile(string path, string baseUrl = null)
        {
            using (var inf = File.OpenRead(path)) {
                return GenerateFromFile(inf, Path.GetFileName(path), baseUrl);
            }
        }

        public static List<ReleaseEntry> BuildReleasesFile(string releasePackagesDir)
        {
            var packagesDir = new DirectoryInfo(releasePackagesDir);

            // Generate release entries for all of the local packages
            var entriesQueue = new ConcurrentQueue<ReleaseEntry>();
            Parallel.ForEach(packagesDir.GetFiles("*.nupkg"), x => {
                using (var file = x.OpenRead()) {
                    entriesQueue.Enqueue(GenerateFromFile(file, x.Name));
                }
            });

            // Write the new RELEASES file to a temp file then move it into
            // place
            var entries = entriesQueue.ToList();
            var tempFile = default(string);
            Utility.WithTempFile(out tempFile, releasePackagesDir);

            try {
                using (var of = File.OpenWrite(tempFile)) {
                    if (entries.Count > 0) WriteReleaseFile(entries, of);
                }

                var target = Path.Combine(packagesDir.FullName, "RELEASES");
                if (File.Exists(target)) {
                    File.Delete(target);
                }

                File.Move(tempFile, target);
            } finally {
                if (File.Exists(tempFile)) Utility.DeleteFileHarder(tempFile, true);
            }

            return entries;
        }

        static bool filenameIsDeltaFile(string filename)
        {
            return filename.EndsWith("-delta.nupkg", StringComparison.InvariantCultureIgnoreCase);
        }

        public static ReleasePackage GetPreviousRelease(IEnumerable<ReleaseEntry> releaseEntries, IReleasePackage package, string targetDir)
        {
            if (releaseEntries == null || !releaseEntries.Any()) return null;

            return releaseEntries
                .Where(x => x.IsDelta == false)
                .Where(x => x.Version < package.ToSemanticVersion())
                .OrderByDescending(x => x.Version)
                .Select(x => new ReleasePackage(Path.Combine(targetDir, x.Filename), true))
                .FirstOrDefault();
        }
    }
}
