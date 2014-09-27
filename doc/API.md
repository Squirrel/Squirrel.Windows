# API

## Squirrel.Client.dll

This is the assembly your project should reference in order to use Squirrel in-app updates functionality.

### UpdateManager

Main entry point for using Squirrel functionality. Here's an example of how to create one:

```csharp
using(var updateManager = new UpdateManager(
    @"http://your-server/releases",
    "YourAppName",
    FrameworkVersion.Net45
    ))
{
    // Use updateManager
}
```

#### Methods

* `Task<ReleaseEntry> UpdateApp()`: Downloads and updates the app to the latest version. 
* `Task<UpdateInfo> CheckForUpdate()`: Checks on the server if there are updates available. Returns an `UpdateInfo` object that contains information about pending updates if there are any, and null if there aren't.
* `Task DownloadReleases()`: Downloads release files (the `nupkg` file deltas) from the server.
* `Task<string> ApplyReleases()` Installs the latest version, and returns the new `app-[version]` directory path.
* `void CreateShortcutsForExecutable(exePath, shortcutLocations, isUpdate)`: Creates shortcuts on the desktop or in Program Files. Pass `true` to isUpdate if ran outside of `--squirrel-install`.
* * `void RemoveShortcutsForExecutable(exePath, shortcutLocations)`: Removes shortcuts created with `CreateShortcutsForExecutable` 

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

