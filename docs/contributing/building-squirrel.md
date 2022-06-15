| [docs](..)  / [contributing](.) / building-squirrel.md
|:---|

# Building Squirrel

Squirrel.Windows is a fairly typical C# / C++ project, the only special part is making sure to clone submodules via the command shown below.

For the Impatient:

```sh
git clone https://github.com/squirrel/squirrel.windows
cd squirrel.windows
git submodule update --init --recursive       ## THIS IS THE PART YOU PROBABLY FORGOT
devbuild.cmd
```

**Tip:** You can compile the Squirrel.Windows solution with Visual Studio version 2019 and above (including community edition).

**Tip:** For Visual Studio versions that use the Visual Studio Installer (2017/2019 and above), you will need to have at least both Desktop .NET development and Desktop C++ development workloads checked in the Visual Studio Installer. You will also need to make sure that the individual package for the VC++ version used by Squirrel is checked.

---
| Return: [Table of Contents](../readme.md) |
|----|
