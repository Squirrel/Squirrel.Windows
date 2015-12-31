| [docs](..)  / [using](.) / loading-gif.md
|:---|

# Loading GIF

Squirrel installers don't have any UI - the goal of a Squirrel installer is to install so blindingly fast that double-clicking on Setup.exe *feels* like double-clicking on an app shortcut. Make your installer **fast**.

However, for large applications, this isn't possible. For these apps, Squirrel will optionally display a graphic as a "splash screen" while installation is processing, but only if installation takes more than a pre-set amount of time. This will be centered, backed by a transparent window, and can optionally be an animated GIF. Specify this via the `-g` parameter.

~~~powershell
PM> Squirrel --releasify MyApp.1.0.0.nupkg -g .\loading.gif
~~~ 

## See Also
* [Squirrel Command Line](squirrel-command-line.md) - command line options for `Squirrel --releasify`


---
| Return: [Table of Contents](../readme.md) |
|----|
