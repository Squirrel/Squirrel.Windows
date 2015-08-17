## Quick Start (for the impatient)

* `Install-Package squirrel.windows`, or grab the binaries from the Releases section

* In your app at an appropriate time (not at startup, don't interrupt the user's work), call:

```cs
// NB: For this version, always say your app is using .NET 4.5, even if it's
// totally not
using (var mgr = new UpdateManager("https://path/to/my/update/folder")) 
{
    await mgr.UpdateApp();
}
```

* Use NuGet Package Explorer (or any other way) to create a NuGet package for your app. Make sure that package doesn't have any dependencies. Here's a good example package:

![](http://cl.ly/image/261D2x2X1e3G/content#png) 

* Open the NuGet Package Console, and type `Squirrel --releasify path/to/the/nuget/package.nupkg`

You should have a folder called `Releases` with three files in it. Publish those all to S3 in the same folder and you've now got an installer.


## BETA: Some hacky notes that will go away soon

1. Make sure your NuGet package doesn't have any `<Dependency>` tags.

1. Use the `<File>` tag to include all of your app's dependencies, even ones that for a normal NuGet package wouldn't be there. Make sure to include `Squirrel.dll` and its dependencies too! Every file in the References section of your app should be there.

1. Put all of your app files in `lib/net45`. I don't care if your app isn't actually a .NET 4.5 app, just do it. Even if your app is written in COBOL, put it in `lib/net45`.

## What about shortcuts?

Every EXE in your package will automatically get a shortcut. If you don't want this to be the case, follow the steps for handling Squirrel events below.

## Handling Squirrel events

Squirrel events are optional, but they can be super useful, as it gives you a chance to do "custom install actions" on install / uninstall / update. Most production applications will end up doing this, but simple applications can accept the default behavior without any problems.

Check out the [article on Squirrel Events](./squirrel-events.md) to get started.
