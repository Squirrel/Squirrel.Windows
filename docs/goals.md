| [docs](.) / goals.md |
|:---|

# What Do We Want?

Deployment and Updates for Desktop applications are a real drag. ClickOnce almost works, but has some glaring bugs that don't seem like they'll ever be fixed. So let's own our own future and build a new one.

Windows apps should be as fast and as easy to install and update as apps like Google Chrome. From an app developer's side, it should be really straightforward to create an installer for my app, and publish updates to it, without having to jump through insane hoops

## Configuring

* Integrating the installer for an existing .NET application should be really easy.
* The client API should be able to check for updates and receive a (preferably in HTML) ChangeLog.
* Developer should have control over custom actions and events during installing and updating.
* Uninstall gives a chance for the application to clean up (i.e. I get to run a chunk of code on uninstall)

## Packaging

* Generating an installer given an existing .NET application should be really easy, like it is for ClickOnce.
* Creating an update for my app should be a very simple process that is easily automated.
* Packaging will support delta files to reduce the size of update packages.

## Distributing

* Hosting an update server should be really straightforward, and should be able to be done using simple HTTP (i.e. I should be able to host my installer and update feed via S3).
* Support for multiple "channels" (a-la Chrome Dev/Beta/Release).

## Installing 

* Install is Wizard-Free™ and doesn't look awful (or at least, it should have the *possibility* to not look awful).
* No UAC dialogs, which means....
* ...installs to the local user account (i.e. under `%LocalAppData%`).
* No Reboots. None!
* Can pull down the .NET Framework if need be.

## Updating

* Updates should be able to be applied while the application is running.
* At no time should the user ever be forced to stop what he or she is doing.
* No Reboots. None!



---
| Return: [Table of Contents](readme.md) |
|:---|
