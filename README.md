| README.md |
|:---|

[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/Clowd.Squirrel?style=flat-square)](https://www.nuget.org/packages/Clowd.Squirrel/)

# Clowd.Squirrel

Squirrel is both a set of tools and a library, to completely manage both installation and updating your Desktop Windows application.

This project is a fork of the library [Squirrel.Windows](https://github.com/Squirrel/Squirrel.Windows). The main focus here has been to update to more modern tooling, such as upgrading the main libraries to `netstandard2.0`, upgrading the tools to `net6.0`, and adding lots of fixes for dotnet core support.

This library will help you build a `Setup.exe`, integrated (or standalone `Update.exe`) application updater, and release updates to your users very quickly and easily. The `Setup.exe` and `Update.exe` produced by this library are completely dependency free, and can even help you bootstrap/install any number of runtimes your app needs (such as dotnet 5/6, .net 4.8 or others).

---

## Migrating from Squirrel.Windows?

A couple of notes you should be aware of.
 - The command line interface for Squirrel.exe is massively different. Check 'Squirrel.exe -h' for more info.
 - The commnad line for Update.exe has stayed the same
 - Update.exe is significantly bigger (12.5mb)
 - There have been a great many other improvements here. To see some of them [have a look at the feature matrix](#feature-matrix).

---

## What Do We Want?

Windows apps should be as fast and as easy to install and update as apps like Google Chrome. From an app developer's side, it should be really straightforward to create an installer for my app, and publish updates to it, without having to jump through insane hoops. 

* **Integrating** an app to use Squirrel should be extremely easy, provide a client API, and be developer friendly.
* **Packaging** is really easy, can be automated, and supports delta update packages.
* **Distributing** should be straightforward, use simple HTTP updates, and provide multiple "channels" (a-la Chrome Dev/Beta/Release).
* **Installing** is Wizard-Free™, with no UAC dialogs, does not require reboot, and is .NET Framework friendly.
* **Updating** is in the background, doesn't interrupt the user, and does not require a reboot.

---

## Quick Start For .NET Apps

1. Install the [Clowd.Squirrel Nuget Package](https://www.nuget.org/packages/Clowd.Squirrel/)

2. Add SquirrelAwareVersion to your assembly manifest to indicate that your exe supports Squirrel. 

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
     <SquirrelAwareVersion xmlns="urn:schema-squirrel-com:asm.v1">1</SquirrelAwareVersion>
   </assembly>
   ```

3. Handle Squirrel events somewhere very early in your application startup (such as the beginning of `main()` or `Application.OnStartup()` for WPF). 

   ```cs
   public static int Main(string[] args)
   {
       SquirrelAwareApp.HandleEvents(
           onInitialInstall: OnInstall,
           onAppUpdate: OnUpdate,
           onAppUninstall: OnUninstall,
           onFirstRun: OnFirstRun);
           
       // ...
   }

   private static void OnInstall(Version obj)
   {
       using var mgr = new UpdateManager("https://the.place/you-host/updates");
       mgr.CreateUninstallerRegistryEntry();
       mgr.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
   }
   
   private static void OnUninstall(Version obj)
   {
       using var mgr = new UpdateManager("https://the.place/you-host/updates");
       mgr.RemoveUninstallerRegistryEntry();
       mgr.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
   }
   
   // ...
   ```
   
   When installed, uninstalled or updated, these methods will be executed, giving your app a chance to add or remove application shortcuts or perform other tasks. 

4. Build/Publish your app (with `msbuild`, `dotnet publish` or similar)

5. Create a Squirrel release using the `Squirrel.exe` command line tool. 
   The tool can be downloaded from GitHub Releases, and it is also bundled into the [Clowd.Squirrel](https://www.nuget.org/packages/Clowd.Squirrel/) nuget package. 
   If installed through NuGet, the tools can usually be found at:
   - `%userprofile%\.nuget\packages\Clowd.Squirrel\<Clowd.Squirrel version>\tools`, or;
   - `..\packages\Clowd.Squirrel\<Clowd.Squirrel version>\tools`
   
   Once you have located the tools folder, create a release. Example below with some useful options, but explore `Squirrel.exe -h` for a complete list.
   ```cmd
   Squirrel.exe pack --packName "YourApp" --packVersion "1.0.0" --packAuthors "YourCompany" --packDirectory "path-to/publish/folder"
   ```
   Important Notes:
   - The same `--releaseDir` (default `.\Releases`) should be used each time, so delta updates can be generated.
   - The package version must comply to strict SemVer syntax. (eg. `1.0.0`, `1.0.1-pre`)
   - A list of supported runtimes for the `--framework` argument is [available here](https://github.com/clowd/Clowd.Squirrel/blob/develop/src/Squirrel/Runtimes.cs)
   
6. Distribute your entire `--releaseDir` folder online. This folder can be hosted on any static web/file server, [Amazon S3](docs/using/amazon-s3.md), BackBlaze B2, or even via [GitHub Releases](docs/using/github.md). 
   
   If using CI to deploy releases, you can use the package syncing commands to download the currently live version, before creating a package. This means delta/patch updates can be generated. Complete powershell example:
   ```ps1
   # build / publish your app
   dotnet publish -c Release -o ".\publish" 

   # find Squirrel.exe path and add an alias
   Set-Alias Squirrel ($env:USERPROFILE + "\.nuget\packages\clowd.squirrel\2.6.2-pre\tools\Squirrel.exe");

   # download currently live version
   Squirrel http-down --url "https://the.place/you-host/updates"

   # build new version and delta updates.
   Squirrel pack`
    --framework net6,vcredist143-x86`  # Install .NET 6.0 (x64) and vcredist143 (x86) during setup, if not installed
    --packName "YourApp"`              # Application / package name
    --packVersion "1.0.0"`             # Version to build. Should be supplied by your CI
    --packAuthors "YourCompany"`       # Your name, or your company name
    --packDirectory ".\publish"`       # The directory the application was published to
    --setupIcon "mySetupIcon.ico"`     # Icon for Setup.exe
    --splashImage "install.gif"        # The splash artwork (or animation) to be shown during install
   ```

7. Update your app on startup / periodically with UpdateManager.
   ```cs
   private static async Task UpdateMyApp()
   {
      using var mgr = new UpdateManager("https://the.place/you-host/updates");
      var newVersion = await mgr.UpdateApp();
      
      // optionally restart the app automatically, or ask the user if/when they want to restart
      if (newVersion != null) {
         UpdateManager.RestartApp();
      }
   }
   ```

---

## Quick Start For Native / Other Apps

This quick start guide is coming soon. Refer to below for complete docs which contains native app instructions.

---

## More Documentation

**Note - most of the following documentation is now out of date and updates are also coming soon**

See the documentation [Table of Contents](docs/readme.md) for an overview of the available documentation. 

---

## Feature Matrix

| Feature | Clowd.Squirrel | Squirrel.Windows |
|---|---|---|
| Continuous updates, bug fixes, and other improvements | ✅ | ❌ |
| Provides a command line update interface (Update.exe) with your app | ✅ | ✅ |
| Update.exe Size | ❌ 12.5mb | ✅ 2mb |
| Provides a C# SDK | netstandard2.0<br>net461<br>net5.0<br>net6.0 | netstandard2.0 |
| SDK has 100% XML comment coverage in Nuget Pacakge | ✅ | None, does not ship comments in NuGet |
| SDK Dependencies | SharpCompress | SharpCompress (outdated & security vulnerability)<br>NuGet (outdated and bugs)<br>Mono.Cecil (outdated and bugs)<br>Microsoft.Web.Xdt<br>Microsoft.CSharp<br>Microsoft.Win32.Registry<br>System.Drawing.Common<br>System.Net.Http<br>System.Web |
| SDK is strong-name signed | ✅ | ❌ |
| Provides an update package builder (Squirrel.exe) | ✅ | ✅ |
| Supports building tiny delta updates | ✅ | ✅ |
| Can compile a release/setup in a single easy command | ✅ | ❌ |
| Command line tool for package building that actually prints helpful messages to the console | ✅ | ❌ |
| CLI help text that is command-based and easily understandable | ✅ | ❌ |
| Supports building packages for native apps | ✅ | ✅ |
| Supports building packages for .Net/Core | ✅ | Limited/Buggy |
| Supports building packages for PublishSingleFile apps | ✅ | ❌ |
| Supports fully automated CI package deployments easily | ✅ | ❌ |
| Compiles an installer (Setup.exe) | ✅ | ✅ |
| Setup Splash Gif | ✅ | ✅ |
| Setup Splash Png,Jpeg,Tiff,Etc | ✅ | ❌ |
| Setup Splash Progress Bar | ✅ | ❌ |
| Setup Splash has Multi-Monitor DPI support | ✅ | ❌ |
| No internal dependencies on external frameworks/runtimes | ✅ | ❌ |
| Can deploy an application that has no dependencies | ✅ | ❌ (always installs .Net Framework with your app) |
| Can install .Net Full Framework during setup | ✅ | ✅ |
| Can install .Net/Core during setup | ✅ | ❌ |
| Can install vcredist during setup | ✅ | ❌ |
| Can install new runtimes (see above) during updates | ✅ | ❌ |
| Cleans up after itself | ✅ | Leaves huge log files everywhere<br>Does not delete itself during uninstall |
| Can build an MSI enterprise machine-wide deployment tool | ✅ | ✅ |

---

## Building Squirrel
For the impatient:

```cmd
git clone https://github.com/clowd/Clowd.Squirrel
cd clowd/Clowd.Squirrel
build.cmd
```

See [Contributing](docs/contributing/contributing.md) for additional information on building and contributing to Squirrel.

## License and Usage

See [COPYING](COPYING) for details on copyright and usage.









