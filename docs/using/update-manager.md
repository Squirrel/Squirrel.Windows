| [docs](..)  / [using](.) / update-manager.md
|:---|

# Update Manager Reference

## Basic Updating

The "Easy Mode" method that does everything all in one go.

* `UpdateApp` - downloads and updates the app to the latest version. 

## Advanced Updating

The following methods are provided to allow you to have more control of the update process (i.e., to interact with app updates and apply them if desired).

* `CheckForUpdate` - checks on the server if there are updates available. Returns an `UpdateInfo` object that contains information about any pending updates.

* `DownloadReleases` - downloads release files (the `nupkg` file deltas) from the server to the local machine

* `ApplyReleases` - installs the downloaded packages, and returns the new `app-[version]` directory path.

### UpdateInfo

The `UpdateInfo` class contains information about available and installed releases.

~~~cs
public class UpdateInfo
{
	public ReleaseEntry CurrentlyInstalledVersion;
	public ReleaseEntry FutureReleaseEntry;
	public List<ReleaseEntry> ReleasesToApply;
}
~~~

### ReleaseEntry

The `ReleaseEntry` class contains the specifics of each release.

~~~cs
public interface ReleaseEntry
{
    public string SHA1;
    public string Filename;
    public long Filesize;
    public bool IsDelta;
}
~~~


## See Also
* [Update Process](update-process.md) - overview of the steps in the update process.
* [GitHub Update Manager](github.md) - process of using `GitHubUpdateManager`.

---
| Return: [Table of Contents](../readme.md) |
|----|

