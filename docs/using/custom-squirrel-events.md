| [docs](..)  / [using](.) / custom-squirrel-events.md
|:---|

# Custom Squirrel Events

## Handling Squirrel Events

Squirrel events allow you to handle custom events around the installation and updating process, which is important because Squirrel doesn't do much of anything at installation time automatically. However, since the code is executing inside your application, it's way easier to do stuff than other systems where you're writing custom "installer DLLs".

### Overriding Default Behaviors

When none of the apps in your package are "Squirrel-Aware", Squirrel does some things on your behalf to make your life easier, the primary one being that every EXE in your app package automatically gets a shortcut on both the Desktop and the Start Menu. Once you enable Squirrel events *for even a single EXE file*, you must do this yourself.

### Making Your App Squirrel Aware 

In your app's `AssemblyInfo.cs`, add the following line:

```
[assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]
```

### Using the `SquirrelAwareApp` Helper

If you are writing a C# app, it is **highly encouraged** to use the `SquirrelAwareApp` helper class to implement this. Here's an implementation that is similar to the default (i.e. non-squirrel-aware) behavior:

```cs
static bool ShowTheWelcomeWizard;
...
static int Main(string[] args) 
{
    // NB: Note here that HandleEvents is being called as early in startup
    // as possible in the app. This is very important! Do _not_ call this
    // method as part of your app's "check for updates" code.

    using (var mgr = new UpdateManager(updateUrl))
    {
        // Note, in most of these scenarios, the app exits after this method
        // completes!
        SquirrelAwareApp.HandleEvents(
          onInitialInstall: v => mgr.CreateShortcutForThisExe(),
          onAppUpdate: v => mgr.CreateShortcutForThisExe(),
          onAppUninstall: v => mgr.RemoveShortcutForThisExe(),
          onFirstRun: () => ShowTheWelcomeWizard = true);
    }
}
```

## App Setup Helper Methods

These methods help you to set up your application in Squirrel events - if you're not using custom Squirrel events, you probably don't need to call these methods.

* `[Create/Remove]ShortcutsForExecutable` - creates and removes shortcuts on the desktop or in the Start Menu.

* `[Create/Remove]UninstallerRegistryEntry` - creates and removes the uninstaller entry. Normally called by `Update.exe`.

## See Also

* [Custom Squirrel Events for non-c# Apps](custom-squirrel-events-non-cs.md) - steps on making a non-c# application Squirrel Aware and handling custom events.

---
| Return: [Table of Contents](../readme.md) |
|----|
