# Contributing to Squirrel

To contribute, please join the #squirrel [Slack](http://slack.com) room. Check out https://github.com/Squirrel/Squirrel.Windows.Next/issues/9 for instructions.

## Style

* Spaces, not tabs
* Brackets on the same line for code blocks other than functions

## Tests

[xunit](http://xunit.github.io/) is used in thie project. Make sure all tests run before creating pull requests, or that the test fails with a clear `TODO`.

To run the tests, first open a command line or PowerShell window and navigate to the Squirrel repository root. Then run:

    .\packages\xunit.runners.2.0.0-beta-build2700\tools\xunit.console.exe .\test\bin\Debug\Squirrel.Tests.dll

See [Getting started with xunit](http://xunit.github.io/doc/getting-started.html) for more details.

## Running a build on your own project

The output of the Squirrel build generates `Update.com`. You can use this exactly as you would use `Squirrel.com` when included with NuGet.

Navigate to your project directory, and execute:

    Squirrel.Windows.Next\src\Update\bin\Debug\Update.com --releasify .\MyProject.nupkg
