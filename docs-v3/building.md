# Building Squirrel

You can compile Squirrel on the command line, or via an alternative IDE like VSC or Rider, but you will need VS2022 installed, or you will need to install all of Squirrel's VS requirements separately.

## Prerequisites
- Be on Windows 10/11
- [Visual Studio 2022](https://visualstudio.microsoft.com/vs/)
  - **Workload**: Desktop development with C++
    - **Individual**: MSVC v143 x86 / x64
    - **Individual**: C++ MFC for v143 (x86 & x64)
  - **Workload**: .NET desktop development
    - **Individual**: .NET Framework 4.6.1 SDK
    - **Individual**: .NET Framework 4.6.1 targeting pack
- [dotnet 5.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/5.0)
- [dotnet 6.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

## Components
- `/vendor` - third party binaries
- `/src/Squirrel` - core Squirrel.Core.dll library, used by installed applications to update. Packaged as .nupkg
- `/src/Squirrel.Tool` - csq locator tool, for building releases. Packaged as .nupkg (dotnet tool)
- `/src/Squirrel.CommandLine` - used by csq
- `/src/Setup` - windows Setup.exe. This is a small self-extractor that runs Update.Windows.
- `/src/StubExecutable` - windows stubs (for running installed apps)
- `/src/Update.Windows` - windows Update.exe, used by installed apps to install updates. Published as single-file exe.
- `/src/Update.OSX` - UpdateMac, used by apps on macOS to install updates. Published as single-file exe.

## Compiling on the command line

There is a one-command build script. Use this to test if you are unable to build with Visual Studio, or when distributing squirrel.
> ./build.cmd

This script will compile the whole solution, package the single-file projects (Update.Windows & Update.OSX) and bundle all the required files into two nupkg's. The two complete nupkg's will be placed in `/build/Release`.

## Compiling in your favorite IDE

This should be fairly straight forward. Open the solution file, run the build-all command (usually F6). This will compile all the Squirrel binaries, and will create nupkg's in `/build/Debug`. 

Note that these packages generated on build are incomplete, as they will not contain `Update.exe` or `UpdateMac`. You will need to run the build script if you wish to create a package intended for release.

## Debugging Squirrel

You can run any of the Squirrel projects in your debugger with the appropriate command line arguments to find issues or test how Squirrel works. Below is examples on how you might debug various features of squirrel.

[If you are looking for help troubleshooting an installed application, that is available here.](troubleshooting.md)

### Package Creation
- Debug csq (src/Squirrel.Tool) with the argument `--csq-embedded` and any other arguments required to create your package. Example:
  > `csq --csq-embedded pack -u MyTestApp -v 1.0.1 -p "path-to/files"`

### Debug Windows Extractor (Setup.exe)
- Compile Setup.exe (src/Setup) in Debug mode
- Attach a package to this binary with csq and the `--debugSetupExe` argument. Example:
  > `csq --csq-embedded pack -u MyTestApp -v 1.0.1 -p "path-to/files" --debugSetupExe "path-to/Setup.exe"`
- Run debugger for Setup.exe (without re-compiling)

### Windows Install (Partial)

- Create a update package (-full.nupkg) using csq (src/Squirrel.Tool).
- Debug Update.exe (src/Update.Windows) with the arguments `"--install {directoryContainingNupkg}"`

### Windows Install (Full)

- Create a Setup bundle using csq (src/Squirrel.Tool). Note the path to generaged Setup.exe, and the `setupBundleOffset` which is printed to the console.
- Debug Update.exe (src/Update.Windows) with the arguments `"--setup {setupExePath} --setupOffset {setupBundleOffset}"`

## Distributing 

Before distributing a nupkg to end users, it is necessary to code-sign `UpdateMac` with a valid Apple developer certificate, and send it to Apple for notarization. This will need to be performed on a computer running macOS after the nupkg's has been created.