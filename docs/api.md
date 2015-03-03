## Squirrel.dll

This is the assembly your project should reference in order to use Squirrel in-app updates functionality.

### UpdateManager

Main entry point for using Squirrel functionality. Here's an example of how to create one:

```cs
using (var mgr = new UpdateManager(
    @"http://your-server/releases", "your-nuget-package-id",
    FrameworkVersion.Net45))
{
    // Use updateManager
}
```

### Methods for managing updates

These methods are the primary methods you'll use to interact with app updates and apply them.

* `UpdateApp`: Downloads and updates the app to the latest version. This method is the "Easy Mode" method that does everything all in one go.

* `CheckForUpdate`: Checks on the server if there are updates available. Returns an `UpdateInfo` object that contains information about any pending updates.

* `DownloadReleases`: Downloads release files (the `nupkg` file deltas) from the server to the local machine

* `ApplyReleases` Installs the downloaded packages, and returns the new `app-[version]` directory path.

### Methods for helping you to set up your app

These methods help you to set up your application in Squirrel events - if you're not using custom Squirrel events, you probably don't need to call these methods.

* `[Create/Remove]ShortcutsForExecutable`: Creates and removes shortcuts on the desktop or in the Start Menu.

* `[Create/Remove]UninstallerRegistryEntry`: Creates and removes the uninstaller entry. Normally called by `Update.exe`.

### UpdateInfo

Contains information about available and installed releases.

* `ReleaseEntry CurrentlyInstalledVersion`
* `ReleaseEntry FutureReleaseEntry`
* `public string PackageDirectory`
* `List<ReleaseEntry> ReleasesToApply`

### ReleaseEntry

Contains the specifics of each release.

* `string SHA1`
* `string Filename`
* `long Filesize`
* `bool IsDelta`
