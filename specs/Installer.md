# Installer

Installer just installs `WixUI` whose job is to:

1. Run the client code to unpack the latest full NuGet package and finish
   initial install.
1. Execute the uninstaller code when WiX goes to remove us, and remove the App
   directory.

### So, on install:

1. WiX unpacks `WixUI` and runs it, and puts an entry in *Programs and
   Features*.
1. `WixUI` executes initial install using `Squirrel.Client` for the full
   NuGet package, doing the update in-place so the installer never needs to be
   rebuilt.

### On Uninstall:

1. WiX gets notified about the uninstall, calls `WixUI` to do app
   uninstall via `Squirrel.Client`
1. WiX then blows away `WixUI`, the "real" installed app.

## Bootstrap UI

`WixUI` has an extremely simple UI when it does its work, it just pops
up, shows a progress bar, a-la Chrome Installer:

![](http://t0.gstatic.com/images?q=tbn:ANd9GcS_DuuEyOX1lfeo_jDetHLiE17pp_4M-Xerj2ieGEkvQQ4h83w57IL5KD6Kzw)

On Uninstall, there is no UI, it's solely in the background.

If Setup.exe gets invoked with the 'Install' action, and the app is already
installed, we just execute the app, a-la ClickOnce.

## Generating the WiX installer

The WiX install script is generated via a Mustache template, whose contents
are primarily populated via the generated NuGet release package. WiX will end
up installing `WixUI`, the latest NuGet package file, and a one-line
RELEASES file (meaning that what WiX installs is technically a valid Squirrel
remote update directory).

## WiX Engine Events and what we should do about them

* `DetectedPackage` - if we're installed (determine this by looking at the
   NuGet package in the same directory as the app), we run the app and bail.

* `DetectComplete` - Do what we're actually here to do (invoke the Squirrel
  installer), then on the UI thread, tell WiX to finish up.

* `PlanPackageBegin` - squelch installation of .NET 4

* `PlanComplete` - Push WiX to to Apply state

* `ApplyComplete` - If something bad happened, switch to UI Error state,
  otherwise start the app if we're in Interactive Mode and call Shutdown()

* `ExecuteError` - Switch to the UI Error state
