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
using System.Net;
using NuGet.Versioning;
using Squirrel.Lib;
using Squirrel.NuGet;

namespace Squirrel
{
    internal static class Utility
    {
        public static string RemoveByteOrderMarkerIfPresent(string content)
        {
            return string.IsNullOrEmpty(content) ?
                string.Empty : RemoveByteOrderMarkerIfPresent(Encoding.UTF8.GetBytes(content));
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
            return normalizedFile.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase);
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

        /*
         * caesay — 09/12/2021 at 12:10 PM
         * yeah
         * can I steal this for squirrel? 
         * Roman — 09/12/2021 at 12:10 PM
         * sure :)
         * reference CommandRunner.cs on the github url as source? :)
         * https://github.com/RT-Projects/RT.Util/blob/ef660cd693f66bc946da3aaa368893b03b74eed7/RT.Util.Core/CommandRunner.cs#L327
         */

        /// <summary>
        ///     Given a number of argument strings, constructs a single command line string with all the arguments escaped
        ///     correctly so that a process using standard Windows API for parsing the command line will receive exactly the
        ///     strings passed in here. See Remarks.</summary>
        /// <remarks>
        ///     The string is only valid for passing directly to a process. If the target process is invoked by passing the
        ///     process name + arguments to cmd.exe then further escaping is required, to counteract cmd.exe's interpretation
        ///     of additional special characters. See <see cref="EscapeCmdExeMetachars"/>.</remarks>
        public static string ArgsToCommandLine(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var arg in args) {
                if (arg == null)
                    continue;
                if (sb.Length != 0)
                    sb.Append(' ');
                // For details, see https://web.archive.org/web/20150318010344/http://blogs.msdn.com/b/twistylittlepassagesallalike/archive/2011/04/23/everyone-quotes-arguments-the-wrong-way.aspx
                // or https://devblogs.microsoft.com/oldnewthing/?p=12833
                if (arg.Length != 0 && arg.IndexOfAny(_cmdChars) < 0)
                    sb.Append(arg);
                else {
                    sb.Append('"');
                    for (int c = 0; c < arg.Length; c++) {
                        int backslashes = 0;
                        while (c < arg.Length && arg[c] == '\\') {
                            c++;
                            backslashes++;
                        }
                        if (c == arg.Length) {
                            sb.Append('\\', backslashes * 2);
                            break;
                        } else if (arg[c] == '"') {
                            sb.Append('\\', backslashes * 2 + 1);
                            sb.Append('"');
                        } else {
                            sb.Append('\\', backslashes);
                            sb.Append(arg[c]);
                        }
                    }
                    sb.Append('"');
                }
            }
            return sb.ToString();
        }
        private static readonly char[] _cmdChars = new[] { ' ', '"', '\n', '\t', '\v' };

        /// <summary>
        ///     Escapes all cmd.exe meta-characters by prefixing them with a ^. See <see cref="ArgsToCommandLine"/> for more
        ///     information.</summary>
        public static string EscapeCmdExeMetachars(string command)
        {
            var result = new StringBuilder();
            foreach (var ch in command) {
                switch (ch) {
                case '(':
                case ')':
                case '%':
                case '!':
                case '^':
                case '"':
                case '<':
                case '>':
                case '&':
                case '|':
                    result.Append('^');
                    break;
                }
                result.Append(ch);
            }
            return result.ToString();
        }

        /// <summary>
        /// This function will escape command line arguments such that CommandLineToArgvW is guarenteed to produce the same output as the 'args' parameter. 
        /// It also will automatically execute wine if trying to run an exe while not on windows.
        /// </summary>
        public static Task<(int ExitCode, string StdOutput)> InvokeProcessAsync(string fileName, IEnumerable<string> args, CancellationToken ct, string workingDirectory = "")
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                return InvokeProcessUnsafeAsync(CreateProcessStartInfo("wine", ArgsToCommandLine(new string[] { fileName }.Concat(args)), workingDirectory), ct);
            } else {
                return InvokeProcessUnsafeAsync(CreateProcessStartInfo(fileName, ArgsToCommandLine(args), workingDirectory), ct);
            }
        }

        public static ProcessStartInfo CreateProcessStartInfo(string fileName, string arguments, string workingDirectory = "")
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.ErrorDialog = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = workingDirectory;
            return psi;
        }

        public static async Task<(int ExitCode, string StdOutput)> InvokeProcessUnsafeAsync(ProcessStartInfo psi, CancellationToken ct)
        {
            var pi = Process.Start(psi);
            await Task.Run(() => {
                while (!ct.IsCancellationRequested) {
                    if (pi.WaitForExit(2000)) return;
                }

                if (ct.IsCancellationRequested) {
                    pi.Kill();
                    ct.ThrowIfCancellationRequested();
                }
            }).ConfigureAwait(false);

            string textResult = await pi.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(textResult) || pi.ExitCode != 0) {
                textResult = (textResult ?? "") + "\n" + await pi.StandardError.ReadToEndAsync().ConfigureAwait(false);

                if (String.IsNullOrWhiteSpace(textResult)) {
                    textResult = String.Empty;
                }
            }

            return (pi.ExitCode, textResult.Trim());
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

        static Lazy<string> directoryChars = new Lazy<string>(() => {
            return "abcdefghijklmnopqrstuvwxyz" +
                Enumerable.Range(0x03B0, 0x03FF - 0x03B0)   // Greek and Coptic
                    .Concat(Enumerable.Range(0x0400, 0x04FF - 0x0400)) // Cyrillic
                    .Aggregate(new StringBuilder(), (acc, x) => { acc.Append(Char.ConvertFromUtf32(x)); return acc; })
                    .ToString();
        });

        internal static string tempNameForIndex(int index, string prefix)
        {
            if (index < directoryChars.Value.Length) {
                return prefix + directoryChars.Value[index];
            }

            return prefix + directoryChars.Value[index % directoryChars.Value.Length] + tempNameForIndex(index / directoryChars.Value.Length, "");
        }

        public static DirectoryInfo GetTempDirectory(string localAppDirectory)
        {
#if DEBUG
            const string TEMP_ENV_VAR = "CLOWD_SQUIRREL_TEMP_DEBUG";
            const string TEMP_DIR_NAME = "SquirrelClowdTempDebug";
#else
            const string TEMP_ENV_VAR = "CLOWD_SQUIRREL_TEMP";
            const string TEMP_DIR_NAME = "SquirrelClowdTemp";
#endif

            var tempDir = Environment.GetEnvironmentVariable(TEMP_ENV_VAR);
            tempDir = tempDir ?? Path.Combine(localAppDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), TEMP_DIR_NAME);

            var di = new DirectoryInfo(tempDir);
            if (!di.Exists) di.Create();

            return di;
        }

        public static IDisposable WithTempDirectory(out string path, string localAppDirectory = null)
        {
            var di = GetTempDirectory(localAppDirectory);
            var tempDir = default(DirectoryInfo);

            var names = Enumerable.Range(0, 1 << 20).Select(x => tempNameForIndex(x, "temp"));

            foreach (var name in names) {
                var target = Path.Combine(di.FullName, name);

                if (!File.Exists(target) && !Directory.Exists(target)) {
                    Directory.CreateDirectory(target);
                    tempDir = new DirectoryInfo(target);
                    break;
                }
            }

            path = tempDir.FullName;

            return Disposable.Create(() => DeleteFileOrDirectoryHardOrGiveUp(tempDir.FullName));
        }

        public static IDisposable WithTempFile(out string path, string localAppDirectory = null)
        {
            var di = GetTempDirectory(localAppDirectory);
            var names = Enumerable.Range(0, 1 << 20).Select(x => tempNameForIndex(x, "temp"));

            path = "";
            foreach (var name in names) {
                path = Path.Combine(di.FullName, name);

                if (!File.Exists(path) && !Directory.Exists(path)) {
                    break;
                }
            }

            var thePath = path;
            return Disposable.Create(() => File.Delete(thePath));
        }

        public static void DeleteFileOrDirectoryHard(string path, bool throwOnFailure = true)
        {
            Contract.Requires(!String.IsNullOrEmpty(path));
            Log().Debug("Starting to delete: {0}", path);

            try {
                if (File.Exists(path)) {
                    DeleteFsiVeryHard(new FileInfo(path));
                } else if (Directory.Exists(path)) {
                    DeleteFsiTree(new DirectoryInfo(path));
                } else {
                    if (throwOnFailure)
                        Log().Warn($"Cannot delete '{path}' if it does not exist.");
                }
            } catch (Exception ex) {
                Log().ErrorException($"Unable to delete '{path}'", ex);
                if (throwOnFailure)
                    throw;
            }
        }

        public static void DeleteFileOrDirectoryHardOrGiveUp(string path) => DeleteFileOrDirectoryHard(path, false);

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
            if (fileSystemInfo.FullName.Equals(SquirrelRuntimeInfo.EntryExePath, StringComparison.InvariantCultureIgnoreCase))
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

        public static string PackageDirectoryForAppDir(string rootAppDirectory)
        {
            return Path.Combine(rootAppDirectory, "packages");
        }

        public static string LocalReleaseFileForAppDir(string rootAppDirectory)
        {
            return Path.Combine(PackageDirectoryForAppDir(rootAppDirectory), "RELEASES");
        }

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
            return String.Format("com.squirrel.{0}.{1}", packageId.Replace(" ", ""), exeName.Replace(".exe", "").Replace(" ", ""));
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
            var bc = Console.BackgroundColor;
            Console.ForegroundColor = color;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(text);
            Console.ForegroundColor = fc;
            Console.BackgroundColor = bc;
        }

        static IFullLogger logger;
        static IFullLogger Log()
        {
            return logger ?? (logger = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(Utility)));
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

#if NET5_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public static List<(string ProcessExePath, int ProcessId)> EnumerateProcesses()
        {
            var pids = new int[2048];
            var gch = GCHandle.Alloc(pids, GCHandleType.Pinned);
            try {
                if (!NativeMethods.EnumProcesses(gch.AddrOfPinnedObject(), sizeof(int) * pids.Length, out var bytesReturned))
                    throw new Win32Exception("Failed to enumerate processes");

                if (bytesReturned < 1)
                    throw new Exception("Failed to enumerate processes");

                List<(string ProcessExePath, int ProcessId)> ret = new();

                for (int i = 0; i < bytesReturned / sizeof(int); i++) {
                    IntPtr hProcess = IntPtr.Zero;
                    try {
                        hProcess = NativeMethods.OpenProcess(ProcessAccess.QueryLimitedInformation, false, pids[i]);
                        if (hProcess == IntPtr.Zero)
                            continue;

                        var sb = new StringBuilder(256);
                        var capacity = sb.Capacity;
                        if (!NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                            continue;

                        var exePath = sb.ToString();
                        if (String.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                            continue;

                        ret.Add((sb.ToString(), pids[i]));
                    } catch (Exception) {
                        // don't care
                    } finally {
                        if (hProcess != IntPtr.Zero)
                            NativeMethods.CloseHandle(hProcess);
                    }
                }
                return ret;
            } finally {
                gch.Free();
            }
        }

#if NET5_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public static void KillProcessesInDirectory(string directoryToKill)
        {
            var ourExePath = SquirrelRuntimeInfo.EntryExePath;
            Utility.EnumerateProcesses()
                .Where(x => {
                    // Processes we can't query will have an empty process name,
                    // we can't kill them anyways
                    if (String.IsNullOrWhiteSpace(x.ProcessExePath)) return false;

                    // Files that aren't in our root app directory are untouched
                    if (!Utility.IsFileInDirectory(x.ProcessExePath, directoryToKill)) return false;

                    // Never kill our own EXE
                    if (ourExePath != null && x.ProcessExePath.Equals(ourExePath, StringComparison.OrdinalIgnoreCase)) return false;

                    var name = Path.GetFileName(x.ProcessExePath).ToLowerInvariant();
                    if (name == "squirrel.exe" || name == "update.exe") return false;

                    return true;
                })
                .ForEach(x => {
                    try {
                        Process.GetProcessById(x.ProcessId).Kill();
                    } catch (Exception ex) {
                        Log().WarnException($"Unable to terminate process (pid.{x.ProcessId})", ex);
                    }
                });
        }

        public record VersionDirInfo(IPackage Manifest, SemanticVersion Version, string DirectoryPath, bool IsCurrent, bool IsExecuting);

        public static IEnumerable<VersionDirInfo> GetAppVersionDirectories(string rootAppDir)
        {
            foreach (var d in Directory.EnumerateDirectories(rootAppDir)) {
                var directoryName = Path.GetFileName(d);
                bool isExecuting = IsFileInDirectory(SquirrelRuntimeInfo.EntryExePath, d);
                bool isCurrent = directoryName.Equals("current", StringComparison.InvariantCultureIgnoreCase);
                var nuspec = Path.Combine(d, "mysqver");
                if (File.Exists(nuspec) && NuspecManifest.TryParseFromFile(nuspec, out var manifest)) {
                    yield return new(manifest, manifest.Version, d, isCurrent, isExecuting);
                } else if (directoryName.StartsWith("app-", StringComparison.InvariantCultureIgnoreCase) && NuGetVersion.TryParse(directoryName.Substring(4), out var ver)) {
                    yield return new(null, ver, d, isCurrent, isExecuting);
                }
            }
        }

#if NET5_0_OR_GREATER
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public static string UpdateAndRetrieveCurrentFolder(string rootAppDir, bool force)
        {
            try {
                var releases = GetAppVersionDirectories(rootAppDir).ToArray();
                var latestVer = releases.OrderByDescending(m => m.Version).First();
                var currentVer = releases.FirstOrDefault(f => f.IsCurrent);

                // if the latest ver is already current, or it does not support
                // being in a current directory.
                if (latestVer.IsCurrent || latestVer.Manifest == null) {
                    return latestVer.DirectoryPath;
                }

                if (force) {
                    Log().Info($"Killing running processes in '{rootAppDir}'.");
                    KillProcessesInDirectory(rootAppDir);
                }

                // 'current' does exist, and it's wrong, so lets move it
                string legacyVersionDir = null;
                if (currentVer != default) {
                    legacyVersionDir = Path.Combine(rootAppDir, "app-" + currentVer.Version);
                    Log().Info($"Moving '{currentVer.DirectoryPath}' to '{legacyVersionDir}'.");
                    Retry(() => Directory.Move(currentVer.DirectoryPath, legacyVersionDir), 8);
                }


                // 'current' doesn't exist right now, lets move the latest version
                var latestDir = Path.Combine(rootAppDir, "current");
                Log().Info($"Moving '{latestVer.DirectoryPath}' to '{latestDir}'.");
                Retry(() => Directory.Move(latestVer.DirectoryPath, latestDir), 8);

                Log().Info("Running app in: " + latestDir);
                return latestDir;
            } catch (Exception e) {
                var releases = GetAppVersionDirectories(rootAppDir).ToArray();
                string fallback = releases.OrderByDescending(m => m.Version).First().DirectoryPath;
                var currentVer = releases.FirstOrDefault(f => f.IsCurrent);
                if (currentVer != default && Directory.Exists(currentVer.DirectoryPath)) {
                    fallback = currentVer.DirectoryPath;
                }
                Log().WarnException("Unable to update 'current' directory", e);
                Log().Info("Running app in: " + fallback);
                return fallback;
            }
        }
    }
}

#if NETFRAMEWORK || NETSTANDARD2_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif