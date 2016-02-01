| [docs](..)  / [using](.) / nuget-package-metadata.md
|:---|

# NuGet Package Metadata

Squirrel uses information from your app's EXE as well as the NuGet package Metadata for the setup and uninstall UI.

* **Id** - name of the application (**no spaces or [dots]**(https://github.com/Squirrel/Squirrel.Windows/issues/523)). Used to name the release packages (e.g., **MyApp**-1.0.0-full.nupkg).
* **Title** - used for the name of the application in the Windows Application Uninstaller.
* **Version** - version specified in `Properties\Assembly.cs`. Used for naming the release package(e.g., MyApp-**1.0.0**-full.nupkg) as well as describing the version number in the Windows Uninstaller (see screenshot below).
* **Icon Url** - url to an icon to be used for the application. Used for the shortcuts and Windows Uninstaller icons.

![](images/uninstall-app.png)

---
| Return: [Table of Contents](../readme.md) |
|----|
