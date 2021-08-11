using System;
using System.Globalization;
namespace NuGet
{
    public enum PackageOperationTarget
    {
        PackagesFolder,
        Project
    }

    public class PackageOperation
    {
        public PackageOperation(IPackage package, PackageAction action)
        {
            Package = package;
            Action = action;
            Target = PackageOperationTarget.Project;
        }

        public IPackage Package
        {
            get;
            private set;
        }

        public PackageAction Action
        {
            get;
            private set;
        }

        public PackageOperationTarget Target
        {
            get;
            set;
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} {2}",
                Action == PackageAction.Install ? "+" : "-",
                Package.Id,
                Package.Version);
        }

        public override bool Equals(object obj)
        {
            var operation = obj as PackageOperation;
            return operation != null &&
                   operation.Action == Action &&
                   operation.Package.Id.Equals(Package.Id, StringComparison.OrdinalIgnoreCase) &&
                   operation.Package.Version.Equals(Package.Version);
        }

        public override int GetHashCode()
        {
            var combiner = new HashCodeCombiner();
            combiner.AddObject(Package.Id);
            combiner.AddObject(Package.Version);
            combiner.AddObject(Action);

            return combiner.CombinedHash;
        }
    }
}
