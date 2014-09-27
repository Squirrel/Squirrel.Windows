## Getting Started

Here's a step by step overview of how to create an installer for your application using Squirrel.

1. Setup your Project
2. Create a NuGet Package for your App
3. Add Squirrel Events (optional)
4. Implement Auto-Update (optional)
5. Build your Installer

## Setup your Project

First add the `squirrel.windows` nuget package. You can type this in the Package Manager:

```posh
Install-Package squirrel.windows
```

## Creating a NuGet package for your app

You can use [NuGet Package Explorer](https://npe.codeplex.com/) or the command line.

Here is an example of a `.nuspec` file. You can find more information on the `.nuspec` format here: [Nuspec Reference](http://docs.nuget.org/docs/reference/nuspec-reference)

```xml
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd">
	<metadata>
		<id>MyApp</id>
		<version>1.0.0</version>
		<frameworkAssemblies>
			<frameworkAssembly assemblyName="System" targetFramework="net45" />
		</frameworkAssemblies>
	</metadata>
	<files>
		<file src="MyApp\bin\Release\MyApp.exe" target="lib\net45\MyApp.exe" />

		<!-- Don't forget to bundle Squirrel dependencies if you want to do in-app updates -->
		<file src="GoToWindow\bin\Release\Microsoft.Web.XmlTransform.dll" target="lib\net45\Microsoft.Web.XmlTransform.dll" />
		<file src="GoToWindow\bin\Release\Mono.Cecil.dll" target="lib\net45\Mono.Cecil.dll" />
		<file src="GoToWindow\bin\Release\Mono.Cecil.Mdb.dll" target="lib\net45\Mono.Cecil.Mdb.dll" />
		<file src="GoToWindow\bin\Release\Mono.Cecil.Pdb.dll" target="lib\net45\Mono.Cecil.Pdb.dll" />
		<file src="GoToWindow\bin\Release\Mono.Cecil.Rocks.dll" target="lib\net45\Mono.Cecil.Rocks.dll" />
		<file src="GoToWindow\bin\Release\NuGet.Core.dll" target="lib\net45\NuGet.Core.dll" />
		<file src="GoToWindow\bin\Release\Splat.dll" target="lib\net45\Splat.dll" />
		<file src="GoToWindow\bin\Release\Squirrel.dll" target="lib\net45\Squirrel.dll" />
		<file src="GoToWindow\bin\Release\Ionic.Zip.dll" target="lib\net45\Ionic.Zip.dll" />
	</files>
</package>
```

## Handling Squirrel Events

Squirrel events are optional, but they can be super useful, as it gives you a chance to do "custom install actions" on install / uninstall / update.

In your app's `AssemblyInfo.cs`, add the following line:

```csharp
[assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]
```

This means that your app will be executed by the installer, in a number of different scenarios, with special flags - you should handle them correctly:

* `--squirrel-install x.y.z.m` - called when your app is installed. Exit as soon as you're finished setting up the app.
* `--squirrel-firstrun` - called after everything is set up. You should treat this like a normal app run (maybe show the "Welcome" screen).
* `--squirrel-updated x.y.z.m` - called when your app is updated to the given version. Exit as soon as you're finished.
* `--squirrel-uninstall` - called when your app is uninstalled. Exit as soon as you're finished.

If your app is "Squirrel Aware", you'll also need to create icons yourself in `--squirrel-install` and `--squirrel-updated`, and  remove them in `--squirrel-uninstall`. See [INSTALLER](doc/INSTALLER.md) for additional information and examples.

## Auto Update

Simply deploy the content of the `Releases` folder to your web server (or, when developing, you can also use a directory path).

Here is simplified sample code you can use:

```csharp
try
{
	using (var mgr = new UpdateManager(@"http://your-server/releases-directory",
			"YourAppName",
			FrameworkVersion.Net45))
	{
		ReleaseEntry result = await mgr.UpdateApp();
		// Handle the update completion
	}
}
catch (Exception ex)
{
   // Handle the error
}
```

You can also run steps manually, e.g. if you want to check for updates and separately download and install them:

```csharp
using(var updateManager = new UpdateManager(@"http://your-server/releases", "YourAppName", FrameworkVersion.Net45))
{
	var updateInfo = await updateManager.CheckForUpdate();

	if (updateInfo == null || !updateInfo.ReleasesToApply.Any())
		return;

	// To get the latest version information:
	//   updateInfo.ReleasesToApply.OrderBy(x => x.Version).Last();

	await _updateManager.DownloadReleases(updateInfo.ReleasesToApply);
	await _updateManager.ApplyReleases(updateInfo);
}
```

## Create your Installer

In NuGet Package Manager Console, type `Squirrel --releasify path/to/the/nuget/package.nupkg`, where the path is from your solution root.

> Note that you may need to provide a `-p C:\Path\To\YourProject\packages`

A folder called `Releases` will be created; simply deploy it to your web server.

It will contain:

* `RELEASES`: The list of available releases
* `Setup.exe`: The installer for your application
* `YourApp.nupkg`: The actual application files
* `YourApp.delta.nupkg`: Generated when a previous release already exists, this allows for faster updating by just downloading the differences.  

## Debugging

A log file will be generated at `(Your Project)\packages\squirrel.windows.(version)\tools\SquirrelSetup.log`.

## Gotchas

* Even though the Auto Update example runs synchronously for simplicity, you should use `Task.ContinueWith` to avoid blocking your application.
* If you implement `ApplyReleases` asynchronously, and then want to exit your WPF application, keep in mind that you'll have to call `Application.Exit` using `Application.Current.Dispatcher.InvokeAsync`.

## A note about Reactive Extensions

Squirrel uses Reactive Extensions (Rx) heavily as the process necessary to retrieve, download and apply updates is best done asynchronously. If you are using the `Microsoft.Bcl.Async` package (which Squirrel also uses) you can combine the Rx APIs with the TPL async/await keywords, for maximum simplicity.