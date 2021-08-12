# Stop the script if an error occurs
$ErrorActionPreference = "Stop"
$In = ".\build\Release\"
$Out = ".\build\publish\"
$Folders = @("./build", "./packages", "./test/bin", "./test/obj")

# Ensure a clean state by removing build/package folders
foreach ($Folder in $Folders) {
    if (Test-Path $Folder) {
        Remove-Item -path $Folder -Recurse -Force
    }
}

# Build Squirrel C++ and library files
msbuild /verbosity:minimal /restore /p:Configuration=Release

# Build single-exe packaged projects
dotnet publish -v minimal -c Release src\Update\Update.csproj -o $Out
dotnet publish -v minimal -c Release src\SyncReleases\SyncReleases.csproj -o $Out

# Copy over all files we need
Move-Item "$Out\Update.exe" -Destination "$Out\Squirrel.exe"
Move-Item "$Out\Update.com" -Destination "$Out\Squirrel.com"
# Move-Item "$Out\Update.pdb" -Destination "$Out\Squirrel.pdb"

New-Item -Path "$Out\lib" -ItemType "directory" | Out-Null
Copy-Item -Path "$In\netstandard2.0\*" -Destination "$Out\lib" -Recurse

Copy-Item "$In\Win32\Setup.exe" -Destination $Out
Copy-Item "$In\Win32\Setup.pdb" -Destination $Out
Copy-Item "$In\Win32\StubExecutable.exe" -Destination $Out
Copy-Item "$In\Win32\WriteZipToSetup.exe" -Destination $Out
Copy-Item "$In\Win32\WriteZipToSetup.pdb" -Destination $Out

Copy-Item -Path ".\vendor\7zip\*" -Destination $Out -Recurse
Copy-Item -Path ".\vendor\wix\*" -Destination $Out -Recurse

Write-Output "Successfully copied files to './build/publish'"
