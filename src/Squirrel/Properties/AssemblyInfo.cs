using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// All other assembly info is defined in SharedAssembly.cs

[assembly: AssemblyTitle("Squirrel")]
[assembly: AssemblyProduct("Squirrel")]
[assembly: AssemblyDescription("Squirrel")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.

[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("3c25a7f9-3e99-4556-aba3-f820c74bb4da")]

[assembly: InternalsVisibleTo("Squirrel.Tests")]
[assembly: InternalsVisibleTo("Update")]
[assembly: InternalsVisibleTo("SyncReleases")]
