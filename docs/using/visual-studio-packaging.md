| [docs](..)  / [using](.) / visual-studio-packaging.md
|:---|

# Visual Studio Build Packaging

Squirrel packaging can be easily integrated directly into your build process using only NuGet and Squirrel. 

## Define Build Target

The first step is to define a build target in your `.csproj` file.

```xml
<Target Name="AfterBuild" Condition=" '$(Configuration)' == 'Release'">  <GetAssemblyIdentity AssemblyFiles="$(TargetPath)">    <Output TaskParameter="Assemblies" ItemName="myAssemblyInfo"/>  </GetAssemblyIdentity>  <Exec Command="nuget pack MyApp.nuspec -Version %(myAssemblyInfo.Version) -Properties Configuration=Release -OutputDirectory $(OutDir) -BasePath $(OutDir)" />  <Exec Command="squirrel --releasify $(OutDir)MyApp.%(myAssemblyInfo.Version).nupkg" /></Target>
```

This will generate a NuGet package from .nuspec file setting version from AssemblyInfo.cs and place it in OutDir (by default bin\Release). Then it will generate release files from it.

## Example .nuspec file for MyApp

Here is an example `MyApp.nuspec` file for the above build target example.

```xml
<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">  <metadata>    <id>MyApp</id>    <!-- version will be replaced by MSBuild -->    <version>0.0.0.0</version>    <title>title</title>    <authors>authors</authors>    <description>description</description>    <requireLicenseAcceptance>false</requireLicenseAcceptance>    <copyright>Copyright 2016</copyright>    <dependencies />  </metadata>  <files>    <file src="*.*" target="lib\net45\" exclude="*.pdb;*.nupkg;*.vshost.*"/>  </files></package>
```

## Additional Notes

Please be aware of the following when using this solution:

* Solution needs to have nuget.exe available which can be accomplished by installing `NuGet.CommandLine` package in your solution.  

  ~~~pm
PM>  Install-Package NuGet.CommandLine
  ~~~
* It suffers from a bug when sometimes NuGet packages are not loaded properly and throws nuget/squirrel is not recogized (9009) errors.  
 **Tip:** In this case you may simply need to restart Visual Studio so the Package Manager Console will have loaded all the package tools
* If you get the following error you may need add the full path to squirrel.exe in the build target `Exec Command` call. `'squirrel' is not recognized as an internal or external command`

**Source:** [Issue #630](https://github.com/Squirrel/Squirrel.Windows/issues/630)

---
| Return: [Packaging Tools](packaging-tools.md) |
|----|



