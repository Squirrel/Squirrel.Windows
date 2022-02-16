| [docs](..)  / [using](.) / debugging-installs.md
|:---|

# Debugging Installs

The following tips will help you to debug the installation of an Squirrel app.

## Simulating an Install and First Run

If the install of your application doesn't seem to be working, you can explore the behavior by executing the install steps from the command line:

~~~ps
C:\user\AppData\Local\MyApp> MyApp.exe --squirrel-install 1.0.0
C:\user\AppData\Local\MyApp> MyApp.exe --squirrel-firstrun
~~~

The first cmd should create some shortcuts then immediately exit, then the 2nd one should start your app ([source](https://github.com/Squirrel/Squirrel.Windows/issues/525))


---
| Return: [Table of Contents](../readme.md) |
|----|
