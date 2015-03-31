## What happens when things go wrong

Squirrel logs a lot of information, but currently it's spread out in a few different places (which isn't Ideal). Here's how to figure out what's happening:

* *While creating packages*: Check `packages\Squirrel.Windows.VERSION\tools\SquirrelSetup.log`

* *During Initial Install*: Check `%LocalAppData%\SquirrelTemp\SquirrelSetup.log`

* *Updating the app*: Catch thrown exceptions and log the results. Alternatively, set up Splat Logging, see [here](https://github.com/Squirrel/Squirrel.Windows.Next/blob/6d7ae23602a3d9a7636265403d42c1090260e6dc/src/Update/Program.cs#L53) for an example. In future versions, this will be less annoying.

## How do I update my local copy of Update.exe on users' machines?

Sometimes, you might ship a busted copy of Update.exe that succeeds the initial install, but doesn't do what you want for some reason. To fix this, you can update Update.exe by including your copy of "Squirrel.exe" in your app update. If Squirrel sees this, it will copy in the latest version into the local app installation.

## Enterprise environments and Group Policy

Squirrel may be prevented from installing if Group Policy disallows the running of executables from `%LocalAppData%`. In this case, the "show log" button on the "installation failed" dialog will fail because `Update.exe` can not run to create a log file.

The `Setup.exe` for your application should still copy files to `%LocalAppData%\SquirrelTemp` as a pre-installation step. To verify that Group Policy is restricting you:

```
C:\Users\<User>\AppData\Local\SquirrelTemp>Update.exe
This program is blocked by group policy. For more information, contact your system administrator.
```

The best course of action is to request that executables for Squirrel and your application be whitelisted by your corporate overlords.

