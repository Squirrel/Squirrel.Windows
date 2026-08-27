| [docs](..)  / [using](.) / squirrel-command-line.md
|:---|

# Squirrel Command Line

Here is a simplified help output specifically around creating releases:

```
Usage: Squirrel.exe command [OPTS]
Creates Squirrel packages

Commands
      --releasify=VALUE      Update or generate a releases directory with a
                               given NuGet package

Options:
  -h, -?, --help             Display Help and exit
  -r, --releaseDir=VALUE     Path to a release directory to use with Releasify
  -p, --packagesDir=VALUE    Path to the NuGet Packages directory for C# apps
      --bootstrapperExe=VALUE
                             Path to the Setup.exe to use as a template
  -g, --loadingGif=VALUE     Path to an animated GIF to be displayed during
                               installation
  -n, --signWithParams=VALUE Sign the installer via SignTool.exe with the
                               parameters given
      --setupIcon=VALUE       Path to an ICO file that will be used for the 
                               Setup executable's icon
  -b  --baseUrl=VALUE         Provides a base URL to prefix the RELEASES file 
                               packages with
      --no-msi                Don't generate an MSI package
      --msi-win64             Mark the MSI as 64-bit, which is useful in
                               Enterprise deployment scenarios
      --no-delta              Don't generate delta packages to save time
      --framework-version=VALUE 
                              Set the required .NET framework version, e.g. net461
```

## See Also
* [Loading GIF](loading-gif.md) - specify a "loading" image during initial install of large applications.
* [Application Signing](application-signing.md) - adding code signing to `Setup.exe` and your application.

---
| Return: [Table of Contents](../readme.md) |
|----|



