| [docs](..)  / [using](.) / visual-studio-packaging.md
|:---|

# Visual Studio Build Packaging

Squirrel packaging can be easily integrated directly into your build process using only NuGet and Squirrel. 

## Define Build Target

The first step is to define a build target in your `.csproj` file.

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

This will generate a NuGet package from .nuspec file setting version from AssemblyInfo.cs and place it in OutDir (by default bin\Release). Then it will generate release files from it.

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

The example above searches for squirrel.exe and nuget.exe in your nuget `packages` directory. To make nuget.exe available please install `NuGet.CommandLine` package.

```pm
PM>  Install-Package NuGet.CommandLine
```

Note: this will not work if your solution uses new `PackageReference` directive instead of `packages.config` file for NuGet dependencies because it will be unable to find nuget.exe and squirrel.exe. In that case change path to executables accordingly.

**Source:** [Issue #630](https://github.com/Squirrel/Squirrel.Windows/issues/630)

---
| Return: [Packaging Tools](packaging-tools.md) |
|----|



