| [docs](..)  / [contributing](.) / building-squirrel.md
|:---|

# Building Squirrel

Squirrel.Windows is a fairly typical C# / C++ project, the only special part is making sure to clone submodules via the command shown below.

For the Impatient:

```sh
git clone https://github.com/squirrel/squirrel.windows
git submodule update --init --recursive       ## THIS IS THE PART YOU PROBABLY FORGOT
.\.NuGet\NuGet.exe restore
msbuild /p:Configuration=Release
```

**Tip:** You can compile the Squirrel.Windows solution with Visual Studio version 2013 and above (including community edition).

---
| Return: [Table of Contents](../readme.md) |
|----|
