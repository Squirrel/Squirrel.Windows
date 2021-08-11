using System;
using System.IO;

namespace NuGet
{
    public class PhysicalPackageAssemblyReference : PhysicalPackageFile, IPackageAssemblyReference
    {
        public PhysicalPackageAssemblyReference()
        {
        }

        public PhysicalPackageAssemblyReference(PhysicalPackageFile file)
            : base(file)
        {

        }

        public PhysicalPackageAssemblyReference(Func<Stream> streamFactory)
            : base(streamFactory)
        {
        }

        public string Name
        {
            get 
            {
                return String.IsNullOrEmpty(Path) ? String.Empty : System.IO.Path.GetFileName(Path);
            }
        }
    }
}