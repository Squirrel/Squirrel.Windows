## What happens when things go wrong

Squirrel logs a lot of information, but currently it's spread out in a few different places (which isn't Ideal). Here's how to figure out what's happening:

* *While creating packages*: Check `packages\Squirrel.Windows.VERSION\tools\SquirrelSetup.log`

* *During Initial Install*: Check `%LocalAppData%\SquirrelTemp\SquirrelSetup.log`

* *Updating the app*: Catch thrown exceptions and log the results. Alternatively, set up Splat Logging, see [here](https://github.com/Squirrel/Squirrel.Windows.Next/blob/6d7ae23602a3d9a7636265403d42c1090260e6dc/src/Update/Program.cs#L53) for an example. In future versions, this will be less annoying.
