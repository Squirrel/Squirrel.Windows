namespace NuGet
{
    public interface IFileConflictResolver
    {
        FileConflictResolution ResolveFileConflict(string message);
    }
}