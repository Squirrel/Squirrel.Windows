[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/Clowd.Squirrel?style=flat-square)](https://www.nuget.org/packages/Clowd.Squirrel/)

# Clowd.Squirrel

Squirrel is both a set of tools and a library, to completely manage both installation and updating your desktop application. 

Feel free to join our discord to recieve updates or to ask questions:

[![discordimg2](https://user-images.githubusercontent.com/1287295/150318745-cbfcf5d0-3697-4bef-ac1a-b0d751f53b48.png)](https://discord.gg/CjrCrNzd3F)

## What Do We Want?

Apps should be as fast easy to install. Update should be seamless like Google Chrome. From a developer's side, it should be really straightforward to create an installer for my app, and publish updates to it, without having to jump through insane hoops. 

* **Integrating** an app to use Squirrel should be extremely easy, provide a client API, and be developer friendly.
* **Packaging** is really easy, can be automated, and supports delta update packages.
* **Distributing** should be straightforward, use simple HTTP updates, and provide multiple "channels" (a-la Chrome Dev/Beta/Release).
* **Installing** is Wizard-Free™, with no UAC dialogs, does not require reboot, and is .NET Framework friendly.
* **Updating** is in the background, doesn't interrupt the user, and does not require a reboot.


## Clowd.Squirrel is now cross-platform


## Quick Start Guide!
*This guide contains everything you need to know to publish your first app. I know, it's short and sweet. Squirrel can do lots more than what you see here, so once you've tried the instructions here, check out some of our other docs!*

### Prerequisites
These are required to build packages with Squirrel, but are not required for applications using Squirrel.
- [Install dotnet 6.0 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- [Install Squirrel Tool (csq)](https://www.nuget.org/packages/csq/)
  > dotnet tool install -g csq

### Adding Squirrel to your App
- Install the [Clowd.Squirrel](https://www.nuget.org/packages/Clowd.Squirrel/) NuGet package
- **IMPORTANT:** Add `SquirrelAwareApp.HandleEvents();` to the beginning of `Main()`
  

### Building a package / installer
Instructions may vary by OS. Consult `csq -h` on your target platform for more info.
```txt
> dotnet publish YourApp.csproj -o ./publish
> csq pack -u YourApp -v 1.0.1 -p ./publish -e YourApp.exe
```

### Updating your App
You can host releases in a directory/file share, online on any static web/file server, [Amazon S3](docs/using/amazon-s3.md), BackBlaze B2, or even via [GitHub Releases](docs/using/github.md). 

```cs
private static async Task UpdateMyApp()
{
    using var mgr = new UpdateManager("https://the.place/you-host/releases");
    var newVersion = await mgr.UpdateApp();
    
    // You must restart to complete the update. 
    // This can be done later / at any time.
    if (newVersion != null) UpdateManager.RestartApp();
}
```