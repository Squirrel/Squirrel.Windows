# search for msbuild, the loaction of vswhere is guarenteed to be consistent
$MSBuildPath = (&"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe) | Out-String
$MSBuildPath = $MSBuildPath.Trim();

# This variable is null in github actions
if ($PSScriptRoot -eq $null) {
    $PSScriptRoot = "."
}

# Stop the script if an error occurs
$ErrorActionPreference = "Stop"
$In = "$PSScriptRoot\build\Release\"
$Out = "$PSScriptRoot\build\publish\"
$Folders = @("$PSScriptRoot\build", "$PSScriptRoot\packages", "$PSScriptRoot\test\bin", "$PSScriptRoot\test\obj")

# Ensure a clean state by removing build/package folders
foreach ($Folder in $Folders) {
    if (Test-Path $Folder) {
        Remove-Item -path "$Folder" -Recurse -Force
    }
}

# Build Squirrel C++ with msbuild as dotnet can't
Write-Output "Building Solution"
&"$MSBuildPath" /verbosity:minimal /restore /p:Configuration=Release

# Build single-exe packaged projects
Write-Output "Publishing SingleFile Projects"
New-Item -Path "$Out" -Name "bin" -ItemType "directory"
$BinOut = "$Out\bin"
dotnet publish -v minimal -c Release -r win-x64 --self-contained=true "$PSScriptRoot\src\Squirrel.CommandLine.Windows\Squirrel.CommandLine.Windows.csproj" -o "$Out"
dotnet publish -v minimal -c Release -r win-x86 --self-contained=true "$PSScriptRoot\src\Update.Windows\Update.Windows.csproj" -o "$Out"

# Copy over all files we need
Write-Output "Copying files to all the right places"
Copy-Item -Path "$PSScriptRoot\vendor\7zip\*" -Destination "$BinOut" -Recurse
Copy-Item -Path "$PSScriptRoot\vendor\wix\*" -Destination "$BinOut" -Recurse
Copy-Item "$In\Win32\Setup.exe" -Destination "$BinOut"
Copy-Item "$In\Win32\StubExecutable.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\rcedit.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\signtool.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\singlefilehost.exe" -Destination "$BinOut"

# Clean up files we do not need to create a nuget package
Write-Output "Cleaning up intermediate files"
Remove-Item "$Out\*.pdb"
Remove-Item "$BinOut\*.pdb"
Remove-Item "$Out\SquirrelLib.xml"
Remove-Item "$In\..\obj" -Recurse
Remove-Item "$In\Win32" -Recurse
Remove-Item "$In\net6.0-windows" -Recurse

Write-Output "Done."

