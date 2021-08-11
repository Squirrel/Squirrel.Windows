namespace NuGet
{
    public class PackageWalkInfo
    {
        public PackageWalkInfo(PackageTargets initialTarget)
        {
            InitialTarget = initialTarget;
            Target = initialTarget;
        }

        public PackageTargets InitialTarget
        {
            get;
            private set;
        }

        public PackageTargets Target
        {
            get;
            set;
        }

        public IPackage Parent
        {
            get;
            set;
        }

        public override string ToString()
        {
            return "Initial Target:" + InitialTarget + ", Current Target: " + Target + ", Parent: " + Parent;
        }
    }
}
