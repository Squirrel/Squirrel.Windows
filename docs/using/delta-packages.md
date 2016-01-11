| [docs](..)  / [using](.) / delta-packages.md
|:---|


# Delta Packages

Now, once we've got a full package, we need to generate a Delta package. To do this, we'll replace all the DLL/EXEs in the NuGet packages with bsdiff files. [bspatch/bsdiff](http://code.logos.com/blog/2010/12/binary_patching_with_bsdiff.html) is a  mostly efficient algorithm for calculating diffs between binary files (especially Native binaries, but it works well for .NET ones too), and a way to apply them.

So, this is pretty easy:

1. Extract the previous NuGet package
1. Extract the current NuGet package
1. Replace every EXE/DLL with the bsdiff. So, `lib\net40\MyCoolApp.exe` becomes `lib\net40\MyCoolApp.exe.diff`. Create a file that contains a SHA1 of the expected resulting file and its filesize called `lib\net40\MyCoolApp.exe.shasum`
1. New DLLs in current get put in verbatim
1. Zip it back up

The .shasum file has the same format as the Releases file described in the "'Latest' Pointer" section, except that it will only have one entry.

So now we've got all of the *metadata* of the original package, just none of its *contents*. To get the final package, we do the following:

1. Take the previous version, expand it out
1. Take the delta version, do the same
1. For each DLL in the previous package, we bspatch it, then check the shasum file to ensure we created the correct resulting file
1. If we find a DLL in the new package, just copy it over
1. If we can't find a bspatch for a file, nuke it (it doesn't exist in the new rev)
1. Zip it back up


---
| Return: [Table of Contents](../readme.md) |
|----|



