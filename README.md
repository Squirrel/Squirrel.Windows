# Squirrel: It's like ClickOnce but Works™

Squirrel is both a set of tools and a library, to completely manage both installation and updating your Desktop Windows application, written in either C# or any other language (i.e. Squirrel can manage native C++ applications).

Squirrel uses NuGet packages to create installation and update packages, which means that you probably already know most of what you need to create an installer.

## Getting Started

Check out the [user documentation](/docs) for how to use Squirrel. A good place to start reading is the [Getting Started page](/docs/getting-started.md), which includes a Quick Start guide.

## What Do We Want?

Deployment and Updates for Desktop applications are a real drag. ClickOnce almost works, but has some glaring bugs that don't seem like they'll ever be fixed. So let's own our own future and build a new one.

Windows apps should be as fast and as easy to install and update as apps like Google Chrome. From an app developer's side, it should be really straightforward to create an installer for my app, and publish updates to it, without having to jump through insane hoops

#### Installation

* Install is Wizard-Free™ and doesn't look awful (or at least, it should have the *possibility* to not look awful)
* No UAC dialogs, which means....
* ...installs to the local user account (i.e. under `%LocalAppData%`)
* Uninstall gives a chance for the application to clean up (i.e. I get to run a chunk of code on uninstall)
* No Reboots. None!
* Can pull down the .NET Framework if need be

#### Updates

* Updates should be able to be applied while the application is running
* At no time should the user ever be forced to stop what he or she is doing
* No Reboots. None!
* The client API should be able to check for updates, receive a (preferably in HTML) ChangeLog

#### Production

* Generating an installer given an existing .NET application should be really easy, like it is for ClickOnce
* Hosting an update server should be really straightforward as well, and should be able to be done using simple HTTP (i.e. I should be able to host my installer and update feed via S3)
* Creating an update for my app should be a very simple process that is easily automated
* Support for multiple "channels" (a-la Chrome Dev/Beta/Release)

## Want to peek under the hood a bit?

Check out [the specs directory](/specs) for more information about how the framework works.

## How can I get involved?

Check out https://github.com/Squirrel/Squirrel.Windows.Next/issues/9 for more information about joining the Squirrel Slack room
