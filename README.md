# Squirrel: It's like ClickOnce but Works™

TODO Intro text

## Getting Started

TODO

## What Do We Want?

Deployment and Updates for Desktop applications blow. ClickOnce almost works, but has some glaring bugs that don't seem like they'll ever be fixed. So let's own our own future and build a new one.

Windows apps should be as fast and as easy to install and update as apps like Google Chrome. From an app developer's side, it should be really straightforward to create an installer for my app, and publish updates to it, without having to jump through insane hoops

#### Installation

* Install is Wizard-Free™ and doesn't look awful (or at least, it should have the *possibility* to not look awful)
* No UAC dialogs, which means....
* ...installs to the local user account (i.e. under `%LocalAppData%`)
* Uninstall gives a chance for the application to clean up (i.e. I get to run a chunk of code on uninstall)
* No Reboots, for fuck's sake.
* Can pull down the .NET Framework if need be

#### Updates

* Updates should be able to be applied while the application is running
* At no time should the user ever be forced to stop what he or she is doing
* No Reboots, for fuck's sake.
* The client API should be able to check for updates, receive a (preferably in HTML) ChangeLog

#### Production

* Generating an installer given an existing .NET application should be really easy, like it is for ClickOnce
* Hosting an update server should be really straightforward as well, and should be able to be done using simple HTTP (i.e. I should be able to host my installer and update feed via S3)
* Creating an update for my app should be a very simple process that is easily automated
* Support for multiple "channels" (a-la Chrome Dev/Beta/Release)

### Want to learn more?

Check out [the specs directory](/specs) for more information about how the framework works.

## Relevant Links

 - [Contributing to Squirrel](https://github.com/Squirrel/Squirrel.Windows/wiki/Contributing-to-Squirrel)

### How can I get involved?

Check out https://github.com/Squirrel/Squirrel.Windows.Next/issues/9 for more information about joining the Squirrel Slack room
