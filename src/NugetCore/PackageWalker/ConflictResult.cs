namespace NuGet
{
    /// <summary>
    /// Stores information about a conflict during an install.
    /// </summary>
    public class ConflictResult
    {
        public ConflictResult(IPackage conflictingPackage, IPackageRepository repository, IDependentsResolver resolver)
        {
            Package = conflictingPackage;
            Repository = repository;
            DependentsResolver = resolver;
        }

        public IPackage Package
        {
            get;
            private set;
        }

        public IPackageRepository Repository
        {
            get;
            private set;
        }

        public IDependentsResolver DependentsResolver
        {
            get;
            private set;
        }
    }
}
