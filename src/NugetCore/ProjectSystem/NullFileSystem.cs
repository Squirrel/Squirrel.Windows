using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NuGet
{
    public class NullFileSystem : IFileSystem
    {
        private static readonly NullFileSystem _instance = new NullFileSystem();

        private NullFileSystem()
        {
            // Private constructor for a singleton
        }

        public static NullFileSystem Instance
        {
            get { return _instance; }
        }

        public ILogger Logger
        {
            get;
            set;
        }

        public string Root
        {
            get { return String.Empty; }
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            // Do nothing
        }

        public IEnumerable<string> GetFiles(string path, string filter, bool recursive)
        {
            return Enumerable.Empty<string>();
        }

        public IEnumerable<string> GetDirectories(string path)
        {
            return Enumerable.Empty<string>();
        }

        public string GetFullPath(string path)
        {
            return path;
        }

        public void DeleteFile(string path)
        {
            // Do nothing
        }

        public void DeleteFiles(IEnumerable<IPackageFile> files, string rootDir)
        {
            // Do nothing
        }

        public bool FileExists(string path)
        {
            return false;
        }

        public bool DirectoryExists(string path)
        {
            return false;
        }

        public void AddFile(string path, Stream stream)
        {
            // Do nothing
        }

        public void AddFile(string path, Action<Stream> writeToStream)
        {
            // Do nothing
        }

        public void AddFiles(IEnumerable<IPackageFile> files, string rootDir)
        {
            // Do nothing
        }

        public Stream CreateFile(string path)
        {
            return Stream.Null;
        }

        public Stream OpenFile(string path)
        {
            return Stream.Null;
        }

        public DateTimeOffset GetLastModified(string path)
        {
            return DateTimeOffset.MinValue;
        }

        public DateTimeOffset GetCreated(string path)
        {
            return DateTimeOffset.MinValue;
        }
 
        public DateTimeOffset GetLastAccessed(string path)
        {
            return DateTimeOffset.MinValue;
        }

        public void MakeFileWritable(string path)
        {
            // Nothing to do here.
        }

        public void MoveFile(string source, string destination)
        {
        }
    }
}