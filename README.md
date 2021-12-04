| README.md |
|:---|

[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/Clowd.Squirrel?style=flat-square)](https://www.nuget.org/packages/Clowd.Squirrel/)

# Clowd.Squirrel

Squirrel is both a set of tools and a library, to completely manage both installation and updating your Desktop Windows application.

This project is a fork of the library [Squirrel.Windows](https://github.com/Squirrel/Squirrel.Windows), which has been largely discontinued by the author. The main focus here has been to update to more modern tooling, such as upgrading the main libraries to `netstandard2.0`. 

This library will help you build a `Setup.exe`, integrated (or standalone `Update.exe`) application updater, and release updates to your users very quickly and easily. The `Setup.exe` and `Update.exe` produced by this library are completely dependency free, and can even help you bootstrap/install a runtime of your choice (such as dotnet 5, .net 4.8 or others).

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

2. Add SquirrelAwareVersion attribute somewhere in your project. (It can be placed in any cs file, but usually it goes into `AssemblyInfo.cs` if your project has one)

   ```cs
   [assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]
   ```
3. Handle Squirrel events somewhere very early in your application startup (such as the beginning of `main()` or `Application.OnStartup()` for WPF). 

   ```cs
   SquirrelAwareApp.HandleEvents(
       onInitialInstall: OnInstall,
       onAppUpdate: OnUpdate,
       onAppUninstall: OnUninstall,
       onFirstRun: OnFirstRun);
   ```

   When installed, uninstalled or updated, your app will be run to give you a chance to add or remove application shortcuts. 

   ```cs
   private static void OnInstall(Version obj)
   {
      using var mgr = new UpdateManager("https://the.place/you-host/updates");
      mgr.CreateUninstallerRegistryEntry();
      mgr.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
   }
   ```

4. Publish your app (with `dotnet publish` or similar) and build a Nuget package with your publish output in the "lib/net45" target folder. (yes, even if your app does not have anything to do with net45)
   You can use `NuGet.exe pack` for this, or any other Nuget creation tool (eg. OctoPack). <br/>
   [More information here](docs/using/visual-studio-packaging.md#example-nuspec-file-for-myapp) <br/>
   *Note: The package version must comply to strict SemVer syntax. (eg. `1.0.0`, `1.0.1-pre`)*

5. Create a Squirrel release using the `Squirrel.com --releasify` command line tool. 
   It is shipped with the [Clowd.Squirrel](https://www.nuget.org/packages/Clowd.Squirrel/) nuget package. 
   The path of the Squirrel tools is available via the MSBuild property `$(SquirrelToolsPath)` if you are integrating this into your build pipeline.
   If not, the tools can usually be found at:
   - `%userprofile%\.nuget\packages\Clowd.Squirrel\<Clowd.Squirrel version>\tools`, or;
   - `..\packages\Clowd.Squirrel\<Clowd.Squirrel version>\tools`
   
   Once you have located the tools folder, create a release. Example below with some useful options, but explore `Squirrel.com -h` for a complete list. You should use the same `releaseDir` each time, so delta updates can be generated.
   ```cmd
   Squirrel.com --releasify MyApp.1.0.0.nupkg --selfContained --releaseDir=".\releases" --setupIcon=myIcon.ico
   ```
   
6. Distribute your entire `releaseDir` folder online. This folder can be hosted on any static web/file server, [Amazon S3](docs/using/amazon-s3.md), BackBlaze B2, or even via [GitHub Releases](docs/using/github.md).

7. Update your app periodically with UpdateManager.
   ```cs
   private static void UpdateMyApp()
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

See the documentation [Table of Contents](docs/readme.md) for an overview of the available documentation. 

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









