using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Squirrel.SimpleSplat;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using Squirrel.NuGet;
using System.Runtime.Versioning;

namespace Squirrel
{
    internal static class Utility
    {
        public static string RemoveByteOrderMarkerIfPresent(string content)
        {
            return string.IsNullOrEmpty(content)
                ? string.Empty
                : RemoveByteOrderMarkerIfPresent(Encoding.UTF8.GetBytes(content));
        }

        public static string RemoveByteOrderMarkerIfPresent(byte[] content)
        {
            byte[] output = { };

            if (content == null) {
                goto done;
            }

            Func<byte[], byte[], bool> matches = (bom, src) => {
                if (src.Length < bom.Length) return false;

                return !bom.Where((chr, index) => src[index] != chr).Any();
            };

            var utf32Be = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
            var utf32Le = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
            var utf16Be = new byte[] { 0xFE, 0xFF };
            var utf16Le = new byte[] { 0xFF, 0xFE };
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF };

            if (matches(utf32Be, content)) {
                output = new byte[content.Length - utf32Be.Length];
            } else if (matches(utf32Le, content)) {
                output = new byte[content.Length - utf32Le.Length];
            } else if (matches(utf16Be, content)) {
                output = new byte[content.Length - utf16Be.Length];
            } else if (matches(utf16Le, content)) {
                output = new byte[content.Length - utf16Le.Length];
            } else if (matches(utf8, content)) {
                output = new byte[content.Length - utf8.Length];
            } else {
                output = content;
            }

            done:
            if (output.Length > 0) {
                Buffer.BlockCopy(content, content.Length - output.Length, output, 0, output.Length);
            }

            return Encoding.UTF8.GetString(output);
        }

        public static bool TryParseEnumU16<TEnum>(ushort enumValue, out TEnum retVal)
        {
            retVal = default(TEnum);
            bool success = Enum.IsDefined(typeof(TEnum), enumValue);
            if (success) {
                retVal = (TEnum) Enum.ToObject(typeof(TEnum), enumValue);
            }

            return success;
        }

        public static bool FullPathEquals(string path1, string path2)
        {
            return NormalizePath(path1).Equals(NormalizePath(path2), SquirrelRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartEquals(string part1, string part2)
        {
            return part1.Equals(part2, SquirrelRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartStartsWith(string part1, string startsWith)
        {
            return part1.StartsWith(startsWith, SquirrelRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartEndsWith(string part1, string endsWith)
        {
            return part1.EndsWith(endsWith, SquirrelRuntimeInfo.PathStringComparison);
        }

        public static bool FileHasExtension(string filePath, string extension)
        {
            var ext = Path.GetExtension(filePath);
            if (!extension.StartsWith(".")) extension = "." + extension;
            return PathPartEquals(ext, extension);
        }

        public static string NormalizePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var normalized = new Uri(fullPath, UriKind.Absolute).LocalPath;
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool IsFileInDirectory(string file, string directory)
        {
            var normalizedDir = NormalizePath(directory) + Path.DirectorySeparatorChar;
            var normalizedFile = NormalizePath(file);
            return normalizedFile.StartsWith(normalizedDir, SquirrelRuntimeInfo.PathStringComparison);
        }

        public static IEnumerable<FileInfo> GetAllFilesRecursively(this DirectoryInfo rootPath)
        {
            Contract.Requires(rootPath != null);

            return rootPath.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        public static IEnumerable<string> GetAllFilePathsRecursively(string rootPath)
        {
            Contract.Requires(rootPath != null);

            return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
        }

        public static string CalculateFileSHA1(string filePath)
        {
            Contract.Requires(filePath != null);

            using (var stream = File.OpenRead(filePath)) {
                return CalculateStreamSHA1(stream);
            }
        }

        public static string CalculateStreamSHA1(Stream file)
        {
            Contract.Requires(file != null && file.CanRead);

            using (var sha1 = SHA1.Create()) {
                return BitConverter.ToString(sha1.ComputeHash(file)).Replace("-", String.Empty);
            }
        }

        public static Sources.IFileDownloader CreateDefaultDownloader()
        {
            return new Sources.HttpClientFileDownloader();
        }

        public static async Task CopyToAsync(string from, string to)
        {
            Contract.Requires(!String.IsNullOrEmpty(from) && File.Exists(from));
            Contract.Requires(!String.IsNullOrEmpty(to));

            if (!File.Exists(from)) {
                Log().Warn("The file {0} does not exist", from);

                // TODO: should we fail this operation?
                return;
            }

            // XXX: SafeCopy
            await Task.Run(() => File.Copy(from, to, true)).ConfigureAwait(false);
        }

        public static void Retry(this Action block, int retries = 4, int retryDelay = 250)
        {
            Contract.Requires(retries > 0);

            Func<object> thunk = () => {
                block();
                return null;
            };

            thunk.Retry(retries, retryDelay);
        }

        public static T Retry<T>(this Func<T> block, int retries = 4, int retryDelay = 250)
        {
            Contract.Requires(retries > 0);

            while (true) {
                try {
                    T ret = block();
                    return ret;
                } catch (Exception) {
                    if (retries == 0) {
                        throw;
                    }

                    retries--;
                    Thread.Sleep(retryDelay);
                }
            }
        }

        public static async Task RetryAsync(this Func<Task> block, int retries = 4, int retryDelay = 250)
        {
            while (true) {
                try {
                    await block().ConfigureAwait(false);
                } catch {
                    if (retries-- == 0) throw;
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                }
            }
        }

        public static async Task<T> RetryAsync<T>(this Func<Task<T>> block, int retries = 4, int retryDelay = 250)
        {
            while (true) {
                try {
                    return await block().ConfigureAwait(false);
                } catch {
                    if (retries-- == 0) throw;
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                }
            }
        }


        public static T GetAwaiterResult<T>(this Task<T> task)
        {
            return task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public static void GetAwaiterResult(this Task task)
        {
            task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public static Task ForEachAsync<T>(this IEnumerable<T> source, Action<T> body, int degreeOfParallelism = 4)
        {
            return ForEachAsync(source, x => Task.Run(() => body(x)), degreeOfParallelism);
        }

        public static Task ForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> body, int degreeOfParallelism = 4)
        {
            return Task.WhenAll(
                from partition in Partitioner.Create(source).GetPartitions(degreeOfParallelism)
                select Task.Run(async () => {
                    using (partition)
                        while (partition.MoveNext())
                            await body(partition.Current).ConfigureAwait(false);
                }));
        }

        public static string GetDefaultTempBaseDirectory()
        {
            string tempDir;
            
            if (SquirrelRuntimeInfo.IsOSX) {
                tempDir = "/tmp/clowd.squirrel";
            } else if (SquirrelRuntimeInfo.IsWindows) {
                tempDir = Path.Combine(Path.GetTempPath(), "Clowd.Squirrel");
            } else {
                throw new NotSupportedException();
            }

            var di = new DirectoryInfo(tempDir);
            if (!di.Exists) di.Create();

            return di.FullName;
        }

        private static string GetNextTempName(string tempDir)
        {
            for (int i = 1; i < 10000; i++) {
                string name = "temp." + i;
                var target = Path.Combine(tempDir, name);

                FileSystemInfo info = null;
                if (Directory.Exists(target)) info = new DirectoryInfo(target);
                else if (File.Exists(target)) info = new FileInfo(target);

                // this dir/file does not exist, lets use it.
                if (info == null) {
                    return target;
                }

                // this dir/file exists, but it is old, let's re-use it.
                // this shouldn't generally happen, but crashes do exist.
                if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(1)) {
                    if (DeleteFileOrDirectoryHard(target, false, true)) {
                        // the dir/file was deleted successfully.
                        return target;
                    }
                }
            }

            throw new Exception(
                "Unable to find free temp path. Has the temp directory exceeded it's maximum number of items? (10000)");
        }

        public static IDisposable GetTempDirectory(out string newTempDirectory)
        {
            return GetTempDirectory(out newTempDirectory, GetDefaultTempBaseDirectory());
        }

        public static IDisposable GetTempDirectory(out string newTempDirectory, string rootTempDir)
        {
            var disp = GetTempFileName(out newTempDirectory, rootTempDir);
            Directory.CreateDirectory(newTempDirectory);
            return disp;
        }

        public static IDisposable GetTempFileName(out string newTempFile)
        {
            return GetTempFileName(out newTempFile, GetDefaultTempBaseDirectory());
        }

        public static IDisposable GetTempFileName(out string newTempFile, string rootTempDir)
        {
            var path = GetNextTempName(rootTempDir);
            newTempFile = path;
            return Disposable.Create(() => DeleteFileOrDirectoryHard(path, throwOnFailure: false));
        }

        /// <summary>
        /// Repeatedly tries various methods to delete a file system object. Optionally renames the directory first.
        /// Optionally ignores errors.
        /// </summary>
        /// <param name="path">The path of the file system entity to delete.</param>
        /// <param name="throwOnFailure">Whether this function should throw if the delete fails.</param>
        /// <param name="renameFirst">Try to rename this object first before deleting. Can help prevent partial delete of folders.</param>
        /// <returns>True if the file system object was deleted, false otherwise.</returns>
        public static bool DeleteFileOrDirectoryHard(string path, bool throwOnFailure = true, bool renameFirst = false)
        {
            Contract.Requires(!String.IsNullOrEmpty(path));
            Log().Debug("Starting to delete: {0}", path);

            try {
                if (File.Exists(path)) {
                    DeleteFsiVeryHard(new FileInfo(path));
                } else if (Directory.Exists(path)) {
                    if (renameFirst) {
                        // if there are locked files in a directory, we will not attempt to delte it
                        var oldPath = path + ".old";
                        Directory.Move(path, oldPath);
                        path = oldPath;
                    }

                    DeleteFsiTree(new DirectoryInfo(path));
                } else {
                    if (throwOnFailure)
                        Log().Warn($"Cannot delete '{path}' if it does not exist.");
                }

                return true;
            } catch (Exception ex) {
                Log().ErrorException($"Unable to delete '{path}'", ex);
                if (throwOnFailure)
                    throw;
                return false;
            }
        }

        private static void DeleteFsiTree(FileSystemInfo fileSystemInfo)
        {
            // if junction / symlink, don't iterate, just delete it.
            if (fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                DeleteFsiVeryHard(fileSystemInfo);
                return;
            }

            // recursively delete children
            try {
                var directoryInfo = fileSystemInfo as DirectoryInfo;
                if (directoryInfo != null) {
                    foreach (FileSystemInfo childInfo in directoryInfo.GetFileSystemInfos()) {
                        DeleteFsiTree(childInfo);
                    }
                }
            } catch (Exception ex) {
                Log().WarnException($"Unable to traverse children of '{fileSystemInfo.FullName}'", ex);
            }

            // finally, delete myself, we should try this even if deleting children failed
            // because Directory.Delete can also be recursive
            DeleteFsiVeryHard(fileSystemInfo);
        }

        private static void DeleteFsiVeryHard(FileSystemInfo fileSystemInfo)
        {
            // don't try to delete the running process
            if (FullPathEquals(fileSystemInfo.FullName, SquirrelRuntimeInfo.EntryExePath))
                return;

            // try to remove "ReadOnly" attributes
            try { fileSystemInfo.Attributes = FileAttributes.Normal; } catch { }

            try { fileSystemInfo.Refresh(); } catch { }

            // use this instead of fsi.Delete() because it is more resilient/aggressive
            Action deleteMe = fileSystemInfo is DirectoryInfo
                ? () => Directory.Delete(fileSystemInfo.FullName, true)
                : () => File.Delete(fileSystemInfo.FullName);

            // retry a few times. if a directory in this tree is open in Windows Explorer,
            // it might be locked for a little while WE cleans up handles
            try {
                Retry(() => {
                    try {
                        deleteMe();
                    } catch (DirectoryNotFoundException) {
                        return; // good!
                    }
                }, retries: 4, retryDelay: 50);
            } catch (Exception ex) {
                Log().WarnException($"Unable to delete child '{fileSystemInfo.FullName}'", ex);
                throw;
            }
        }

        //public static string PackageDirectoryForAppDir(string rootAppDirectory)
        //{
        //    return Path.Combine(rootAppDirectory, "packages");
        //}

        //public static string LocalReleaseFileForAppDir(string rootAppDirectory)
        //{
        //    return Path.Combine(PackageDirectoryForAppDir(rootAppDirectory), "RELEASES");
        //}

        public static IEnumerable<ReleaseEntry> LoadLocalReleases(string localReleaseFile)
        {
            var file = File.OpenRead(localReleaseFile);

            // NB: sr disposes file
            using (var sr = new StreamReader(file, Encoding.UTF8)) {
                return ReleaseEntry.ParseReleaseFile(sr.ReadToEnd());
            }
        }

        public static ReleaseEntry FindCurrentVersion(IEnumerable<ReleaseEntry> localReleases)
        {
            if (!localReleases.Any()) {
                return null;
            }

            return localReleases.OrderByDescending(x => x.Version).FirstOrDefault(x => !x.IsDelta);
        }

        public static string GetAppUserModelId(string packageId, string exeName)
        {
            return String.Format("com.squirrel.{0}.{1}", packageId.Replace(" ", ""),
                exeName.Replace(".exe", "").Replace(" ", ""));
        }

        public static bool IsHttpUrl(string urlOrPath)
        {
            var uri = default(Uri);
            if (!Uri.TryCreate(urlOrPath, UriKind.Absolute, out uri)) {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        public static Uri AppendPathToUri(Uri uri, string path)
        {
            var builder = new UriBuilder(uri);
            if (!builder.Path.EndsWith("/")) {
                builder.Path += "/";
            }

            builder.Path += path;
            return builder.Uri;
        }

        public static Uri EnsureTrailingSlash(Uri uri)
        {
            return AppendPathToUri(uri, "");
        }

        public static Uri AddQueryParamsToUri(Uri uri, IEnumerable<KeyValuePair<string, string>> newQuery)
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            foreach (var entry in newQuery) {
                query[entry.Key] = entry.Value;
            }

            var builder = new UriBuilder(uri);
            builder.Query = query.ToString();

            return builder.Uri;
        }

        readonly static string[] peExtensions = new[] { ".exe", ".dll", ".node" };

        public static bool FileIsLikelyPEImage(string name)
        {
            var ext = Path.GetExtension(name);
            return peExtensions.Any(x => ext.Equals(x, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsFileTopLevelInPackage(string fullName, string pkgPath)
        {
            var fn = fullName.ToLowerInvariant();
            var pkg = pkgPath.ToLowerInvariant();
            var relativePath = fn.Replace(pkg, "");

            // NB: We want to match things like `/lib/net45/foo.exe` but not `/lib/net45/bar/foo.exe`
            return relativePath.Split(Path.DirectorySeparatorChar).Length == 4;
        }

        public static void LogIfThrows(this IFullLogger This, LogLevel level, string message, Action block)
        {
            try {
                block();
            } catch (Exception ex) {
                switch (level) {
                case LogLevel.Debug:
                    This.DebugException(message ?? "", ex);
                    break;
                case LogLevel.Info:
                    This.InfoException(message ?? "", ex);
                    break;
                case LogLevel.Warn:
                    This.WarnException(message ?? "", ex);
                    break;
                case LogLevel.Error:
                    This.ErrorException(message ?? "", ex);
                    break;
                }

                throw;
            }
        }

        public static async Task LogIfThrows(this IFullLogger This, LogLevel level, string message, Func<Task> block)
        {
            try {
                await block().ConfigureAwait(false);
            } catch (Exception ex) {
                switch (level) {
                case LogLevel.Debug:
                    This.DebugException(message ?? "", ex);
                    break;
                case LogLevel.Info:
                    This.InfoException(message ?? "", ex);
                    break;
                case LogLevel.Warn:
                    This.WarnException(message ?? "", ex);
                    break;
                case LogLevel.Error:
                    This.ErrorException(message ?? "", ex);
                    break;
                }

                throw;
            }
        }

        public static async Task<T> LogIfThrows<T>(this IFullLogger This, LogLevel level, string message, Func<Task<T>> block)
        {
            try {
                return await block().ConfigureAwait(false);
            } catch (Exception ex) {
                switch (level) {
                case LogLevel.Debug:
                    This.DebugException(message ?? "", ex);
                    break;
                case LogLevel.Info:
                    This.InfoException(message ?? "", ex);
                    break;
                case LogLevel.Warn:
                    This.WarnException(message ?? "", ex);
                    break;
                case LogLevel.Error:
                    This.ErrorException(message ?? "", ex);
                    break;
                }

                throw;
            }
        }

        public static void WarnIfThrows(this IEnableLogger This, Action block, string message = null)
        {
            This.Log().LogIfThrows(LogLevel.Warn, message, block);
        }

        public static Task WarnIfThrows(this IEnableLogger This, Func<Task> block, string message = null)
        {
            return This.Log().LogIfThrows(LogLevel.Warn, message, block);
        }

        public static Task<T> WarnIfThrows<T>(this IEnableLogger This, Func<Task<T>> block, string message = null)
        {
            return This.Log().LogIfThrows(LogLevel.Warn, message, block);
        }

        public static void ErrorIfThrows(this IEnableLogger This, Action block, string message = null)
        {
            This.Log().LogIfThrows(LogLevel.Error, message, block);
        }

        public static Task ErrorIfThrows(this IEnableLogger This, Func<Task> block, string message = null)
        {
            return This.Log().LogIfThrows(LogLevel.Error, message, block);
        }

        public static Task<T> ErrorIfThrows<T>(this IEnableLogger This, Func<Task<T>> block, string message = null)
        {
            return This.Log().LogIfThrows(LogLevel.Error, message, block);
        }

        public static void WarnIfThrows(this IFullLogger This, Action block, string message = null)
        {
            This.LogIfThrows(LogLevel.Warn, message, block);
        }

        public static Task WarnIfThrows(this IFullLogger This, Func<Task> block, string message = null)
        {
            return This.LogIfThrows(LogLevel.Warn, message, block);
        }

        public static Task<T> WarnIfThrows<T>(this IFullLogger This, Func<Task<T>> block, string message = null)
        {
            return This.LogIfThrows(LogLevel.Warn, message, block);
        }

        public static void ErrorIfThrows(this IFullLogger This, Action block, string message = null)
        {
            This.LogIfThrows(LogLevel.Error, message, block);
        }

        public static Task ErrorIfThrows(this IFullLogger This, Func<Task> block, string message = null)
        {
            return This.LogIfThrows(LogLevel.Error, message, block);
        }

        public static Task<T> ErrorIfThrows<T>(this IFullLogger This, Func<Task<T>> block, string message = null)
        {
            return This.LogIfThrows(LogLevel.Error, message, block);
        }

        public static void ConsoleWriteWithColor(string text, ConsoleColor color)
        {
            var fc = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = fc;
        }

        static IFullLogger logger;

        static IFullLogger Log()
        {
            return logger ??
                   (logger = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(Utility)));
        }

        public static Guid CreateGuidFromHash(string text)
        {
            return CreateGuidFromHash(text, Utility.IsoOidNamespace);
        }

        public static Guid CreateGuidFromHash(byte[] data)
        {
            return CreateGuidFromHash(data, Utility.IsoOidNamespace);
        }

        public static Guid CreateGuidFromHash(string text, Guid namespaceId)
        {
            return CreateGuidFromHash(Encoding.UTF8.GetBytes(text), namespaceId);
        }

        public static Guid CreateGuidFromHash(byte[] nameBytes, Guid namespaceId)
        {
            // convert the namespace UUID to network order (step 3)
            byte[] namespaceBytes = namespaceId.ToByteArray();
            SwapByteOrder(namespaceBytes);

            // comput the hash of the name space ID concatenated with the 
            // name (step 4)
            byte[] hash;
            using (var algorithm = SHA1.Create()) {
                algorithm.TransformBlock(namespaceBytes, 0, namespaceBytes.Length, null, 0);
                algorithm.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
                hash = algorithm.Hash;
            }

            // most bytes from the hash are copied straight to the bytes of 
            // the new GUID (steps 5-7, 9, 11-12)
            var newGuid = new byte[16];
            Array.Copy(hash, 0, newGuid, 0, 16);

            // set the four most significant bits (bits 12 through 15) of 
            // the time_hi_and_version field to the appropriate 4-bit 
            // version number from Section 4.1.3 (step 8)
            newGuid[6] = (byte) ((newGuid[6] & 0x0F) | (5 << 4));

            // set the two most significant bits (bits 6 and 7) of the 
            // clock_seq_hi_and_reserved to zero and one, respectively 
            // (step 10)
            newGuid[8] = (byte) ((newGuid[8] & 0x3F) | 0x80);

            // convert the resulting UUID to local byte order (step 13)
            SwapByteOrder(newGuid);
            return new Guid(newGuid);
        }

        /// <summary>
        /// The namespace for fully-qualified domain names (from RFC 4122, Appendix C).
        /// </summary>
        public static readonly Guid DnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        /// <summary>
        /// The namespace for URLs (from RFC 4122, Appendix C).
        /// </summary>
        public static readonly Guid UrlNamespace = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

        /// <summary>
        /// The namespace for ISO OIDs (from RFC 4122, Appendix C).
        /// </summary>
        public static readonly Guid IsoOidNamespace = new Guid("6ba7b812-9dad-11d1-80b4-00c04fd430c8");

        // Converts a GUID (expressed as a byte array) to/from network order (MSB-first).
        static void SwapByteOrder(byte[] guid)
        {
            SwapBytes(guid, 0, 3);
            SwapBytes(guid, 1, 2);
            SwapBytes(guid, 4, 5);
            SwapBytes(guid, 6, 7);
        }

        static void SwapBytes(byte[] guid, int left, int right)
        {
            byte temp = guid[left];
            guid[left] = guid[right];
            guid[right] = temp;
        }

        public const string SpecVersionFileName = "sq.version";

        public static NuspecManifest ReadManifestFromVersionDir(string appVersionDir)
        {
            NuspecManifest manifest;
            string nuspec;

            nuspec = Path.Combine(appVersionDir, SpecVersionFileName);
            if (File.Exists(nuspec) && NuspecManifest.TryParseFromFile(nuspec, out manifest))
                return manifest;

            nuspec = Path.Combine(appVersionDir, "Contents", SpecVersionFileName);
            if (File.Exists(nuspec) && NuspecManifest.TryParseFromFile(nuspec, out manifest))
                return manifest;

            nuspec = Path.Combine(appVersionDir, "mysqver");
            if (File.Exists(nuspec) && NuspecManifest.TryParseFromFile(nuspec, out manifest))
                return manifest;

            nuspec = Path.Combine(appVersionDir, "current.version");
            if (File.Exists(nuspec) && NuspecManifest.TryParseFromFile(nuspec, out manifest))
                return manifest;

            return null;
        }
    }
}