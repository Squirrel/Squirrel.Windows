using System.Runtime.Versioning;

namespace NuGet
{
    public static class ProjectManagerExtensions
    {
        public static FrameworkName GetTargetFrameworkForPackage(this IProjectManager projectManager, string packageId)
        {
            if (projectManager == null)
            {
                return null;
            }

            FrameworkName targetFramework = null;

            var packageReferenceRepository = projectManager.LocalRepository as IPackageReferenceRepository;
            if (packageReferenceRepository != null)
            {
                targetFramework = packageReferenceRepository.GetPackageTargetFramework(packageId);
            }

            if (targetFramework == null && projectManager.Project != null)
            {
                targetFramework = projectManager.Project.TargetFramework;
            }

            return targetFramework;
        }
    }
}
