## What happens when things go wrong

Squirrel logs a lot of information, but currently it's spread out in a few different places (which isn't Ideal). Here's how to figure out what's happening:

* *While creating packages*: Check `packages\Squirrel.Windows.VERSION\tools\SquirrelSetup.log`

* *During Initial Install*: Check `%LocalAppData%\SquirrelTemp\SquirrelSetup.log`

* *Updating the app*: Catch thrown exceptions and log the results. Alternatively, set up Splat Logging, see [here](https://github.com/Squirrel/Squirrel.Windows.Next/blob/6d7ae23602a3d9a7636265403d42c1090260e6dc/src/Update/Program.cs#L53) for an example. In future versions, this will be less annoying.

## How do I update my local copy of Update.exe on users' machines?

Sometimes, you might ship a busted copy of Update.exe that succeeds the initial install, but doesn't do what you want for some reason. To fix this, you can update Update.exe by including your copy of "Squirrel.exe" in your app update. If Squirrel sees this, it will copy in the latest version into the local app installation.
