| [docs](..)  / [using](.) / custom-squirrel-events-non-cs.md
|:---|

# Custom Squirrel Events (Non-C# Apps)

Squirrel events allow you to handle custom events around the installation and updating process.

### Making Your App Squirrel Aware 

Add an entry to the *English* Version Block info called "SquirrelAwareVersion" with a value of "1". Typically this is done via the "App.rc" resource file. Here's a typical entry:

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

### Application Startup Commands

This means that this EXE will be executed by the installer in a number of different scenarios, with special flags - you should handle them correctly:

* `--squirrel-install x.y.z.m` - called when your app is installed. Exit as soon as you're finished setting up the app
* `--squirrel-firstrun` - called after everything is set up. You should treat this like a normal app run (maybe show the "Welcome" screen)
* `--squirrel-updated x.y.z.m` - called when your app is updated to the given version. Exit as soon as you're finished.
* `--squirrel-obsolete x.y.z.m` - called when your out-of-date app is no longer the newest version. Exit as soon as you're finished.
* `--squirrel-uninstall x.y.z.m` - called when your app is uninstalled. Exit as soon as you're finished.

## See Also

* [Custom Squirrel Events for c# Apps](custom-squirrel-events.md) - steps on making a c# application Squirrel Aware and handling custom events.

---
| Return: [Table of Contents](../readme.md) |
|----|