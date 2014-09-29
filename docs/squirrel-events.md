## Handling Squirrel Events

Squirrel events allow you to handle custom events around the installation and updating process, which is important because Squirrel doesn't do much of anything at installation time automatically. However, since the code is executing inside your application, it's way easier to do stuff than other systems where you're writing custom "installer DLLs".

### Custom Events means you opt-out of default behavior

When none of the apps in your package are "Squirrel-Aware", Squirrel does some things on your behalf to make your life easier, the primary one being that every EXE in your app package automatically gets a shortcut on both the Desktop and the Start Menu. Once you enable Squirrel events *for even a single EXE file*, you must do this yourself.

### Getting Started

In your app's `AssemblyInfo.cs`, add the following line:

```
[assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]
```

For non-C# applications, add an entry to the *English* Version Block info called "SquirrelAwareVersion" with a value of "1". Typically this is done via the "App.rc" resource file. Here's a typical entry:

```
BLOCK "StringFileInfo"
BEGIN
    BLOCK "040904b0"
    BEGIN
        VALUE "FileDescription", "Installer for Squirrel-based applications"
        VALUE "FileVersion", "0.5.0.0"
        VALUE "InternalName", "Setup.exe"
        VALUE "LegalCopyright", "Copyright (C) 2014"
        VALUE "OriginalFilename", "Setup.exe"
        VALUE "ProductName", "Squirrel-based application"
        VALUE "ProductVersion", "0.5.0.0"
        VALUE "SquirrelAwareVersion", "1"
    END
END
```

This means that this EXE will be executed by the installer in a number of different scenarios, with special flags - you should handle them correctly:

* `--squirrel-install x.y.z.m` - called when your app is installed. Exit as soon as you're finished setting up the app
* `--squirrel-firstrun` - called after everything is set up. You should treat this like a normal app run (maybe show the "Welcome" screen)
* `--squirrel-updated x.y.z.m` - called when your app is updated to the given version. Exit as soon as you're finished.
* `--squirrel-obsolete x.y.z.m` - called when your out-of-date app is no longer the newest version. Exit as soon as you're finished.
* `--squirrel-uninstall x.y.z.m` - called when your app is uninstalled. Exit as soon as you're finished.

## C# Developers, do this instead

If you are writing a C# app, it is **highly encouraged** to use the `SquirrelAwareApp` helper class to implement this. Here's an implementation that is similar to the default (i.e. non-squirrel-aware) behavior:

```cs
static bool ShowTheWelcomeWizard;

using (var mgr = new UpdateManager(updateUrl, appName, FrameworkVersion.Net45))
{
    // Note, in most of these scenarios, the app exits after this method
    // completes!
    SquirrelAwareApp.HandleEvents(
      onInitialInstall: v => mgr.CreateShortcutForThisExe(),
      onAppUpdate: v => mgr.CreateShortcutForThisExe(),
      onAppUninstall: v => mgr.RemoveShortcutForThisExe(),
      onFirstRun: () => ShowTheWelcomeWizard = true);
}
```
