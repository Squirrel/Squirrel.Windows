namespace NuGet
{
    public enum PackageAction
    {
        Install,
        Uninstall,

        // For project level packages, Update is the same as Install. 
        // For solution level packages, there are different since multiple versions
        // of the same solution level package can be installed.
        Update
    }
}
