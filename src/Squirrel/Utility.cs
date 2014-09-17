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
using Splat;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;

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

            if (content == null || content.Length < 2)
            {
                goto done;
            }

            Func<byte[], byte[], bool> matches = (bom, src) =>
            {
                if (src.Length < bom.Length)
                {
                    return false;
                }
                return !bom.Where((chr, index) => src[index] != chr).Any();
            };

            var utf32Be = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
            var utf32Le = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
            var utf16Be = new byte[] { 0xFE, 0xFF };
            var utf16Le = new byte[] { 0xFF, 0xFE };
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF };

            if (matches(utf32Be, content))
            {
                output = new byte[content.Length - utf32Be.Length];
            }
            else if (matches(utf32Le, content))
            {
                output = new byte[content.Length - utf32Le.Length];
            }
            else if (matches(utf16Be, content))
            {
                output = new byte[content.Length - utf16Be.Length];
            }
            else if (matches(utf16Le, content))
            {
                output = new byte[content.Length - utf16Le.Length];
            }
            else if (matches(utf8, content))
            {
                output = new byte[content.Length - utf8.Length];
            }
            else
            {
                output = content;
            }

        done:
            if (output.Length > 0)
            {
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

        public static Task<int> InvokeProcessAsync(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.ErrorDialog = false;

            return InvokeProcessAsync(psi);
        }

        public static async Task<int> InvokeProcessAsync(ProcessStartInfo psi)
        {
            var pi = Process.Start(psi);

            await Task.Run(() => pi.WaitForExit());
            return pi.ExitCode;
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

        static string directoryChars;
        public static IDisposable WithTempDirectory(out string path)
        {
            var di = new DirectoryInfo(Environment.GetEnvironmentVariable("SQUIRREL_TEMP") ?? Environment.GetEnvironmentVariable("TEMP") ?? "");
            if (!di.Exists) {
                throw new Exception("%TEMP% isn't defined, go set it");
            }

            var tempDir = default(DirectoryInfo);

            directoryChars = directoryChars ?? (
                "abcdefghijklmnopqrstuvwxyz" +
                Enumerable.Range(0x4E00, 0x9FCC - 0x4E00)  // CJK UNIFIED IDEOGRAPHS
                    .Aggregate(new StringBuilder(), (acc, x) => { acc.Append(Char.ConvertFromUtf32(x)); return acc; })
                    .ToString());

            foreach (var c in directoryChars) {
                var target = Path.Combine(di.FullName, c.ToString());

                if (!File.Exists(target) && !Directory.Exists(target)) {
                    Directory.CreateDirectory(target);
                    tempDir = new DirectoryInfo(target);
                    break;
                }
            }

            path = tempDir.FullName;

            return Disposable.Create(() =>
                DeleteDirectory(tempDir.FullName).Wait());
        }

        public static async Task DeleteDirectory(string directoryPath)
        {
            Contract.Requires(!String.IsNullOrEmpty(directoryPath));

            Log().Info("Starting to delete folder: {0}", directoryPath);

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

        public static Tuple<string, Stream> CreateTempFile()
        {
            var path = Path.GetTempFileName();
            return Tuple.Create(path, (Stream) File.OpenWrite(path));
        }

        public static string PackageDirectoryForAppDir(string rootAppDirectory) 
        {
            return Path.Combine(rootAppDirectory, "packages");
        }

        public static string LocalReleaseFileForAppDir(string rootAppDirectory)
        {
            return Path.Combine(PackageDirectoryForAppDir(rootAppDirectory), "RELEASES");
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
            try {
                var url = new Uri(urlOrPath);
                return new[] {"https", "http"}.Contains(url.Scheme.ToLowerInvariant());
            } catch (Exception) {
                return false;
            }
        }

        public static async Task DeleteDirectoryWithFallbackToNextReboot(string dir)
        {
            try {
                await Utility.DeleteDirectory(dir);
            } catch (Exception ex) {
                var message = String.Format("Uninstall failed to delete dir '{0}', punting to next reboot", dir);
                LogHost.Default.WarnException(message, ex);

                Utility.DeleteDirectoryAtNextReboot(dir);
            }
        }

        public static void DeleteDirectoryAtNextReboot(string directoryPath)
        {
            var di = new DirectoryInfo(directoryPath);

            if (!di.Exists) {
                Log().Warn("DeleteDirectoryAtNextReboot: does not exist - {0}", directoryPath);
                return;
            }

            // NB: MoveFileEx blows up if you're a non-admin, so you always need a backup plan
            di.GetFiles().ForEach(x => safeDeleteFileAtNextReboot(x.FullName));
            di.GetDirectories().ForEach(x => DeleteDirectoryAtNextReboot(x.FullName));

            safeDeleteFileAtNextReboot(directoryPath);
        }

        static void safeDeleteFileAtNextReboot(string name)
        {
            if (MoveFileEx(name, null, MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT)) return;

            // thank you, http://www.pinvoke.net/default.aspx/coredll.getlasterror
            var lastError = Marshal.GetLastWin32Error();

            Log().Error("safeDeleteFileAtNextReboot: failed - {0} - {1}", name, lastError);
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
                (logger = Locator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(Utility)));
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
    }

    sealed class SingleGlobalInstance : IDisposable
    {
        bool HasHandle = false;
        Mutex mutex;
        EventLoopScheduler scheduler; 

        public SingleGlobalInstance(string key, int timeOut)
        {
            if (ModeDetector.InUnitTestRunner()) {
                return;
            }

            scheduler = new EventLoopScheduler();

            initMutex(key);
            try {
                if (timeOut <= 0) {
                    HasHandle = scheduler.Enqueue(() => mutex.WaitOne(Timeout.Infinite, false)).Result;
                } else {
                    HasHandle = scheduler.Enqueue(() => mutex.WaitOne(timeOut, false)).Result;
                }

                if (HasHandle == false) {
                    throw new TimeoutException("Timeout waiting for exclusive access on SingleInstance");
                }
            } catch (AbandonedMutexException) {
                HasHandle = true;
            }
        }

        public void Dispose()
        {
            if (ModeDetector.InUnitTestRunner()) {
                return;
            }

            if (HasHandle && mutex != null) {
                scheduler.Enqueue(() => mutex.ReleaseMutex()).Wait();
                HasHandle = false;
            }

            scheduler.Dispose();
        }

        ~SingleGlobalInstance()
        {
            if (!HasHandle) return;
            throw new AbandonedMutexException("Leaked a Mutex!");
        }

        void initMutex(string key)
        {
            string mutexId = string.Format("Global\\{{{0}}}", key);
            mutex = new Mutex(false, mutexId);

            var allowEveryoneRule = new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), MutexRights.FullControl, AccessControlType.Allow);
            var securitySettings = new MutexSecurity();
            securitySettings.AddAccessRule(allowEveryoneRule);
            mutex.SetAccessControl(securitySettings);
        }
    }

    class EventLoopScheduler : IDisposable
    {
        readonly CancellationTokenSource shouldBail = new CancellationTokenSource();
        readonly BlockingCollection<Tuple<Action, TaskCompletionSource<bool>>> queue = new BlockingCollection<Tuple<Action, TaskCompletionSource<bool>>>();
        readonly Thread loop;
        IDisposable inner;

        public EventLoopScheduler()
        {
            loop = new Thread(() => {
                var token = shouldBail.Token;

                while (!token.IsCancellationRequested) {
                    Tuple<Action, TaskCompletionSource<bool>> action;

                    try {
                        action = queue.Take(token);
                    } catch (OperationCanceledException) {
                        continue;
                    }

                    try {
                        action.Item1();
                        action.Item2.TrySetResult(true);
                    } catch (Exception ex) {
                        action.Item2.TrySetException(ex);
                    }
                }
            });

            loop.Start();

            inner = Disposable.Create(() => {
                shouldBail.Cancel();
                loop.Join();
            });
        }

        public async Task<T> Enqueue<T>(Func<T> block)
        {
            T result = default(T);
            var tcs = new TaskCompletionSource<bool>();
            queue.Add(Tuple.Create(new Action(() => result = block()), tcs));
            await tcs.Task;

            return result;
        }

        public async Task Enqueue(Action block)
        {
            var tcs = new TaskCompletionSource<bool>();
            queue.Add(Tuple.Create(block, tcs));
            await tcs.Task;
        }

        public void Dispose()
        {
            inner.Dispose();
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
