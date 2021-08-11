namespace NuGet
{
    /// <summary>
    /// Set of known context values to be used in calls to HttpUtility.CreateUserAgentString
    /// </summary>
    public static class RepositoryOperationNames
    {
        public static readonly string OperationHeaderName = "NuGet-Operation";
        public static readonly string DependentPackageHeaderName = "NuGet-DependentPackage";
        public static readonly string DependentPackageVersionHeaderName = "NuGet-DependentPackageVersion";
        public static readonly string PackageId = "NuGet-PackageId";
        public static readonly string PackageVersion = "NuGet-PackageVersion";

        public static readonly string Update = "Update";
        public static readonly string Install = "Install";
        public static readonly string Restore = "Restore";
        public static readonly string Mirror = "Mirror";
        public static readonly string Reinstall = "Reinstall";
    }
}
