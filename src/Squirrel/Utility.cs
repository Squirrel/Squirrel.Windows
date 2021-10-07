using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using Squirrel.SimpleSplat;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using NuGet;

namespace Squirrel
{
    static class Utility
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

        public static WebClient CreateWebClient()
        {
            // WHY DOESNT IT JUST DO THISSSSSSSS
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            var ret = new WebClient();
            var wp = WebRequest.DefaultWebProxy;
            if (wp != null) {
                wp.Credentials = CredentialCache.DefaultCredentials;
                ret.Proxy = wp;
            }

            return ret;
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
            await Task.Run(() => File.Copy(from, to, true));
        }

        public static void Retry(this Action block, int retries = 2)
        {
            Contract.Requires(retries > 0);

            Func<object> thunk = () => {
                block();
                return null;
            };

            thunk.Retry(retries);
        }

        public static T Retry<T>(this Func<T> block, int retries = 2)
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
                    Thread.Sleep(250);
                }
            }
        }

        public static Task<Tuple<int, string>> InvokeProcessAsync(string fileName, string arguments, CancellationToken ct, string workingDirectory = "")
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            if (Environment.OSVersion.Platform != PlatformID.Win32NT && fileName.EndsWith (".exe", StringComparison.OrdinalIgnoreCase)) {
                psi = new ProcessStartInfo("wine", fileName + " " + arguments);
            }

            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.ErrorDialog = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = workingDirectory;

            return InvokeProcessAsync(psi, ct);
        }

        public static async Task<Tuple<int, string>> InvokeProcessAsync(ProcessStartInfo psi, CancellationToken ct)
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
            });

            string textResult = await pi.StandardOutput.ReadToEndAsync();
            if (String.IsNullOrWhiteSpace(textResult) || pi.ExitCode != 0) {
                textResult = (textResult ?? "") + "\n" + await pi.StandardError.ReadToEndAsync();

                if (String.IsNullOrWhiteSpace(textResult)) {
                    textResult = String.Empty;
                }
            }

            return Tuple.Create(pi.ExitCode, textResult.Trim());
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
                            await body(partition.Current);
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
            var tempDir = Environment.GetEnvironmentVariable("SQUIRREL_TEMP");
            tempDir = tempDir ?? Path.Combine(localAppDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SquirrelTemp");

            var di = new DirectoryInfo(tempDir);
            if (!di.Exists) di.Create();

            return di;
        }

        public static IDisposable WithTempDirectory(out string path, string localAppDirectory = null)
        {
            var di = GetTempDirectory(localAppDirectory);
            var tempDir = default(DirectoryInfo);

            var names = Enumerable.Range(0, 1<<20).Select(x => tempNameForIndex(x, "temp"));

            foreach (var name in names) {
                var target = Path.Combine(di.FullName, name);

                if (!File.Exists(target) && !Directory.Exists(target)) {
                    Directory.CreateDirectory(target);
                    tempDir = new DirectoryInfo(target);
                    break;
                }
            }

            path = tempDir.FullName;

            return Disposable.Create(() => Task.Run(async () => await DeleteDirectory(tempDir.FullName)).Wait());
        }

        public static IDisposable WithTempFile(out string path, string localAppDirectory = null)
        {
            var di = GetTempDirectory(localAppDirectory);
            var names = Enumerable.Range(0, 1<<20).Select(x => tempNameForIndex(x, "temp"));

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

        public static async Task DeleteDirectory(string directoryPath)
        {
            Contract.Requires(!String.IsNullOrEmpty(directoryPath));

            Log().Debug("Starting to delete folder: {0}", directoryPath);

            if (!Directory.Exists(directoryPath)) {
                Log().Warn("DeleteDirectory: does not exist - {0}", directoryPath);
                return;
            }

            // From http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true/329502#329502
            var files = new string[0];
            try {
                files = Directory.GetFiles(directoryPath);
            } catch (UnauthorizedAccessException ex) {
                var message = String.Format("The files inside {0} could not be read", directoryPath);
                Log().Warn(message, ex);
            }

            var dirs = new string[0];
            try {
                dirs = Directory.GetDirectories(directoryPath);
            } catch (UnauthorizedAccessException ex) {
                var message = String.Format("The directories inside {0} could not be read", directoryPath);
                Log().Warn(message, ex);
            }

            var fileOperations = files.ForEachAsync(file => {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            });

            var directoryOperations =
                dirs.ForEachAsync(async dir => await DeleteDirectory(dir));

            await Task.WhenAll(fileOperations, directoryOperations);

            Log().Debug("Now deleting folder: {0}", directoryPath);
            File.SetAttributes(directoryPath, FileAttributes.Normal);

            try {
                Directory.Delete(directoryPath, false);
            } catch (Exception ex) {
                var message = String.Format("DeleteDirectory: could not delete - {0}", directoryPath);
                Log().ErrorException(message, ex);
            }
        }

        public static string FindHelperExecutable(string toFind, IEnumerable<string> additionalDirs = null)
        {
            additionalDirs = additionalDirs ?? Enumerable.Empty<string>();
            var dirs = (new[] { Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) })
                .Concat(additionalDirs ?? Enumerable.Empty<string>());

            var exe = @".\" + toFind;
            return dirs
                .Select(x => Path.Combine(x, toFind))
                .FirstOrDefault(x => File.Exists(x)) ?? exe;
        }

        static string find7Zip()
        {
            if (ModeDetector.InUnitTestRunner()) {
                var vendorDir = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase.Replace("file:///", "")),
                    "..", "..", "..", "..",
                    "vendor", "7zip"
                );
                return FindHelperExecutable("7z.exe", new[] { vendorDir });
            } else {
                return FindHelperExecutable("7z.exe");
            }
        }

        public static async Task ExtractZipToDirectory(string zipFilePath, string outFolder)
        {
            var sevenZip = find7Zip();
            var result = default(Tuple<int, string>);

            try {
                var cmd = sevenZip;
                var args = String.Format("x \"{0}\" -tzip -mmt on -aoa -y -o\"{1}\" *", zipFilePath, outFolder);
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                    cmd = "wine";
                    args = sevenZip + " " + args;
                }

                result = await Utility.InvokeProcessAsync(cmd, args, CancellationToken.None);
                if (result.Item1 != 0) throw new Exception(result.Item2);
            } catch (Exception ex) {
                Log().Error($"Failed to extract file {zipFilePath} to {outFolder}\n{ex.Message}");
                throw;
            }
        }

        public static async Task CreateZipFromDirectory(string zipFilePath, string inFolder)
        {
            var sevenZip = find7Zip();
            var result = default(Tuple<int, string>);

            try {
                var cmd = sevenZip;
                var args = String.Format("a \"{0}\" -tzip -aoa -y -mmt on *", zipFilePath);
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                    cmd = "wine";
                    args = sevenZip + " " + args;
                }

                result = await Utility.InvokeProcessAsync(cmd, args, CancellationToken.None, inFolder);
                if (result.Item1 != 0) throw new Exception(result.Item2);
            } catch (Exception ex) {
                Log().Error($"Failed to extract file {zipFilePath} to {inFolder}\n{ex.Message}");
                throw;
            }
        }

        public static string AppDirForRelease(string rootAppDirectory, ReleaseEntry entry)
        {
            return Path.Combine(rootAppDirectory, "app-" + entry.Version.ToString());
        }

        public static string AppDirForVersion(string rootAppDirectory, SemanticVersion version)
        {
            return Path.Combine(rootAppDirectory, "app-" + version.ToString());
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

        static TAcc scan<T, TAcc>(this IEnumerable<T> This, TAcc initialValue, Func<TAcc, T, TAcc> accFunc)
        {
            TAcc acc = initialValue;

            foreach (var x in This)
            {
                acc = accFunc(acc, x);
            }

            return acc;
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

        public static void DeleteFileHarder(string path, bool ignoreIfFails = false)
        {
            try {
                Retry(() => File.Delete(path), 2);
            } catch (Exception ex) {
                if (ignoreIfFails) return;

                LogHost.Default.ErrorException("Really couldn't delete file: " + path, ex);
                throw;
            }
        }

        public static async Task DeleteDirectoryOrJustGiveUp(string dir)
        {
            try {
                await Utility.DeleteDirectory(dir);
            } catch {
                var message = String.Format("Uninstall failed to delete dir '{0}'", dir);
            }
        }

        // http://stackoverflow.com/questions/3111669/how-can-i-determine-the-subsystem-used-by-a-given-net-assembly
        public static bool ExecutableUsesWin32Subsystem(string peImage)
        {
            using (var s = new FileStream(peImage, FileMode.Open, FileAccess.Read)) {
                var rawPeSignatureOffset = new byte[4];
                s.Seek(0x3c, SeekOrigin.Begin);
                s.Read(rawPeSignatureOffset, 0, 4);

                int peSignatureOffset = rawPeSignatureOffset[0];
                peSignatureOffset |= rawPeSignatureOffset[1] << 8;
                peSignatureOffset |= rawPeSignatureOffset[2] << 16;
                peSignatureOffset |= rawPeSignatureOffset[3] << 24;

                var coffHeader = new byte[24];
                s.Seek(peSignatureOffset, SeekOrigin.Begin);
                s.Read(coffHeader, 0, 24);

                byte[] signature = { (byte)'P', (byte)'E', (byte)'\0', (byte)'\0' };
                for (int index = 0; index < 4; index++) {
                    if (coffHeader[index] != signature[index]) throw new Exception("File is not a PE image");
                }

                var subsystemBytes = new byte[2];
                s.Seek(68, SeekOrigin.Current);
                s.Read(subsystemBytes, 0, 2);

                int subSystem = subsystemBytes[0] | subsystemBytes[1] << 8;
                return subSystem == 2; /*IMAGE_SUBSYSTEM_WINDOWS_GUI*/
            }
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
                await block();
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
                return await block();
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

        static IFullLogger logger;
        static IFullLogger Log()
        {
            return logger ??
                (logger = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(Utility)));
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);

        [Flags]
        enum MoveFileFlags
        {
            MOVEFILE_REPLACE_EXISTING = 0x00000001,
            MOVEFILE_COPY_ALLOWED = 0x00000002,
            MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004,
            MOVEFILE_WRITE_THROUGH = 0x00000008,
            MOVEFILE_CREATE_HARDLINK = 0x00000010,
            MOVEFILE_FAIL_IF_NOT_TRACKABLE = 0x00000020
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
            newGuid[6] = (byte)((newGuid[6] & 0x0F) | (5 << 4));

            // set the two most significant bits (bits 6 and 7) of the 
            // clock_seq_hi_and_reserved to zero and one, respectively 
            // (step 10)
            newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);

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
    }

    static unsafe class UnsafeUtility
    {
        public static List<Tuple<string, int>> EnumerateProcesses()
        {
            int bytesReturned = 0;
            var pids = new int[2048];

            fixed(int* p = pids) {
                if (!NativeMethods.EnumProcesses((IntPtr)p, sizeof(int) * pids.Length, out bytesReturned)) {
                    throw new Win32Exception("Failed to enumerate processes");
                }

                if (bytesReturned < 1) throw new Exception("Failed to enumerate processes");
            }

            return Enumerable.Range(0, bytesReturned / sizeof(int))
                .Where(i => pids[i] > 0)
                .Select(i => {
                    try {
                        var hProcess = NativeMethods.OpenProcess(ProcessAccess.QueryLimitedInformation, false, pids[i]);
                        if (hProcess == IntPtr.Zero) throw new Win32Exception();

                        var sb = new StringBuilder(256);
                        var capacity = sb.Capacity;
                        if (!NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref capacity)) {
                            throw new Win32Exception();
                        }

                        NativeMethods.CloseHandle(hProcess);
                        return Tuple.Create(sb.ToString(), pids[i]);
                    } catch (Exception) {
                        return Tuple.Create(default(string), pids[i]);
                    }
                })
                .ToList();
        }
    }

    sealed class SingleGlobalInstance : IDisposable, IEnableLogger
    {
        IDisposable handle = null;

        public SingleGlobalInstance(string key, TimeSpan timeOut)
        {
            if (ModeDetector.InUnitTestRunner()) {
                return;
            }

            var path = Path.Combine(Path.GetTempPath(), ".squirrel-lock-" + key);

            var st = new Stopwatch();
            st.Start();

            var fh = default(FileStream);
            while (st.Elapsed < timeOut) {
                try {
                    fh = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete);
                    fh.Write(new byte[] { 0xba, 0xad, 0xf0, 0x0d, }, 0, 4);
                    break;
                } catch (Exception ex) {
                    this.Log().WarnException("Failed to grab lockfile, will retry: " + path, ex);
                    Thread.Sleep(250);
                }
            }

            st.Stop();

            if (fh == null) {
                throw new Exception("Couldn't acquire lock, is another instance running");
            }

            handle = Disposable.Create(() => {
                fh.Dispose();
                File.Delete(path);
            });
        }

        public void Dispose()
        {
            if (ModeDetector.InUnitTestRunner()) {
                return;
            }

            var disp = Interlocked.Exchange(ref handle, null);
            if (disp != null) disp.Dispose();
        }

        ~SingleGlobalInstance()
        {
            if (handle == null) return;
            throw new AbandonedMutexException("Leaked a Mutex!");
        }
    }

    static class Disposable
    {
        public static IDisposable Create(Action action)
        {
            return new AnonDisposable(action);
        }

        class AnonDisposable : IDisposable
        {
            static readonly Action dummyBlock = (() => { });
            Action block;

            public AnonDisposable(Action b)
            {
                block = b;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref block, dummyBlock)();
            }
        }
    }
}
