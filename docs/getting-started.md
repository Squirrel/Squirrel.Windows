## Quick Start (for the impatient)

1. `Install-Package squirrel.windows`
2. Use NuGet Package Explorer (or any other way) to create a NuGet package for your app.
3. Open the NuGet Package Console, and type `Squirrel --releasify path/to/the/nuget/package.nupkg`
4. In your app, from time to time run this code:

```cs
using (var mgr = new UpdateManager("https://path/to/my/update/folder", "nuget-package-id", FrameworkVersion.Net45)) {
    await mgr.UpdateApp();
}
```

You should have a folder called `Releases` with three files in it. Publish those all to S3 in the same folder and you've now got an installer.

## What about shortcuts?

Every EXE in your package will automatically get a shortcut. If you don't want this to be the case, follow the steps for handling Squirrel events below.

## Handling Squirrel events

Squirrel events are optional, but they can be super useful, as it gives you a chance to do "custom install actions" on install / uninstall / update. Most production applications will end up doing this, but simple applications can accept the default behavior without any problems.

Check out the [article on Squirrel Events](./squirrel-events.md) to get started.
