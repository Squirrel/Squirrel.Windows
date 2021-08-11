using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using NuGet.Resources;

namespace NuGet
{
    public static class FileSystemExtensions
    {
        public static IEnumerable<string> GetFiles(this IFileSystem fileSystem, string path, string filter)
        {
            return fileSystem.GetFiles(path, filter, recursive: false);
        }

        public static void AddFiles(IFileSystem fileSystem, IEnumerable<IPackageFile> files, string rootDir)
        {
            AddFiles(fileSystem, files, rootDir, preserveFilePath: true);
        }

        /// <summary>
        /// Add the files to the specified FileSystem
        /// </summary>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="files">The files to add to FileSystem.</param>
        /// <param name="rootDir">The directory of the FileSystem to copy the files to.</param>
        /// <param name="preserveFilePath">if set to <c>true</c> preserve full path of the copies files. Otherwise,
        /// all files with be copied to the <paramref name="rootDir"/>.</param>
        public static void AddFiles(this IFileSystem fileSystem, IEnumerable<IPackageFile> files, string rootDir, bool preserveFilePath)
        {
            foreach (IPackageFile file in files)
            {
                string path = Path.Combine(rootDir, preserveFilePath ? file.Path : Path.GetFileName(file.Path));
                fileSystem.AddFileWithCheck(path, file.GetStream);
            }
        }

        internal static void DeleteFiles(IFileSystem fileSystem, IEnumerable<IPackageFile> files, string rootDir)
        {
            // First get all directories that contain files
            var directoryLookup = files.ToLookup(p => Path.GetDirectoryName(p.Path));


            // Get all directories that this package may have added
            var directories = from grouping in directoryLookup
                              from directory in GetDirectories(grouping.Key)
                              orderby directory.Length descending
                              select directory;

            // Remove files from every directory
            foreach (var directory in directories)
            {
                var directoryFiles = directoryLookup.Contains(directory) ? directoryLookup[directory] : Enumerable.Empty<IPackageFile>();
                string dirPath = Path.Combine(rootDir, directory);

                if (!fileSystem.DirectoryExists(dirPath))
                {
                    continue;
                }

                foreach (var file in directoryFiles)
                {
                    string path = Path.Combine(rootDir, file.Path);

                    fileSystem.DeleteFileSafe(path, file.GetStream);
                }

                // If the directory is empty then delete it
                if (!fileSystem.GetFilesSafe(dirPath).Any() &&
                    !fileSystem.GetDirectoriesSafe(dirPath).Any())
                {
                    fileSystem.DeleteDirectorySafe(dirPath, recursive: false);
                }
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We want to log an exception as a warning and move on")]
        internal static IEnumerable<string> GetDirectoriesSafe(this IFileSystem fileSystem, string path)
        {
            try
            {
                return fileSystem.GetDirectories(path);
            }
            catch (Exception e)
            {
                fileSystem.Logger.Log(MessageLevel.Warning, e.Message);
            }

            return Enumerable.Empty<string>();
        }

        internal static IEnumerable<string> GetFilesSafe(this IFileSystem fileSystem, string path)
        {
            return GetFilesSafe(fileSystem, path, "*.*");
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We want to log an exception as a warning and move on")]
        internal static IEnumerable<string> GetFilesSafe(this IFileSystem fileSystem, string path, string filter)
        {
            try
            {
                return fileSystem.GetFiles(path, filter);
            }
            catch (Exception e)
            {
                fileSystem.Logger.Log(MessageLevel.Warning, e.Message);
            }

            return Enumerable.Empty<string>();
        }

        internal static void DeleteDirectorySafe(this IFileSystem fileSystem, string path, bool recursive)
        {
            DoSafeAction(() => fileSystem.DeleteDirectory(path, recursive), fileSystem.Logger);
        }

        internal static void DeleteFileSafe(this IFileSystem fileSystem, string path)
        {
            DoSafeAction(() => fileSystem.DeleteFile(path), fileSystem.Logger);
        }

        public static bool ContentEqual(IFileSystem fileSystem, string path, Func<Stream> streamFactory)
        {
            using (Stream stream = streamFactory(),
                fileStream = fileSystem.OpenFile(path))
            {
                return stream.ContentEquals(fileStream);
            }
        }

        public static void DeleteFileSafe(this IFileSystem fileSystem, string path, Func<Stream> streamFactory)
        {
            // Only delete the file if it exists and the checksum is the same
            if (fileSystem.FileExists(path))
            {
                if (ContentEqual(fileSystem, path, streamFactory))
                {
                    fileSystem.DeleteFileSafe(path);
                }
                else
                {
                    // This package installed a file that was modified so warn the user
                    fileSystem.Logger.Log(MessageLevel.Warning, NuGetResources.Warning_FileModified, path);
                }
            }
        }

        public static void DeleteFileAndParentDirectoriesIfEmpty(this IFileSystem fileSystem, string filePath)
        {
            // first delete the file itself
            fileSystem.DeleteFileSafe(filePath);

            // now delete all parent directories if they are empty
            for (string path = Path.GetDirectoryName(filePath); !String.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
            {
                if (fileSystem.GetFiles(path, "*.*").Any() || fileSystem.GetDirectories(path).Any())
                {
                    // if this directory is not empty, stop
                    break;
                }
                else
                {
                    // otherwise, delete it, and move up to its parent
                    fileSystem.DeleteDirectorySafe(path, recursive: false);
                }
            }
        }

        internal static void AddFileWithCheck(this IFileSystem fileSystem, string path, Func<Stream> streamFactory)
        {
            if (fileSystem.FileExists(path))
            {
                fileSystem.Logger.Log(MessageLevel.Warning, NuGetResources.Warning_FileAlreadyExists, path);
            }
            else
            {
                using (Stream stream = streamFactory())
                {
                    fileSystem.AddFile(path, stream);
                }
            }
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "The caller is responsible for closing the stream")]
        internal static void AddFileWithCheck(this IFileSystem fileSystem, string path, Action<Stream> write)
        {
            if (fileSystem.FileExists(path))
            {
                fileSystem.Logger.Log(MessageLevel.Warning, NuGetResources.Warning_FileAlreadyExists, path);
            }
            else
            {
                fileSystem.AddFile(path, write);
            }
        }

        internal static IEnumerable<string> GetDirectories(string path)
        {
            foreach (var index in IndexOfAll(path, Path.DirectorySeparatorChar))
            {
                yield return path.Substring(0, index);
            }
            yield return path;
        }

        private static IEnumerable<int> IndexOfAll(string value, char ch)
        {
            int index = -1;
            do
            {
                index = value.IndexOf(ch, index + 1);
                if (index >= 0)
                {
                    yield return index;
                }
            }
            while (index >= 0);
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We want to log an exception as a warning and move on")]
        private static void DoSafeAction(Action action, ILogger logger)
        {
            try
            {
                Attempt(action);
            }
            catch (Exception e)
            {
                logger.Log(MessageLevel.Warning, e.Message);
            }
        }

        private static void Attempt(Action action, int retries = 3, int delayBeforeRetry = 150)
        {
            while (retries > 0)
            {
                try
                {
                    action();
                    break;
                }
                catch
                {
                    retries--;
                    if (retries == 0)
                    {
                        throw;
                    }
                }
                Thread.Sleep(delayBeforeRetry);
            }
        }
    }
}