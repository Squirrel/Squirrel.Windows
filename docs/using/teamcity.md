| [docs](..)  / [using](.) / teamcity.md
|:---|


# Team City Packaging

Here are two alternate approaches to using TeamCity to package and releasify your app. 

## Packaging and Releasify as TeamCity Build Steps
When TeamCity pulls down your code, the squirrel.exe will sit under packages if it was added to your solution using NuGet.1. Add a NuGet Pack process which will create the .nupkg based on a .nuspec file to ensure the package is correct.2. Create a command line build process and add the following:~~~%system.teamcity.build.workingDir%\packages\squirrel.windows.1.4.0\tools\squirrel --releasify [BUILD_SERVER_NUPKG_PATH]\%system.build.number%.nupkg -r [OUTPUT_PATH]~~~**Note:** Paths may vary depending on your structure so make sure to update the path information above correctly.

This will cause the appropriate files to be created just as if you had run it from the Package Manager Console.

**Source:** [Issue #737](https://github.com/Squirrel/Squirrel.Windows/issues/737)

## MSBuild AfterBuild Actions with TeamCity

TeamCity can also package and releasify your app if you are using the [Visual Studio Build Packaging](visual-studio-packaging.md) approach. 

**Be aware**, if your TeamCity build agent is running as the `System` account, you may encounter an error during the build with the following symptoms:

1. The build fails with the following error:
```
[Exec] C:\BuildAgent\work\689c651b5369f733\src\SquirrelApp.csproj(166, 5): error MSB3073: The command ""C:\BuildAgent\work\689c651b5369f733\packages\squirrel.windows.1.7.8\tools\Squirrel.exe" --releasify C:\BuildAgent\work\<buildnumber>\src\bin\Package\SquirrelApp.0.1.0.nupkg" exited with code -1.
```

2. Inspecting the `SquirrelSetup.log` on the actual build machine under `C:\BuildAgent\work\<buildnumber>\packages\squirrel.windows.1.7.8\tools` with the following message. 

```
...
2017-10-31 20:08:30> Utility: Failed to extract file C:\BuildAgent\work\<buildnumber>\src\Releases\SquirrelApp-0.1.0-full.nupkg to C:\Windows\system32\config\systemprofile\AppData\Local\SquirrelTemp\tempa
7-Zip [64] 16.04 : Copyright (c) 1999-2016 Igor Pavlov : 2016-10-04

Scanning the drive:
0 files, 0 bytes
...
```

**Solution:** Add a `SQUIRREL_TEMP` environment variable in TeamCity to point to a temp directory in the teamcity build path  (i.e., add `env.SQUIRREL_TEMP` with a value of `%teamcity.build.workingDir%\SquirrelTemp`). You can add an environment variable to the build process under the TeamCity Build Configurations > Parameters section. 


## See Also

* [Packaging Tools](packaging-tools.md) - list of packaging tools to simplify and/or automate the packaging process.

---
| Return: [Table of Contents](../readme.md) |
|----|

