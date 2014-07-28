## Scenarios

#### Production

I'm a developer with a WPF application. I have *zero* way to distribute my
application at the moment. I go to NuGet and install the Squirrel client library.

Now, I want to publish a release. To do so, I pop into the PowerShell Console
and type `New-Release`. What does this do? It:

* Creates a NuGet package of my app (i.e. via shelling out to NuGet.exe or w/e)
* It puts the package in a special "Releases" directory of my solution (along
  perhaps with a special "delta package" for updates)
* It also creates a Setup.exe that I can distribute to people
* Can also transform `changelog.md` to `changelog.html` using the bundled
  Markdown library that ships with Squirrel

I've created a new release. Now, I want to share it with the world! I upload
the contents of my Releases directory verbatim to the web via S3 / FTP /
whatever.

In my app, I call `bool
UpdateManager.CheckForUpdates("http://mycoolsite.com/releases/")` - similar to
ClickOnce API but not awful. The library helps me check for updates, get the
ChangeLog HTML to render, and if I'm really lazy, I can just call
`UpdateManager.ShowUpdateNotification()` and get a stock WPF dialog walking
the user through the upgrade. For production applications, I get the
information I need to create my own update experience (yet I don't have to do
any of the actual heavy lifting).

When I call `UpdateManager.Upgrade()`, the application does the update in the
background, without disturbing the user at all - the next time the app
restarts, it's the new version.


#### Users

I click on a link, and a setup experience starts up. Instead of the usual
"Next >" buttons, I see a single "Install" button (think Visual Studio 2012 installer).
Clicking that installs and immediately opens the application. No UAC prompts,
no long waits.
