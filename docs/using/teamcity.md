| [docs](..)  / [using](.) / teamcity.md
|:---|


# Team City Packaging


## Adding the Packaging Step
When TeamCity pulls down your code, the squirrel.exe will sit under packages if it was added to your solution using NuGet.1. Add a NuGet Pack process which will create the .nupkg based on a .nuspec file to ensure the package is correct.2. Create a command line build process and add the following:~~~%system.teamcity.build.workingDir%\packages\squirrel.windows.1.4.0\tools\squirrel --releasify [BUILD_SERVER_NUPKG_PATH]\%system.build.number%.nupkg -r [OUTPUT_PATH]~~~**Note:** Paths may vary depending on your structure so make sure to update the path information above correctly.

This will cause the appropriate files to be created just as if you had run it from the Package Manager Console.

**Source:** [Issue #737](https://github.com/Squirrel/Squirrel.Windows/issues/737)

## See Also

* [Packaging Tools](packaging-tools.md) - list of packaging tools to simplify and/or automate the packaging process.

---
| Return: [Table of Contents](../readme.md) |
|----|

