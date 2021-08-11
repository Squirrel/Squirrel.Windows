namespace NuGet
{
    public interface IPackageAssemblyReference : IPackageFile
    {
        string Name
        {
            get;
        }
    }
}
