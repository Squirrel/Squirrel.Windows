<#
Package script for electron-winstaller

The NPM package electron-winstaller allows developers to
build Windows installers for Electron apps using Squirrel
(https://github.com/electron/windows-installer)

This script copies the required files into a single folder
which can then be copied to the electron-winstaller/vendor folder
(either manually or in an automated way).
#>

# Stop the script if an error occurs
$ErrorActionPreference = "Stop"
$In = ".\build\Release\"
$Out = ".\build\electron-winstaller\"
$Folders = @("./build", "./packages", "./test/bin", "./test/obj")

# Ensure a clean state by removing build/package folders
foreach ($Folder in $Folders) {
    if (Test-Path $Folder) {
        Remove-Item -path $Folder -Recurse -Force
    }
}

# Build Squirrel
git submodule update --init --recursive
.\.NuGet\NuGet.exe restore
msbuild /p:Configuration=Release

# Create the electron-winstaller folder
New-Item -Path $Out -ItemType "directory" | Out-Null

# Copy over all files we need
Copy-Item "$In\net45\Update.exe" -Destination "$Out\Squirrel.exe"
Copy-Item "$In\net45\update.com" -Destination "$Out\Squirrel.com"
Copy-Item "$In\net45\Update.pdb" -Destination "$Out\Squirrel.pdb"
Copy-Item "$In\Win32\Setup.exe" -Destination $Out
Copy-Item "$In\Win32\Setup.pdb" -Destination $Out
Copy-Item "$In\net45\Update-Mono.exe" -Destination "$Out\Squirrel-Mono.exe"
Copy-Item "$In\net45\Update-Mono.pdb" -Destination "$Out\Squirrel-Mono.pdb"
Copy-Item "$In\Win32\StubExecutable.exe" -Destination $Out
Copy-Item "$In\net45\SyncReleases.exe" -Destination $Out
Copy-Item "$In\net45\SyncReleases.pdb" -Destination $Out
Copy-Item "$In\Win32\WriteZipToSetup.exe" -Destination $Out
Copy-Item "$In\Win32\WriteZipToSetup.pdb" -Destination $Out

Write-Output "Successfully copied files for electron-winstaller to build/electron-winstaller."
