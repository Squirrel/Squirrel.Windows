#if VS14
using Microsoft.VisualStudio.ProjectSystem.Interop;

namespace NuGet
{
    public class NuGetPackageMoniker : INuGetPackageMoniker
    {
        public string Id
        {
            get;
            set;
        }

        public string Version
        {
            get;
            set;
        }
    }
}
#endif
