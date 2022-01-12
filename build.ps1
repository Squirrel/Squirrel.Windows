# search for msbuild, the loaction of vswhere is guarenteed to be consistent
$MSBuildPath = (&"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe) | Out-String
$MSBuildPath = $MSBuildPath.Trim();

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
&"$MSBuildPath" /verbosity:minimal /restore /p:Configuration=Release

# Build single-exe packaged projects
# New-Item -Path "$Out" -Name "win-x86" -ItemType "directory"
$BinOut = $Out
dotnet publish -v minimal -c Release -r win-x86 --self-contained=true "$PSScriptRoot\src\SquirrelCli\SquirrelCli.csproj" -o "$Out"
dotnet publish -v minimal -c Release -r win-x86 --self-contained=true "$PSScriptRoot\src\Update\Update.csproj" -o "$BinOut"

# Copy over all files we need
Copy-Item -Path "$PSScriptRoot\vendor\7zip\*" -Destination "$BinOut" -Recurse
Copy-Item -Path "$PSScriptRoot\vendor\wix\*" -Destination "$BinOut" -Recurse
Copy-Item "$In\Win32\Setup.exe" -Destination "$BinOut"
Copy-Item "$In\Win32\StubExecutable.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\rcedit.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\signtool.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\singlefilehost.exe" -Destination "$BinOut"

Remove-Item "$Out\*.pdb"
Remove-Item "$BinOut\*.pdb"
Remove-Item "$Out\SquirrelLib.xml"

Write-Output "Successfully copied files to './build/publish'"
