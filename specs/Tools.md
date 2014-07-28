## Scenarios

At the end of the day, here's how a developer will use Squirrel:

1. Add the **Squirrel** package to your application
1. As part of the install for Squirrel, NuGet Package Build is enabled in the csproj file
1. The user edits the generated `.nuspec` to specify some details about their app
1. From the NuGet package console, run `New-Release` - this builds the world, and you end up with a `$SolutionDir/Releases/ASSEMBLYNAME` folder that has both a Squirrel release package as well as a `Setup.exe`

## How does this work:

As part of adding Squirrel to your application, a `targets` file gets added to your csproj file. This targets file dumps all of the references in your application to the output directory in a simple text file, as well as a list of files marked as content.

Calling `New-Release` results in this process being kicked off:

1. Call `$DTE` to build the current project, including the NuGet packages
1. For the current project, run `CreateReleasePackage.exe` to build release and delta packages.
1. Create a Zip file consisting of `update.exe` and the latest full release from `Releases`.
1. Using Win32 API Abuse™, put that into `setup.exe`, a C++ bootstrapper application whose sole goal is to download .NET 4.5, install it, then run update.exe
1. Copy that to the Releases folder.
