using System;
using System.IO;

namespace NuGet
{
    /// <summary>
    /// Represents an empty framework folder in NuGet 2.0+ packages. 
    /// An empty framework folder is represented by a file named "_._".
    /// </summary>
    internal sealed class EmptyFrameworkFolderFile : PhysicalPackageFile
    {
        public EmptyFrameworkFolderFile(string directoryPathInPackage) :
            base(() => Stream.Null)
        {
            if (directoryPathInPackage == null)
            {
                throw new ArgumentNullException("directoryPathInPackage");
            }

            TargetPath = System.IO.Path.Combine(directoryPathInPackage, Constants.PackageEmptyFileName);
        }
    }
}