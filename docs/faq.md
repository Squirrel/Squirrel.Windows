| [docs](.) / faq.md |
|:---|

# Frequently Asked Questions (FAQ)

Frequently Asked Questions for Squirrel.Windows, organized by area below.

## Integrating

1. **Can Squirrel.Windows be used on applications that aren't made with .Net?**  
   Yes, you can package a non-c# application in the same manner as described in the Getting Started guide. For additional customization, see [custom squirrel events for non-c# apps](using/custom-squirrel-events-non-CS.md).  
1. **How do I migrate a ClickOnce app to Squirrel?**  
   You may want to look into the [ClickOnceToSquirrelMigrator](https://github.com/flagbug/ClickOnceToSquirrelMigrator) migration helper.

## Packaging

1. **How can I tell was is going wrong with the releasify?**  
   Check `packages\Squirrel.Windows.VERSION\tools\SquirrelSetup.log` for logging information when creating packages.
2. **Do I really have to add all the Squirrel DLLs to my app ?**
   Yes, you have to add them all to the NuGet package, however, [others](https://github.com/Squirrel/Squirrel.Windows/issues/531) have used [ILMerge](http://research.microsoft.com/en-us/people/mbarnett/ilmerge.aspx) to generate a single assembly.

## Distributing

1. **Can I distribute update files on IIS?**  
Yes you can, see [Microsoft IIS](using/microsoft-iis.md) for details.

## Installing

1. **The Initial Install via `Setup.exe` is failing. How do I learn what is going wrong?**  
   Check `%LocalAppData%\SquirrelTemp\SquirrelSetup.log` for logs related to the initial install.
1. **Installer application doesn't do anything. The animation flashes but the application never starts.**  
   The app is likely crashing on the first run (see [Debugging Installs](using/debugging-installs.md) for details).
2. **The Installer seems to be blocked in Enterprise environments. How can confirm this?**  
  Squirrel may be prevented from installing if Group Policy disallows the running of executables from `%LocalAppData%`. In this case, the "show log" button on the "installation failed" dialog will fail because `Update.exe` can not run to create a log file.  
  
  The `Setup.exe` for your application should still copy files to `%LocalAppData%\SquirrelTemp` as a pre-installation step. To verify that Group Policy is restricting you, execute `Update.exe` from the command line:

  ```
C:\>%LocalAppData\MyApp\Update.exe
This program is blocked by group policy. For more information, contact your system administrator.
  ```

  The best course of action is to request that executables for Squirrel and your application be whitelisted by your corporate overlords.
4. **No Shortcuts are Created for my Application**
   Verify that the NuGet Package Metadata `id` property doesn't have a [space or \[dot\]](https://github.com/Squirrel/Squirrel.Windows/issues/530) in it.


## Updating

1. **How do I determine what is going wrong with the UpdateManager in MyApp?**  
   You can setup your `\bin` directory so you can execute MyApp in the Visual Studio debugger and simply step through the update process as well as catch exceptions and log the results (see [Debugging Updates](using/debugging-updates.md) for details)
2. **I've Distributed a Broken Copy of Update.exe. How can I fix this?**  
   Sometimes, you might ship a broken copy of `Update.exe` that succeeds the initial install, but doesn't do what you want for some reason. To fix this, you can force an update of the `Update.exe` by including a copy of `Squirrel.exe` in your app update package. If Squirrel sees this, it will copy in this latest version to the local app installation.
3. **How can you replace DLLs while they're loaded? Impossible!**  
   You can't. So, how can you do it? The basic trick that ClickOnce uses is, you have a folder of EXEs and DLLs, and an Application Shortcut. When ClickOnce goes to update its stuff, it builds a completely *new* folder of binaries, then the last thing it does is rewrite the app shortcut to point to the new folder. See []

---
| Return: [Table of Contents](readme.md) |
|:---|
