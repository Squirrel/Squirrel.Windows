namespace NuGet
{
    public interface ICloneableRepository
    {
        IPackageRepository Clone();
    }
}
