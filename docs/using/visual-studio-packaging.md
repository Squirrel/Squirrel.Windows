| [docs](..)  / [using](.) / visual-studio-packaging.md
|:---|

# Visual Studio Build Packaging

Squirrel packaging can be easily integrated directly into your build process using only NuGet and Squirrel directly via commands in your `.csproj` file. This will cause the `Squirrel.exe --releasify` command to be run each time you build a specific configuration of your project.

## Prerequisites

Your solution needs to have the `Squirrel.exe` and `NuGet.exe` tools available. Squirrel has been added previously, you can install `NuGet.exe` with the `NuGet.CommandLine` package.  

```pm
PM>  Install-Package NuGet.CommandLine
```

## Process Overview

This approach defines an `AfterBuild` build target in your `.csproj` file for the configuration you wish to build the packages from (e.g., `Release`). The exec commands do the following:

1. **Package `MyApp.exe` files into `MyApp.nupkg`** - Packages your application files using `NuGet.exe` and a `.nuspec` file. The new file is created in your output directory (e.g., `bin\Release`).
2. **Releasify your `MyApp.nupkg`** - Runs `Squirrel --releasify` on your newly packaged MyApp (e.g., `MyApp.1.0.0.nupkg`) and places the Squirrel `Releases` directory at the solution level.

## Build Target Configuration

The build target configuration example below incorporates the following concepts and techniques:

* **TargetName and TargetPath Macros** - It uses additional [MSBuild Macros](https://msdn.microsoft.com/en-us/library/c02as0cs.aspx) to pull your application and `.nuspec` file names.
* **App Version from `AssemblyInfo.cs`** - It creates a `SemVerNumber` attribute to automatically use the version in your AssemblyInfo.cs file.
* **ItemGroup Include File Wildcards and MSBuild Transforms and MetaData** - It avoids hardcoded `Squirrel.exe` and `NuGet.exe` paths in your `.csproj` by using include wildcards. This way, when you update your Squirrel or NuGet.CommandLine package versions, you won't have to update your `.csproj` file. See [MSBuild Transforms](https://msdn.microsoft.com/en-us/library/ms171476.aspx) and [Item Metadata](https://docs.microsoft.com/en-us/visualstudio/msbuild/msbuild-well-known-item-metadata) for more info.
* **Error Conditions** - Helpful error messages if the exe files aren't found

### Build Target Example

The first step is to define a build target in your `.csproj` file.

**`MyApp.csproj`**

```xml
<Target Name="AfterBuild" Condition=" '$(Configuration)' == 'Release'">
  <GetAssemblyIdentity AssemblyFiles="$(TargetPath)">
    <Output TaskParameter="Assemblies" ItemName="myAssemblyInfo" />
  </GetAssemblyIdentity>
  <ItemGroup>
    <!-- If your .NET version is <3.5 and you get build error, move this ItemGroup outside of Target -->
    <NuGetExe Include="..\packages\NuGet.CommandLine.*\tools\nuget.exe" />
    <SquirrelExe Include="..\packages\Squirrel.Windows.*\tools\squirrel.exe" />
  </ItemGroup>
  <PropertyGroup>
    <ReleaseDir>..\Releases\</ReleaseDir>
    <!-- Extra optional params for squirrel. can be empty -->
    <SquirrelParams>--no-msi</SquirrelParams>
    <SemVerNumber>$([System.Version]::Parse(%(myAssemblyInfo.Version)).ToString(3))</SemVerNumber>
  </PropertyGroup>
  <!-- Add some nice errors for the next person that comes along -->
  <Error Condition="!Exists(%(NuGetExe.FullPath))" Text="You are trying to use the NuGet.CommandLine package, but it is not installed. Please install NuGet.CommandLine from the Package Manager." />
  <Error Condition="!Exists(%(SquirrelExe.FullPath))" Text="You are trying to use the Squirrel.Windows package, but it is not installed. Please install Squirrel.Windows from the Package Manager." />
  <!-- Build nupkg into the project local bin\Release\ directory temporarily -->
  <Exec Command='"%(NuGetExe.FullPath)" pack $(TargetName).nuspec -Version $(SemVerNumber) -OutputDirectory $(OutDir) -BasePath $(OutDir)' />
  <!-- Squirrelify into the release dir (usually at solution level. Change the property above for a different location -->
  <Exec Command='"%(SquirrelExe.FullPath)" --releasify $(OutDir)$(TargetName).$(SemVerNumber).nupkg --releaseDir=$(ReleaseDir) $(SquirrelParams)' />
</Target>
```

## Example .nuspec file

Here is an example `MyApp.nuspec` file for the above build target example. Don't forget to change id tag and file name to match your project name.

```xml
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>MyApp</id>
    <!-- version will be replaced by MSBuild -->
    <version>0.0.0.0</version>
    <description>description</description>
    <authors>authors</authors>
    <dependencies />
  </metadata>
  <files>
    <file src="**\*.*" target="lib\net45\" exclude="*.pdb;*.nupkg;*.vshost.*"/>
  </files>
</package>
```

## Additional Notes

Please be aware of the following when using this solution:

* It suffers from a bug when sometimes NuGet packages are not loaded properly and throws nuget/squirrel is not recogized (9009) errors. **Tip:** In this case you may simply need to restart Visual Studio so the Package Manager Console will have loaded all the package tools
* If you get a `'squirrel' is not recognized as an internal or external command` error, you need to add the full path to `Squirrel.exe` in the build target `Exec Command` call.  
* You can create a separate build configuration for building a package if you wish to avoid running `--releasify` every time you build your `Release` configuration (e.g., create a new `Package` configuration based on `Release`).
* This will not work if your solution uses new `PackageReference` directive instead of `packages.config` file for NuGet dependencies because it will be unable to find nuget.exe and squirrel.exe. In that case change path to executables accordingly.

**Source:** [Issue #630](https://github.com/Squirrel/Squirrel.Windows/issues/630)

---
| Return: [Packaging Tools](packaging-tools.md) |
|----|



