using System;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("NuGet.Core")]
[assembly: AssemblyDescription("NuGet.Core is the core framework assembly for NuGet that the rest of NuGet builds upon.")]
[assembly: CLSCompliant(true)]

[assembly: InternalsVisibleTo("NuGet.Test")]
[assembly: InternalsVisibleTo("NuGet.Test.Utility")]
[assembly: InternalsVisibleTo("NuGet.Test.Integration")]
[assembly: InternalsVisibleTo("NuGet.VisualStudio.Test")]
