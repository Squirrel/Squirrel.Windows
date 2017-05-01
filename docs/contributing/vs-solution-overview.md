| [docs](..)  / [contributing](.) / vs-solution-overview.md
|:---|

# Visual Studio Solution Overview

An overview of the various projects in the Squirrel.Windows Visual Studio solution and how they relate to different aspects of the update process.


| Project / Assembly Name | Libraries (NuGet) | Libraries (NuGet) | Releases Directory (releasify output) | MyApp (install location) |
|--------------------------------|---------|-----|------------------|-------------| 
| **Core** NuGet.Squirrel.dll | NuGet.Squirrel.dll | | | |
| **Squirrel** Squirrel.dll | Squirrel.dll | | | |
| **SyncRelease** SyncRelease.exe |           | SyncRelease.exe | | |
| **Update** Update.exe          |           | Squirrel.exe |             | Update.exe    |
| **Setup**  Setup.exe |           | Setup.exe | Setup.exe (+MyApp.Latest.nupkg) | |
| **WriteZipToSetup** WriteZipToSetup.exe  |           | WriteZipToSetup.exe | | |

* **Project / Assembly Name**: Solution project name (from Squirrel.sln) and output assembly name.
* **Libraries (NuGet)**: Program libraries installed added as references in your MyApp solution when adding the Squirrel.Windows NuGet package to your project.
* **Libraries (NuGet)**: Executable tools included in the Squirrel.Windows NuGet package used to prepare deployments via the Package Manager Console (e.g., Squirrel.exe).
* **Releases Directory (releasify output)**: Directory where the Squirrel --releasify process outputs the packages and Setup application for your project (e.g., MyAppSourceCode/Releases).
* **MyApp (install location)**: MyApp install directory (e.g., %LOCALAPPDATA%\MyApp) where the application is actually installed and updated via Squirrel.Windows.

**Note**: Note that the Squirrel.exe application found in the tools directory of the Squirrel.Windows NuGet package is actually a renamed version of the Update.exe application (see Squirrel.Windows\src\Squirrel.nuspec) 

---
| Return: [Table of Contents](../readme.md) |
|----|

