| [docs](..)  / [using](.) / install-process.md
|:---|

# Install Process

This section goes into detail about the install process.

## Setup.exe 

`Setup.exe` is a C++ bootstrapper application used to install your app on the user's local system. It is generated as part of the `Squirrel --releasify` process.

The `Setup.exe` file includes the `Update.exe` application and the latest version of the MyApp package to be installed. A single executable file can be provided due to the `WriteZipToSetup.exe` tool injecting the appropriate files into the exe. 

In addition, the `Setup.exe` will also ensure that .NET 4.5 is installed on the user's computer.

## Install Location

The `Setup.exe`, and later the `UpdateManager` in MyApp must have the ability to write files to and execute files from the application install location. To ensure permission for all types of users, the user's application data directory is selected as the install location (i.e., `%LocalAppData%\MyApp`).

The installation root really only needs to consist of two types of folders:

* **Packages** - folder used to download and assemble the update package files.
* **App Folders** - the "installed" application files for a given version of MyApp.

```
\%LocalAppData%
   \packages
      MyApp-1.0.0.nupkg
      MyApp-1.0.1-delta.nupkg
      MyApp-1.0.1.nupkg   
   \app-1.0.0
      MyApp.exe
   \app-1.0.1
      MyApp.exe
```

The packages directory is effectively immutable, it simply consists of the packages we've downloaded. Using the user's local application data directory means that we the needed write-access to the install directory on a per-user basis. 

**Tip:** See [Machine-wide Installs](machine-wide-installs.md) for more information on ensuring your application pushed to all users in an enterprise environment. 

## Install Process Overview

The `Setup.exe` application preforms the following:

1. **Ensures .NET Framework Installed** - determines if .NET Framework is available, and if not relaunches itself with `/installfx45` to download and launch the .NET Framework installer.
1. **Create `%LocalAppData%\MyApp` Directory** - creates a directory for the MyApp to be installed.
2. **Extracts `Update.exe`** - extracts the `Update.exe` application to the application directory (`%LocalAppData%\MyApp`).
3. **Extracts `MyApp.1.0.0-full.nupkg`** - extracts the MyApp full application package to the  `%LocalAppData%\MyApp\packages\temp` directory.
4. **Executes `Update.exe` to Finish Install** - executes the `Updater.exe` application with the `/install` switch to finish the application installation and then launch the application.
    1. **Copy MyApp to `app-1.0.0` Directory** - copy the full version of MyApp files to a application sub-directory (e.g., `MyApp\app-1.0.0`). 
    2. **Launch MyApp** - at the end of the setup process, the Updater launches the  newly installed version of MyApp.
6. **MyApp Creates Shortcuts** - the first execution of the application will cause shortcuts to be created on the desktop and Windows start menu for MyApp. 

## Desktop & Windows Start Shortcuts

By default, application shortcuts are created on the desktop and the Windows Start menu that point to the `Update.exe` application with additional arguments pointing to the correct application to execute.

**`MyApp.lnk` (Application Shortcut)**

* **Target:** `C:\Users\kbailey\AppData\Local\MyApp\Update.exe --processStart MyApp.exe`
* **Start in:** `C:\Users\kbailey\AppData\Local\MyApp\app-1.0.0`


## See Also

* [Loading GIF](loading-gif.md) - specify a "loading" image during initial install of large applications.
* [Machine-wide Installs](machine-wide-installs.md) - generating an MSI file suitable for installation via Group Policy.
* [NuGet Package Metadata](using/nuget-package-metadata.md) - overview of the NuGet metadata and its uses by Squirrel.

---
| Return: [Table of Contents](../readme.md) |
|----|

