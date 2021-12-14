using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Squirrel.NuGet
{
    internal interface IFrameworkTargetable
    {
        IEnumerable<string> SupportedFrameworks { get; }
    }

    internal class PackageDependency
    {
        public PackageDependency(string id)
            : this(id, versionSpec: null)
        {
        }

        public PackageDependency(string id, string versionSpec)
        {
            if (String.IsNullOrEmpty(id)) {
                throw new ArgumentException("Argument_Cannot_Be_Null_Or_Empty", "id");
            }
            Id = id;
            VersionSpec = versionSpec;
        }

        public string Id {
            get;
            private set;
        }

        public string VersionSpec {
            get;
            private set;
        }

        public override string ToString()
        {
            if (VersionSpec == null) {
                return Id;
            }

            return Id + " " + VersionSpec;
        }
    }

    internal class PackageDependencySet : IFrameworkTargetable
    {
        private readonly string _targetFramework;
        private readonly ReadOnlyCollection<PackageDependency> _dependencies;

        public PackageDependencySet(string targetFramework, IEnumerable<PackageDependency> dependencies)
        {
            if (dependencies == null) {
                throw new ArgumentNullException("dependencies");
            }

            _targetFramework = targetFramework;
            _dependencies = new ReadOnlyCollection<PackageDependency>(dependencies.ToList());
        }

        public string TargetFramework {
            get {
                return _targetFramework;
            }
        }

        public ICollection<PackageDependency> Dependencies {
            get {
                return _dependencies;
            }
        }

        public IEnumerable<string> SupportedFrameworks {
            get {
                if (TargetFramework == null) {
                    yield break;
                }

                yield return TargetFramework;
            }
        }
    }

    internal class FrameworkAssemblyReference : IFrameworkTargetable
    {
        public FrameworkAssemblyReference(string assemblyName)
            : this(assemblyName, Enumerable.Empty<string>())
        {
        }

        public FrameworkAssemblyReference(string assemblyName, IEnumerable<string> supportedFrameworks)
        {
            if (String.IsNullOrEmpty(assemblyName)) {
                throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, "Argument_Cannot_Be_Null_Or_Empty", "assemblyName"));
            }

            if (supportedFrameworks == null) {
                throw new ArgumentNullException("supportedFrameworks");
            }

            AssemblyName = assemblyName;
            SupportedFrameworks = supportedFrameworks;
        }

        public string AssemblyName { get; private set; }
        public IEnumerable<string> SupportedFrameworks { get; private set; }
    }
}