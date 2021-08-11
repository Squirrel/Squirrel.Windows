
namespace NuGet
{
    public interface IPackageName
    {
        string Id { get; }
        SemanticVersion Version { get; }
    }
}
