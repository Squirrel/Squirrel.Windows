using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NuGet;
using Squirrel.SimpleSplat;
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
        float? StagingPercentage { get; }

        string GetReleaseNotes(string packageDirectory);
        Uri GetIconUrl(string packageDirectory);
    }

    [DataContract]
    public class ReleaseEntry : IEnableLogger, IReleaseEntry
    {
        [DataMember] public string SHA1 { get; protected set; }
        [DataMember] public string BaseUrl { get; protected set; }
        [DataMember] public string Filename { get; protected set; }
        [DataMember] public string Query { get; protected set; }
        [DataMember] public long Filesize { get; protected set; }
        [DataMember] public bool IsDelta { get; protected set; }
        [DataMember] public float? StagingPercentage { get; protected set; }

        protected ReleaseEntry(string sha1, string filename, long filesize, bool isDelta, string baseUrl = null, string query = null, float? stagingPercentage = null)
        {
            Contract.Requires(sha1 != null && sha1.Length == 40);
            Contract.Requires(filename != null);
            Contract.Requires(filename.Contains(Path.DirectorySeparatorChar) == false);
            Contract.Requires(filesize > 0);

            SHA1 = sha1; BaseUrl = baseUrl;  Filename = filename; Query = query; Filesize = filesize; IsDelta = isDelta; StagingPercentage = stagingPercentage;
        }

        [IgnoreDataMember]
        public string EntryAsString {
            get {
                if (StagingPercentage != null) {
                    return String.Format("{0} {1}{2} {3} # {4}", SHA1, BaseUrl, Filename, Filesize, stagingPercentageAsString(StagingPercentage.Value));
                } else {
                    return String.Format("{0} {1}{2} {3}", SHA1, BaseUrl, Filename, Filesize);
                }
            }
        }

        [IgnoreDataMember]
        public SemanticVersion Version { get { return Filename.ToSemanticVersion(); } }

        static readonly Regex packageNameRegex = new Regex(@"^([\w-]+)-\d+\..+\.nupkg$");
        [IgnoreDataMember]
        public string PackageName {
            get {
                var match = packageNameRegex.Match(Filename);
                return match.Success ? 
                    match.Groups[1].Value : 
                    Filename.Substring(0, Filename.IndexOfAny(new[] { '-', '.' }));
            }
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
        static readonly Regex commentRegex = new Regex(@"\s*#.*$");
        static readonly Regex stagingRegex = new Regex(@"#\s+(\d{1,3})%$");
        public static ReleaseEntry ParseReleaseEntry(string entry)
        {
            Contract.Requires(entry != null);

            float? stagingPercentage = null;
            var m = stagingRegex.Match(entry);
            if (m != null && m.Success) {
                stagingPercentage = Single.Parse(m.Groups[1].Value) / 100.0f;
            }

            entry = commentRegex.Replace(entry, "");
            if (String.IsNullOrWhiteSpace(entry)) {
                return null;
            }

            m = entryRegex.Match(entry);
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
            string query = null;

            if(Utility.IsHttpUrl(filename)) {
                var uri = new Uri(filename);
                var path = uri.LocalPath;
                var authority = uri.GetLeftPart(UriPartial.Authority);

                if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(authority)) {
                    throw new Exception("Invalid URL");
                }

                var indexOfLastPathSeparator = path.LastIndexOf("/") + 1;
                baseUrl = authority + path.Substring(0, indexOfLastPathSeparator);
                filename = path.Substring(indexOfLastPathSeparator);

                if (!String.IsNullOrEmpty(uri.Query)) {
                    query = uri.Query;
                }
            }

            if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) > -1) {
                throw new Exception("Filename can either be an absolute HTTP[s] URL, *or* a file name");
            }

            long size = Int64.Parse(m.Groups[3].Value);
            bool isDelta = filenameIsDeltaFile(filename);

            return new ReleaseEntry(m.Groups[1].Value, filename, size, isDelta, baseUrl, query, stagingPercentage);
        }

        public bool IsStagingMatch(Guid? userId)
        {
            // A "Staging match" is when a user falls into the affirmative
            // bucket - i.e. if the staging is at 10%, this user is the one out
            // of ten case.
            if (!StagingPercentage.HasValue) return true;
            if (!userId.HasValue) return false;

            uint val = BitConverter.ToUInt32(userId.Value.ToByteArray(), 12);

            double percentage = ((double)val / (double)UInt32.MaxValue);
            return percentage < StagingPercentage.Value;
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

        public static IEnumerable<ReleaseEntry> ParseReleaseFileAndApplyStaging(string fileContents, Guid? userToken)
        {
            if (String.IsNullOrEmpty(fileContents)) {
                return new ReleaseEntry[0];
            }

            fileContents = Utility.RemoveByteOrderMarkerIfPresent(fileContents);

            var ret = fileContents.Split('\n')
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(ParseReleaseEntry)
                .Where(x => x != null && x.IsStagingMatch(userToken))
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

        static string stagingPercentageAsString(float percentage)
        {
            return String.Format("{0:F0}%", percentage * 100.0);
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
