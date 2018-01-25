| [docs](..)  / [using](.) / squirrel-command-line.md
|:---|

# Squirrel Command Line

Here is a simplified help output specifically around creating releases:

```
Usage: Squirrel.exe command [OPTS]
Creates Squirrel packages

Commands
      --install=VALUE        Install the app whose package is in the specified
                               directory
      --uninstall            Uninstall the app the same dir as Update.exe
      --download=VALUE       Download the releases specified by the URL and
                               write new results to stdout as JSON
      --checkForUpdate=VALUE Check for one available update and writes new
                               results to stdout as JSON
      --update=VALUE         Update the application to the latest remote
                               version specified by URL
      --releasify=VALUE      Update or generate a releases directory with a
                               given NuGet package
      --createShortcut=VALUE Create a shortcut for the given executable name
      --removeShortcut=VALUE Remove a shortcut for the given executable name
      --updateSelf=VALUE     Copy the currently executing Update.exe into the
                               default location

Options:
  -h, -?, --help             Display Help and exit
  -r, --releaseDir=VALUE     Path to a release directory to use with releasify
  -p, --packagesDir=VALUE    Path to the NuGet Packages directory for C# apps
      --bootstrapperExe=VALUE
                             Path to the Setup.exe to use as a template
  -g, --loadingGif=VALUE     Path to an animated GIF to be displayed during
                               installation
  -i, --icon=VALUE           Path to an ICO file that will be used for icon
                               shortcuts
      --setupIcon=VALUE      Path to an ICO file that will be used for the
                               Setup executable's icon
  -n, --signWithParams=VALUE Sign the installer via SignTool.exe with the
                               parameters given
  -s, --silent               Silent install
  -l, --shortcut-locations=VALUE
                             Comma-separated string of shortcut locations, e.g.
                               'Desktop,StartMenu'
      --no-msi               Don't generate an MSI package
      --no-delta             Don't generate delta packages to save time
      --framework-version=VALUE
                             Set the required .NET framework version, e.g.
                               net461
```

## See Also
* [Loading GIF](loading-gif.md) - specify a "loading" image during initial install of large applications.
* [Application Signing](application-signing.md) - adding code signing to `Setup.exe` and your application.

---
| Return: [Table of Contents](../readme.md) |
|----|



