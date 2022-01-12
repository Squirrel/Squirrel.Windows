# Vendor Binaries
This folder contains pre-compiled binaries from a variety of sources. These should be updated periodically.

### singlefilehost.exe
- This is the native exe that has the .net native runtime linked in. 
- It is also used for re-packing the `Update.exe` binary.
- Can be found in the dotnet SDK at "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x86\6.0.0\runtimes\win-x86\native\singlefilehost.exe".
- Can also be downloaded from NuGet, from here https://www.nuget.org/packages/Microsoft.NETCore.App.Host.win-x86/6.0.0
- MIT License: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

### rcedit.exe
- Updates PE resources, like VersionInfo or icons. It is used when generating `Setup.exe` and `Update.exe` to apply the user preferences.
- Can be found at https://github.com/electron/rcedit/releases
- MIT License: https://github.com/electron/rcedit/blob/master/LICENSE

### signtool.exe
- Signs application binaries while building Squirrel packages.
- Can be found in the Windows SDK at "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x86\signtool.exe" or similar, depending on the version of the SDK you have installed.
- License? https://github.com/dotnet/docs/issues/10478

### 7z.exe / 7z.dll
- Incldued because it is much faster at zipping / unzipping than the available managed algorithms.
- Can be found at https://www.7-zip.org/
- License is LGPL & BSD 3: https://www.7-zip.org/license.txt

### wix
- Can be used to build persistent msi packages for the purposes of AD deployment
- Can be found at https://github.com/wixtoolset/wix3/releases
- MS-RL License: https://github.com/wixtoolset/wix3/blob/develop/LICENSE.TXT