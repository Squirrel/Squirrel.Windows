
namespace NuGet
{
    public interface IVersionSpec
    {
        SemanticVersion MinVersion { get; }
        bool IsMinInclusive { get; }
        SemanticVersion MaxVersion { get; }
        bool IsMaxInclusive { get; }
    }
}
