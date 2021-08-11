namespace NuGet
{
    public interface ILatestPackageLookup
    {
        bool TryFindLatestPackageById(string id, out SemanticVersion latestVersion);
        bool TryFindLatestPackageById(string id, bool includePrerelease, out IPackage package);
    }
}