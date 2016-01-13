| [docs](..)  / [using](.) / naming.md
|:---|

# Naming conventions

In addition to the [nuget-package-metadata](nuget-package-metadata.md), there are other places that squirrel pulls naming information from. Here is the logic:

## Shortcut name
1. Use `[assembly: AssemblyProduct("MyApp")` from your exe
2. Squirrel Package `title`
3. Use `[assembly: AssemblyDescription("MyApp")`
4. Your exe filename

## Install location
1. `%appdata%/<nugetpackageid>` - **NOTE** Using .'s in your package id will cause issues

## Program and Features entry
1. Squirrel Package `title` 

---
| Return: [Table of Contents](../readme.md) |
|----|
