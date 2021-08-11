namespace NuGet
{
    public interface IPackageConstraintProvider
    {
        string Source { get; }
        IVersionSpec GetConstraint(string packageId);
    }
}
