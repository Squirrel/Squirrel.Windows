using System.Collections.ObjectModel;
using System.IO;

namespace NuGet
{
    public interface IPackageBuilder : IPackageMetadata
    {
        Collection<IPackageFile> Files { get; }
        void Save(Stream stream);
    }
}
