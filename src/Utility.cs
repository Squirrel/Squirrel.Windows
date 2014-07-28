using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using ReactiveUIMicro;
using System.Text;

namespace Squirrel.Core
{
    public static class Utility
    {
        public static IEnumerable<FileInfo> GetAllFilesRecursively(this DirectoryInfo rootPath)
        {
            Contract.Requires(rootPath != null);

            return rootPath.GetDirectories()
                .SelectMany(GetAllFilesRecursively)
                .Concat(rootPath.GetFiles());
        }

        public static IEnumerable<string> GetAllFilePathsRecursively(string rootPath)
        {
            Contract.Requires(rootPath != null);

            return Directory.GetDirectories(rootPath)
                .SelectMany(GetAllFilePathsRecursively)
                .Concat(Directory.GetFiles(rootPath));
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

        public static IObservable<Unit> CopyToAsync(string from, string to)
        {
            Contract.Requires(!String.IsNullOrEmpty(from) && File.Exists(from));
            Contract.Requires(!String.IsNullOrEmpty(to));

            if (!File.Exists(from)) {
                Log().Warn("The file {0} does not exist", from);

                // TODO: should we fail this operation?
                return Observable.Return(Unit.Default);
            }

            // XXX: SafeCopy
            return Observable.Start(() => File.Copy(from, to, true), RxApp.TaskpoolScheduler);
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

        public static IObservable<IList<TRet>> MapReduce<T, TRet>(this IObservable<T> This, Func<T, IObservable<TRet>> selector, int degreeOfParallelism = 4)
        {
            return This.Select(x => Observable.Defer(() => selector(x))).Merge(degreeOfParallelism).ToList();
        }

        public static IObservable<IList<TRet>> MapReduce<T, TRet>(this IEnumerable<T> This, Func<T, IObservable<TRet>> selector, int degreeOfParallelism = 4)
        {
            return This.ToObservable().Select(x => Observable.Defer(() => selector(x))).Merge(degreeOfParallelism).ToList();
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

        public static IObservable<Unit> DeleteDirectory(string directoryPath, IScheduler scheduler = null)
        {
            Contract.Requires(!String.IsNullOrEmpty(directoryPath));

            scheduler = scheduler ?? RxApp.TaskpoolScheduler;

            Log().Info("Starting to delete folder: {0}", directoryPath);

            if (!Directory.Exists(directoryPath)) {
                Log().Warn("DeleteDirectory: does not exist - {0}", directoryPath);
                return Observable.Return(Unit.Default);
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

            var fileOperations = files.MapReduce(file =>
                Observable.Start(() => {
                    Log().Debug("Now deleting file: {0}", file);
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }, scheduler))
            .Select(_ => Unit.Default);

            var directoryOperations =
                dirs.MapReduce(dir => DeleteDirectory(dir, scheduler)
                    .Retry(3))
                    .Select(_ => Unit.Default);

            return fileOperations
                .Merge(directoryOperations, scheduler)
                .ToList() // still feeling a bit icky
                .Select(_ => {
                    Log().Debug("Now deleting folder: {0}", directoryPath);
                    File.SetAttributes(directoryPath, FileAttributes.Normal);

                    try {
                        Directory.Delete(directoryPath, false);
                    } catch (Exception ex) {
                        var message = String.Format("DeleteDirectory: could not delete - {0}", directoryPath);
                        Log().ErrorException(message, ex);
                    }
                    return Unit.Default;
                });
        }

        public static Tuple<string, Stream> CreateTempFile()
        {
            var path = Path.GetTempFileName();
            return Tuple.Create(path, (Stream) File.OpenWrite(path));
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

        static IRxUIFullLogger Log()
        {
            return LogManager.GetLogger(typeof(Utility));
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

    public sealed class SingleGlobalInstance : IDisposable
    {
        readonly static object gate = 42;
        bool HasHandle = false;
        Mutex mutex;
        EventLoopScheduler lockScheduler = new EventLoopScheduler();

        public SingleGlobalInstance(string key, int timeOut)
        {
            if (RxApp.InUnitTestRunner()) {
                HasHandle = Observable.Start(() => Monitor.TryEnter(gate, timeOut), lockScheduler).First();

                if (HasHandle == false)
                    throw new TimeoutException("Timeout waiting for exclusive access on SingleInstance");
                return;
            }

            initMutex(key);
            try
            {
                if (timeOut <= 0)
                    HasHandle = Observable.Start(() => mutex.WaitOne(Timeout.Infinite, false), lockScheduler).First();
                else
                    HasHandle = Observable.Start(() => mutex.WaitOne(timeOut, false), lockScheduler).First();

                if (HasHandle == false)
                    throw new TimeoutException("Timeout waiting for exclusive access on SingleInstance");
            }
            catch (AbandonedMutexException)
            {
                HasHandle = true;
            }
        }

        private void initMutex(string key)
        {
            string mutexId = string.Format("Global\\{{{0}}}", key);
            mutex = new Mutex(false, mutexId);

            var allowEveryoneRule = new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), MutexRights.FullControl, AccessControlType.Allow);
            var securitySettings = new MutexSecurity();
            securitySettings.AddAccessRule(allowEveryoneRule);
            mutex.SetAccessControl(securitySettings);
        }

        public void Dispose()
        {
            if (HasHandle && RxApp.InUnitTestRunner()) {
                Observable.Start(() => Monitor.Exit(gate), lockScheduler).First();
                HasHandle = false;
            }

            if (HasHandle && mutex != null) {
                Observable.Start(() => mutex.ReleaseMutex(), lockScheduler).First();
                HasHandle = false;
            }

            lockScheduler.Dispose();
        }
    }
}
