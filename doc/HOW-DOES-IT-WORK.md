# How does it work?

> How can you replace DLLs while they're loaded? Impossible!

You can't. So, how can you do it? The basic trick that [ClickOnce](http://msdn.microsoft.com/en-ca/library/142dbbz4(v=vs.90).aspx) uses is, you
have a folder of EXEs and DLLs, and an Application Shortcut. When ClickOnce
goes to update its stuff, it builds a completely *new* folder of binaries,
then the last thing it does is rewrite the app shortcut to point to the new
folder.

So, to that end, the installation root really only needs to consist of two
folders:

```
  \packages
    MyCoolApp-1.0.nupkg
    MyCoolApp-1.1-delta.nupkg
    MyCoolApp-1.1.nupkg   ## Generated from 1.0+1.1-delta
  \app-[version]
```

Packages are effectively immutable, they simply consist of the packages we've
downloaded. This means however, that we need write-access to our own install
directory - this is fine for per-user installs, but if the user has installed
to Program Files, we'll need to come up with another solution. And that
solution is, "Only support per-user installs".

## The Update process, from start to finish

### Syncing the packages directory

The first thing that the Squirrel client will do to start the updates process, is
download the remote version of "Releases". Comparing this file to the Releases
file on disk will tell us whether an update is available.

Determining whether to use the delta packages or not will depend on the
download size - the updater will take the smaller of "latest full package" vs.
"Sum of all delta packages between current and latest". The updater makes a
choice, then fetches down all the files and checks them against the SHA1s in
the Releases file.

If the installer decided to do a Delta update, it will then use the Delta
updates against the existing Full package to build a new Full package.

### Installing a full update

Since we've done the prep work to create a new NuGet package from the deltas,
the actual update process only has to deal with full NuGet packages. This is
as simple as:

1. Extract the NuGet package to a temp dir
1. Move `lib\net45` to `\app-[newversion]`
1. Rewrite the shortcut to point to `\app-[newversion]`

On next startup, we blow away `\app-[version]` since it's now the previous
version of the code.

### What do we do on Setup? (Bootstrapping)

Since the WiX setup application is too dumb to setup our default directory, in
order to simplify trying to bootstrap our app directory, we'll just recreate
it. This is some wasted bandwidth, but oh well. If the packages or app root
doesn't actually exist, we'll download the latest full release and set up the
app.

# Implementation

## Major Pieces

We need:

- A client library, which includes the core update logic
- An executable / PowerShell script to implement `New-Release`
- The actual Setup.exe that Create-Release hacks up, as well as any related
  implementation (WiX stuff?) that we need.

## Production / "Server Side"

### The tricky part

Ironically, the difficulty of using NuGet packages as a distribution container
for your app, is *if your app uses NuGet*. This is because NuGet (with good
reason!) packages the *list* of dependencies, not the actual binaries. So, if
we were to try to use the NuGet package of the App directly, we'd be missing a
bunch of DLLs.

So, we need an application that can *flatten* a NuGet dependency tree and
repack the package with all the DLLs. While this is a lot of steps, it's
actually pretty straightforward:

1. Extract the App's NuGet package to a temp directory.
1. Walk the list of dependencies. For each dependency, extract it on top of
   the temp directory (i.e. so that its `lib/*` ends up in the App's dir)
1. Recursively do the same thing (i.e. recurse down the dependency tree)
1. Edit the root NuGet package XML and remove all its explicit dependencies.

This is kind of the moral equivalent of the Rails Gem "vendor freeze" I guess.

### Delta Packages

Now, once we've got a full package, we need to generate a Delta package. To do
this, we'll replace all the DLL/EXEs in the NuGet packages with bsdiff files.
[bspatch/bsdiff](http://code.logos.com/blog/2010/12/binary_patching_with_bsdiff.html)
is a  mostly efficient algorithm for calculating diffs between binary files
(especially Native binaries, but it works well for .NET ones too), and a way
to apply them.

So, this is pretty easy:

1. Extract the previous NuGet package
1. Extract the current NuGet package
1. Replace every EXE/DLL with the bsdiff. So, `lib\net40\MyCoolApp.exe`
   becomes `lib\net40\MyCoolApp.exe.diff`. Create a file that contains a SHA1
   of the expected resulting file and its filesize called
   `lib\net40\MyCoolApp.exe.shasum`
1. New DLLs in current get put in verbatim
1. Zip it back up

The .shasum file has the same format as the Releases file described in the
"'Latest' Pointer" section, except that it will only have one entry.

So now we've got all of the *metadata* of the original package, just none of
its *contents*. To get the final package, we do the following:

1. Take the previous version, expand it out
1. Take the delta version, do the same
1. For each DLL in the previous package, we bspatch it, then check the shasum
   file to ensure we created the correct resulting file
1. If we find a DLL in the new package, just copy it over
1. If we can't find a bspatch for a file, nuke it (it doesn't exist in the new
   rev)
1. Zip it back up

### ChangeLogs / Release Notes

To write release notes for each release, we're going to reuse the
`<ReleaseNotes>` NuSpec element. However, we're going to standard that you
can write Markdown in this element, and as part of generating a flattened
package, we will render this Markdown as HTML.

### "Latest" Pointer

One of the last things we do before finishing `Create-Release` is that we
write out a simple "Releases" file alongside the flattened and Delta NuGet
packages. This is a text file that has the name of all of the release package
filenames in the folder in release order (i.e. oldest at top, newest at
bottom), along with the SHA1 hashes of their contents and their file sizes.
So, something like:

```
  94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.nupkg  1004502
  3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.nupkg  1040561
  14db31d2647c6d2284882a2e101924a9c409ee67  MyCoolApp-1.1-delta.nupkg  80396
```

This format has a number of advantages - it's dead simple, yet enables us to
check for package corruption, as well as makes it efficient to determine what
to do if a user gets multiple versions behind (i.e. whether it's worth it to
download all of the delta packages to catch them up, or to just download the
latest full package)
