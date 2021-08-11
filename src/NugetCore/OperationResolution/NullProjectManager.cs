using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Resolver
{
    public class NullProjectManager : IProjectManager
    {
        IPackageRepository _localRepository;
        IProjectSystem _project;        

        public NullProjectManager(IPackageManager packageManager)
        {
            _localRepository = new VirtualRepository(repo: null);
            _project = new NullProjectSystem();
            PackageManager = packageManager;
        }

        public IPackageRepository LocalRepository
        {
            get { return _localRepository; }
        }

        public IPackageManager PackageManager
        {
            get;
            private set;
        }

        public ILogger Logger
        {
            get;
            set;
        }

        public IProjectSystem Project
        {
            get { return _project; }
        }

        public IPackageConstraintProvider ConstraintProvider
        {
            get { return NullConstraintProvider.Instance; }
            set 
            { 
                // no-op 
            }
        }

        // Disable warnings that those events are never used since this is intentional.
#pragma warning disable 0067
        public event EventHandler<PackageOperationEventArgs> PackageReferenceAdded;

        public event EventHandler<PackageOperationEventArgs> PackageReferenceAdding;

        public event EventHandler<PackageOperationEventArgs> PackageReferenceRemoved;

        public event EventHandler<PackageOperationEventArgs> PackageReferenceRemoving;
#pragma warning restore 0067

        public void Execute(PackageOperation operation)
        {
            // no-op
        }
    }

    class NullProjectSystem : IProjectSystem
    {
        public System.Runtime.Versioning.FrameworkName TargetFramework
        {
            get { return null; }
        }

        public string ProjectName
        {
            get { return "NullProject"; }
        }

        public void AddReference(string referencePath)
        {
            throw new NotImplementedException();
        }

        public void AddFrameworkReference(string name)
        {
            throw new NotImplementedException();
        }

        public bool ReferenceExists(string name)
        {
            throw new NotImplementedException();
        }

        public void RemoveReference(string name)
        {
            throw new NotImplementedException();
        }

        public bool IsSupportedFile(string path)
        {
            throw new NotImplementedException();
        }

        public string ResolvePath(string path)
        {
            throw new NotImplementedException();
        }

        public bool IsBindingRedirectSupported
        {
            get { return false; }
        }

        public void AddImport(string targetFullPath, ProjectImportLocation location)
        {
            throw new NotImplementedException();
        }

        public void RemoveImport(string targetFullPath)
        {
            throw new NotImplementedException();
        }

        public bool FileExistsInProject(string path)
        {
            throw new NotImplementedException();
        }

        public ILogger Logger
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public string Root
        {
            get { throw new NotImplementedException(); }
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetFiles(string path, string filter, bool recursive)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetDirectories(string path)
        {
            throw new NotImplementedException();
        }

        public string GetFullPath(string path)
        {
            throw new NotImplementedException();
        }

        public void DeleteFile(string path)
        {
            throw new NotImplementedException();
        }

        public void DeleteFiles(IEnumerable<IPackageFile> files, string rootDir)
        {
            throw new NotImplementedException();
        }

        public bool FileExists(string path)
        {
            throw new NotImplementedException();
        }

        public bool DirectoryExists(string path)
        {
            throw new NotImplementedException();
        }

        public void AddFile(string path, Stream stream)
        {
            throw new NotImplementedException();
        }

        public void AddFile(string path, Action<Stream> writeToStream)
        {
            throw new NotImplementedException();
        }

        public void AddFiles(IEnumerable<IPackageFile> files, string rootDir)
        {
            throw new NotImplementedException();
        }

        public void MakeFileWritable(string path)
        {
            throw new NotImplementedException();
        }

        public void MoveFile(string source, string destination)
        {
            throw new NotImplementedException();
        }

        public Stream CreateFile(string path)
        {
            throw new NotImplementedException();
        }

        public Stream OpenFile(string path)
        {
            throw new NotImplementedException();
        }

        public DateTimeOffset GetLastModified(string path)
        {
            throw new NotImplementedException();
        }

        public DateTimeOffset GetCreated(string path)
        {
            throw new NotImplementedException();
        }

        public DateTimeOffset GetLastAccessed(string path)
        {
            throw new NotImplementedException();
        }

        public dynamic GetPropertyValue(string propertyName)
        {
            throw new NotImplementedException();
        }
    }
}
