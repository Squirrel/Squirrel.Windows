| Squirrel.Windows |
|:---|

# Squirrel: It's like ClickOnce but Works™

Squirrel is both a set of tools and a library, to completely manage both installation and updating your Desktop Windows application, written in either C# or any other language (i.e., Squirrel can manage native C++ applications).

Squirrel uses NuGet packages to create installation and update packages, which means that you probably already know most of what you need to create an installer.

## What Do We Want?

Windows apps should be as fast and as easy to install and update as apps like Google Chrome. From an app developer's side, it should be really straightforward to create an installer for my app, and publish updates to it, without having to jump through insane hoops. 

* **Integrating** an app to use Squirrel should be extremely easy, provide a client API, and be developer friendly.
* **Packaging** is really easy, can be automated, and supports delta update packages.
* **Distributing** should be straightforward, use simple HTTP updates, and provide multiple "channels" (a-la Chrome Dev/Beta/Release).
* **Installing** is Wizard-Free™, with no UAC dialogs, does not require reboot, and is .NET Framework friendly.
* **Updating** is in the background, doesn't interrupt the user, and does not require a reboot.

Refer to our full list of goals for [integrating, packaging, distributing, installing, and updating](docs/0-What-Do-We-Want.md).

## 1 Getting Started

Section 1, [Getting Started](docs/1-Getting-Started.md), will step through the integration of Squirrel.Windows for a basic c# application. The steps for using Squirrel.Windows with your application include the following:

* [1.1 Integrating](docs/1.1-Integrating.md) - integrating Squirrel update into your application.
* [1.2 Packaging](docs/1.2-Packaging.md) - packaging application files and preparing them for release.
* [1.3 Distributing](docs/1.3-Distributing.md) - providing install and update files for users.
* [1.4 Installing](docs/1.4-Installing.md) - process of initial installation of your application.
* [1.5 Updating](docs/1.5-Updating.md) - process of updating an existing install.

## 2 Advanced Usage

Section [2 Advanced Usage](docs/2-Advanced-Usage.md), explores specific advanced topics for using Squirrel, including the following:


* [2.1 More Integrating](docs/2.1-More-Integrating.md) - details on integrating Squirrel in MyApp along with advanced topics (e.g., [Debugging](docs/2.1-Integrating-Debugging.md), [Custom Squirrel Events](docs/2.1-Integrating-Custom-Events.md)).
* [2.2 More Packaging](docs/2.2-More-Packaging.md) - packaging meta data details and advanced topics (e.g., [Automating Packaging](docs/2.2.2-Packing-Automate-Nuspec.md), [Delta Packages](docs/2.2-Packaging-Delta-Packages.md), [Application Signing](docs/2.2-Packaging-Releasify-Application-Signing.md)).
* [2.3 More Distributing](docs/2.3-More-Distributing.md) - additional distribution options (e.g., [Microsoft IIS](docs/2.3-Distributing-IIS.md), [GitHub](docs/2.3-Distributing-GitHub.md), [Amazon S3](docs/2.3-Distributing-Amazon-S3.md)).
* [2.4 More Installing](docs/2.4-More-Installing.md) - detailed install steps and advanced topics (e.g., [Machine-wide Installs](docs/2.4-Machine-wide-Installs.md), [Loading GIF](docs/2.4-Loading-Gif.md)).
* [2.5 More Updating](docs/2.5-More-Updating.md) - detailed update steps.


## 3 Contributing

Why not give back and help make Squirrel even better by [contributing](docs/3-Contributing.md) to the project.

## 4 FAQ

Have a question? Review the [Frequently Asked Questions (FAQ)](docs/4-FAQ.md) document. 

## 5 Copyright and Usage

See [COPYING](COPYING) for details on copyright and usage of the Squirrel.Windows software.



---
|Next: [1 Getting Started](docs/1-Getting-Started.md)|
|:---|





