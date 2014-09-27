# Installer

The installer consists of two parts: `Setup.exe` (C++ bootstrapper) and `Update.exe` (C# Squirrel Client). There are several main design goals of the installer:

* Run as quickly as possible, with as little user interface interaction as possible. The faster we can get into the application, the better. An ideal install experience is that once `Setup.exe` gets clicked, within 3sec the application is running on the user's machine. Double-clicking `Setup.exe` should feel like clicking the app shortcut.

* `Setup.exe` should be written such that it does as little work as possible, because C++.

* Running an older `Setup.exe` should simply execute the current app.

* Support installation of non-C# applications

## Setup.exe

Setup.exe does the following operations:

1. Determines if the .NET Framework is installed
1. If not, relaunches itself with `/installfx45`, which opens a progress dialog which downloads the .NET Framework and silently invokes it.
1. Extract `Update.exe` and `AppName-full.nupkg` to `%LocalAppData%\Squirrel\Temp` which are embedded as resources.
1. Execute `Update.exe` with the `/install` switch, and apply any switches that were applied to Setup.exe
1. Nuke the extracted temporary files from step 3.

## Update.exe

Update.exe is a generic client for Squirrel which supports several operations:

* `--install [directory] [/silent]` - Install the NuPkg file given (or any NuPkg files in the same directory as itself), and launch their applications. If `/silent` is given, don't launch anything. Copy `Update.exe` to the application root directory. Install also writes an entry in Programs and Features which will invoke `/uninstall`.
* `--uninstall` - Completely uninstall the application associated with the directory in which `Update.exe` resides.
* `--download URL` - Check for updates from the given URL and write information about available versions to standard output in JSON format.
* `--update URL` - Updates the application to the latest version from the remote URL

Here is sample code for handling these events:

```csharp
public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		// to make this work you MUST remove StartupUri from app.xaml
		if (e.Args.Any())
		{

			if (e.Args[0] == "--squirrel-install")
			{
				// called when your app is installed. Exit as soon as you're finished setting up the app. 
				using (var mgr = new UpdateManager("updateUrl", "MyApp", FrameworkVersion.Net45))
				{
					mgr.CreateShortcutsForExecutable(
					  Path.GetFileName(Assembly.GetEntryAssembly().Location),
					  ShortcutLocation.Desktop | ShortcutLocation.StartMenu,
					  false
				   );
					Shutdown(0);
					return;
				}
			}
			if (e.Args[0] == "--squirrel-updated")
			{
				// called when your app is updated to the given version. Exit as soon as you're finished
				using (var mgr = new UpdateManager("updateUrl", "MyApp", FrameworkVersion.Net45))
				{
					mgr.CreateShortcutsForExecutable(
					  Path.GetFileName(Assembly.GetEntryAssembly().Location),
					  ShortcutLocation.Desktop | ShortcutLocation.StartMenu,
					  true
				   );
					Shutdown(0);
					return;
				}
			}
			if (e.Args[0] == "--squirrel-uninstall")
			{
				// called when your app is uninstalled. Exit as soon as you're finished
				using (var mgr = new UpdateManager("updateUrl", "MyApp", FrameworkVersion.Net45))
				{
					mgr.RemoveShortcutsForExecutable(
					  Path.GetFileName(Assembly.GetEntryAssembly().Location),
					  ShortcutLocation.Desktop | ShortcutLocation.StartMenu
				   );
					Shutdown(0);
					return;
				}
			}
			if (e.Args[0] == "--squirrel-firstrun")
			{
				// called after everything is set up. You should treat this like a normal app run (maybe show the "Welcome" screen)
				MessageBox.Show("first running. Welcome");
				// fall through and run.
			}
		}

		// You startup code, e.g. show the main window

		base.OnStartup(e);
	}
}
```

## Sample Update.exe --download JSON output

```
{
  "CurrentlyInstalledVersion": null,
  "FutureReleaseEntry": {
    "SHA1": "163D0D3F0B339D6B5866C808ABFF57DB74B93C68",
    "Filename": "GitHubForWindows.2.3.1.1.nupkg",
    "Filesize": 33160193,
    "IsDelta": false,
    "EntryAsString": "163D0D3F0B339D6B5866C808ABFF57DB74B93C68 GitHubForWindows.2.3.1.1.nupkg 33160193",
    "Version": {
      "Major": 2,
      "Minor": 3,
      "Build": 1,
      "Revision": 1,
      "MajorRevision": 0,
      "MinorRevision": 1
    },
    "PackageName": "GitHubForWindows"
  },
  "ReleasesToApply": [
    {
      "SHA1": "163D0D3F0B339D6B5866C808ABFF57DB74B93C68",
      "Filename": "GitHubForWindows.2.3.1.1.nupkg",
      "Filesize": 33160193,
      "IsDelta": false,
      "EntryAsString": "163D0D3F0B339D6B5866C808ABFF57DB74B93C68 GitHubForWindows.2.3.1.1.nupkg 33160193",
      "Version": {
        "Major": 2,
        "Minor": 3,
        "Build": 1,
        "Revision": 1,
        "MajorRevision": 0,
        "MinorRevision": 1
      },
      "PackageName": "GitHubForWindows"
    }
  ],
  "AppFrameworkVersion": 1,
  "IsBootstrapping": true,
  "PackageDirectory": "C:\\Users\\paul\\AppData\\Local\\Debug\\packages"
}
```
