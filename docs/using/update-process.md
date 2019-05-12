| [docs](..)  / [using](.) / update-process.md
|:---|


# Update Process

The following steps are performed by the `UpdateManager` each time your app is executed:

1. **Check for Updates** - the `RELEASES` file at the distribution location is downloaded and compared to local `RELEASES` file to check for any updates.
2. **Download & Verify Update Packages** - if there is a new release, the `UpdateManager` determines whether to download the deltas or the latest full package (by calculating which one requires less total downloading) to update to the current version. The packages are compared against their SHA1 in the `RELEASES` file for verification.
3. **Build Full Package from Deltas** - if delta packages were downloaded, a new full package is created from the previous full package and the downloaded delta file.
3. **Install New Version** - the current version of MyApp is extracted from the full package and placed in a new `%LocalAppData%\MyApp` install directory based on the version number (e.g., `app-1.0.1`).
4. **Update Shortcuts** - desktop and Windows Start Menu shortcuts are updated to point to the new MyApp version (via the `--processStart` command line parameter passed to `Update.exe`).
5. **Previous Version Clean-up** - on the next startup of MyApp, all but current and immediately previous version of your app are deleted as part of clean up (e.g., after updating to app-1.0.5, app-1.0.4 will remain, but app-1.0.3 and before will be deleted - see [issue #589](https://github.com/Squirrel/Squirrel.Windows/issues/589)). 

## Rollback

Currently, there is no built-in support for rolling back to a previous version.

## See Also

* [Update Manager](update-manager.md) - reference guide for the `UpdateManager`. 
* [Debugging Updates](debugging-updates.md) - tips on debugging your Squirrel application.


---
| Return: [Table of Contents](../readme.md) |
|----|

