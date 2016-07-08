| [docs](..)  / [using](.) / octopack.md
|:---|

# Using OctoPack

In order to automatically construct your nuget packages you can use [OctoPack](https://github.com/OctopusDeploy/OctoPack).  Octopack allows you to specify a .nuspec file which will be used to specify how your .nupkg should be created.

Follow the core instructions for creating your .nuspec file on the  [OctoPack](https://github.com/OctopusDeploy/OctoPack) page.

You'll then need to add a files specification to match Squirrel's expected .nupkg structure:

~~~
  <files>
    <file src="bin\Release\*.*" target="lib\net45\" exclude="bin\release\*.pdb;bin\release\*.nupkg;bin\release\*.vshost.*"/>
  </files>
~~~

If you're building using Visual Studio, you will also need to edit your .csproj file to include a property group.

~~~
  <PropertyGroup>
    <RunOctoPack>true</RunOctoPack>
  </PropertyGroup>
~~~

If you're using a build server, see OctoPack's guides on how to trigger it to be run.

---
| Return: [Packaging Tools](packaging-tools.md) |
|----|



