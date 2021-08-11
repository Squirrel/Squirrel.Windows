using System.ComponentModel;

namespace NuGet
{
    public class PackageOperationEventArgs : CancelEventArgs
    {
        public PackageOperationEventArgs(IPackage package, IFileSystem fileSystem, string installPath) 
        {
            Package = package;
            InstallPath = installPath;
            FileSystem = fileSystem;
        }

        public string InstallPath { get; private set; }
        public IPackage Package { get; private set; }
        public IFileSystem FileSystem { get; private set; }
    }
}