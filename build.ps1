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
dotnet publish -v minimal -c Release -r win-x86 --self-contained=true "$PSScriptRoot\src\Update\Update.csproj" -o "$Out"
dotnet publish -v minimal -c Release -r win-x86 --self-contained=true "$PSScriptRoot\src\SquirrelCli\SquirrelCli.csproj" -o "$Out"

# Copy over all files we need
# Move-Item "$Out\Update.exe" -Destination "$Out\Squirrel.exe"
# Move-Item "$Out\Update.com" -Destination "$Out\Squirrel.com"

# Move-Item "$Out\Update.pdb" -Destination "$Out\Squirrel.pdb"
# New-Item -Path "$Out\lib" -ItemType "directory" | Out-Null
# Copy-Item -Path "$In\netstandard2.0\*" -Destination "$Out\lib" -Recurse

Copy-Item "$In\Win32\Setup.exe" -Destination "$Out"
Copy-Item "$In\Win32\Setup.pdb" -Destination "$Out"
Copy-Item "$In\Win32\StubExecutable.exe" -Destination "$Out"
Copy-Item "$In\Win32\WriteZipToSetup.exe" -Destination "$Out"
Copy-Item "$In\Win32\WriteZipToSetup.pdb" -Destination "$Out"

Copy-Item -Path "$PSScriptRoot\vendor\7zip\*" -Destination "$Out" -Recurse
# Copy-Item -Path "$PSScriptRoot\vendor\wix\*" -Destination "$Out" -Recurse
Copy-Item "$PSScriptRoot\vendor\NuGet.exe" -Destination "$Out"
Copy-Item "$PSScriptRoot\vendor\rcedit.exe" -Destination "$Out"
Copy-Item "$PSScriptRoot\vendor\signtool.exe" -Destination "$Out"
Copy-Item "$PSScriptRoot\vendor\singlefilehost.exe" -Destination "$Out"

Remove-Item "$Out\*.pdb"

Write-Output "Successfully copied files to './build/publish'"
