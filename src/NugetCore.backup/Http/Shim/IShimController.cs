
using System;

namespace NuGet
{
    public interface IShimController : IDisposable
    {
        void Enable(IPackageSourceProvider sourceProvider);

        void UpdateSources();

        void Disable();
    }
}
