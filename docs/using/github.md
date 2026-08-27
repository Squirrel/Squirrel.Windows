| [docs](..)  / [using](.) / github.md
|:---|

# Using GitHub

GitHub release assets can be used to distribute the necessary Squirrel files for the Squirrel install and update process. It still requires you to upload all the release files as assets for each release, but provides you a means of hosting your update files via your GitHub repository.

**Important:** GitHub since February 22, 2018 [only support TLS 1.2 connections](https://githubengineering.com/crypto-removal-notice/). The host application is therefore required to use .NET framework 4.6.1, otherwise TLS 1.1 is the default protocol and check for update won't work. 

## Installing from GitHub

GitHub allows you to provide a [static link](https://help.github.com/articles/linking-to-releases/) to a repositories latest release page. You can direct your users to download the `Setup.exe` from the list of assets you uploaded for the release.

~~~
https://github.com/myuser/MyApp/releases/latest
~~~

**Tip:** This link simply redirects to the repositories latest release page, and cannot be used to download an asset directly (i.e., you can't simply make a static link to ".../releases/latest/Setup.exe"). However, you can use the [GitHub API with ajax](http://stackoverflow.com/a/26454035) to provide a direct link on your website and avoid the user having to select the correct file or navigate to the GitHub website.

## Distributing from GitHub

The following steps are required to distribute your RELEASES and update NuGet packages with GitHub:

1. **Commit Latest Code** - In order for GitHub to mark a new release as the `Latest`, you have at least one additional commit since the last release tag was added (i.e., releases tags must not share the same commit).
1. **Create a New Release** - [Create a new GitHub release](https://help.github.com/articles/creating-releases/) in your MyApp repository matching your current release version (e.g., 1.0.0).
2. **Upload Release Files** - upload all of the files from `Releases` as assets of the GitHub release (e.g., RELEASES, MyApp.1.0.0-full.nupkg, MyApp.1.0.1-delta.nupkg, MyApp.1.0.1-full.nupkg). 
3. **Set Pre-release (optional)** - if desired, set the release as a pre-release. 
4. **Publish the Release** - click the "Publish Release" to make the release available to the general public and your users.

**Important:** You must upload all packages as assets you wish to be available for update (i.e., the GitHubUpdateManager doesn't look back to previous GitHub releases for previous version packages). If you only include the latest packages, Squirrel will be forced to download the latest full package for each update.


## Updating with GitHub

The Updating process requires you to build, package, releasify, and distribute the update files. 

**Important:** You must ensure there is at least one additional commit since the last version release before adding a new release. GitHub will not update the latest release if the new release tag is tied to the same last commit as a previous release tag.

### GitHub Update Manager

To use GitHub release assets as your distribution mechanism you need to replace `UpdateManager` with `GitHubUpdateManager` when integrating Squirrel in your app:  

**`Program.cs`**

~~~cs
using Squirrel;
~~~

**`static void Main()`**

~~~cs
using (var mgr = UpdateManager.GitHubUpdateManager("https://github.com/myuser/myapp"))
{
  await mgr.Result.UpdateApp();
}
~~~

**Important:** Make sure your url doesn't end in a forward slash ("/"). Adding the trailing forward slash will cause it to fail with a 404 error ([see #641](https://github.com/Squirrel/Squirrel.Windows/issues/641#issuecomment-201478324)).

**Tip:** You can also specify that the update manager should use `prerelease` for updating (see method signature for details).

**Source:** See [Issue #442](https://github.com/Squirrel/Squirrel.Windows/issues/442) for more information.

### How it Works

The GitHub API is used by the `GitHubUpdateManager` to obtain the correct release and asset files for downloading. The following actions are performed:

1. Extracts the username and repository from the url (e.g., "myuser" and "myapp").
2. Uses the GitHub API to get the latest release information. For example, the following url obtains a json list of all release information for the Squirrel.Windows repository: [https://api.github.com/repos/Squirrel/Squirrel.Windows/releases](https://api.github.com/repos/Squirrel/Squirrel.Windows/releases)
3. Obtains the correct download path from the `html_url` attribute of the latest release (or pre-release if specified) to be used as the path for downloading assets. 
4. Follows the normal Squirrel update process by looking for a `RELEASES` file and downloading the necessary delta or full application packages.

---
| Return: [Table of Contents](../readme.md) |
|----|



