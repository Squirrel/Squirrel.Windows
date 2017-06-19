| [docs](..)  / [using](.) / visual-studio-packaging.md
|:---|

# Visual Studio Build Packaging

Squirrel packaging can be easily integrated directly into your build process using only NuGet and Squirrel. 

## Define Build Target

The first step is to define a build target in your `.csproj` file.

```xml
<Target Name="AfterBuild" Condition=" '$(Configuration)' == 'Release'">
  <GetAssemblyIdentity AssemblyFiles="$(TargetPath)">
    <Output TaskParameter="Assemblies" ItemName="myAssemblyInfo"/>
  </GetAssemblyIdentity>
  <Exec Command="nuget pack $(MSBuildProjectName).nuspec -Version %(myAssemblyInfo.Version) -Properties Configuration=Release -OutputDirectory $(OutDir) -BasePath $(OutDir)" />
  <Exec Command="squirrel --releasify $(OutDir)$(MSBuildProjectName).%(myAssemblyInfo.Version).nupkg" />
</Target>
```

This will generate a NuGet package from .nuspec file setting version from AssemblyInfo.cs and place it in OutDir (by default bin\Release). Then it will generate release files from it.

## Example .nuspec file for MyApp

Here is an example `MyApp.nuspec` file for the above build target example.

```xml
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>MyApp</id>
    <!-- version will be replaced by MSBuild -->
    <version>0.0.0.0</version>
    <title>title</title>
    <authors>authors</authors>
    <description>description</description>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <copyright>Copyright 2016</copyright>
    <dependencies />
  </metadata>
  <files>
    <file src="*.*" target="lib\net45\" exclude="*.pdb;*.nupkg;*.vshost.*"/>
  </files>
</package>
```

We use `nuget pack MyApp.nuspec -Version ...` instead of usual `nuget pack MyApp.csproj` to prevent it from including project dependencies into the package.

## Additional Notes

MSBuild needs to be able to find `nuget.exe` and `squirrel.exe` which can be accomplished in one of the following ways:
* Using full paths to exe files in your `Exec` commands
* Including paths to executables in your system PATH variable
* Visual Studio package manager can automatically find tool executables from installed NuGet packages and include them into local environment path. To do this:
  * Install `NuGet.CommandLine` package in your solution to get nuget.exe: `PM>  Install-Package NuGet.CommandLine`
  * Open Package Manager Console window to scan solution for executable tools. You have to open this window each time you restart VS. Sometimes VS forgets paths to tools after some time due to a bug - in that case you need to reopen your solutuon.

If you get Error 9009: `'squirrel/nuget' is not recognized as an internal or external command` that means MSBuild can't find nuget.exe or squirrel.exe.

---
| Return: [Packaging Tools](packaging-tools.md) |
|----|



