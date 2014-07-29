## Squirrel.Windows: The Next Generation

The dream of Squirrel.Windows, risen from the ashes.

#### What even is this?

This is Squirrel.Windows, rewritten to drop a lot of the things that caused the original (never finished) version of Squirrel for Windows. Here's a few examples:

* Squirrel.Windows did an enormous of work to support .NET 4.0, and brought in a ton of dependencies to make it happen. vNext requires .NET 4.5, and uses a minimum of dependencies.

* Squirrel.Windows allowed too much setup customization via installation hooks. This feature was super hard because installation hooks often had their own dependencies that blew up when we tried to load them. vNext loses this feature.

* Squirrel.Windows had a super complicated WiX-based installer that was an unholy nightmare. vNext replaces this with a single hardcoded C++ EXE whose goal is to display as little UI as possible. [Installer Spec](https://github.com/Squirrel/Squirrel.Windows.Next/blob/master/specs/Installer.md)

* Squirrel.Windows was super IObservable-heavy, when the reality is that the vast majority of installer ops should just be synchronous. Ditch Rx completely and use async/await only when necessary.

* We didn't get anything but suffering out of IO abstractions. Kill 'em all.

* Squirrel got hella confused while walking the dependency tree by trying to detect which files in the NuGet package we were *actually* using (i.e. if you're a .NET 4.5 project, you could be using binaries from `Net20`, `Net35`, `Net45`, etc). Instead, write a Targets file which simply dumps the reference list to the output directory, and use that to inform which files should be in the final package. [Tools Spec](https://github.com/Squirrel/Squirrel.Windows.Next/blob/master/specs/Tools.md)
