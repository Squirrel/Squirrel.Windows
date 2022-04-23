using namespace System.IO
using namespace System.Text.RegularExpressions

Set-Location "$PSScriptRoot"
$ErrorActionPreference = "Stop"

# get current git version
Write-Host "Retrieving current version from nbgv" -ForegroundColor Magenta
$gitVerJson = (&nbgv get-version -f json) | ConvertFrom-Json
$version = $gitVerJson.NuGetPackageVersion
$public = $gitVerJson.PublicRelease

# build nuget package
Write-Host "Building nuget package v$version" -ForegroundColor Magenta
& "$PSScriptRoot\vendor\NuGet.exe" pack "$PSScriptRoot\src\Clowd.Squirrel.nuspec" `
-BasePath "$PSScriptRoot\src" `
-OutputDirectory "$PSScriptRoot\build" `
-Version $version

# build zip for github releases
Write-Host "Creating tools zip" -ForegroundColor Magenta
& "$PSScriptRoot\vendor\7zip\7z.exe" a "$PSScriptRoot\build\SquirrelTools-$version.zip" -tzip -aoa -y -mmt "$PSScriptRoot\build\publish\*"

if ($public) {
    Write-Host "This is a public release. README has been updated." -ForegroundColor Magenta
    # update readme examples with latest version
    $readmePath = "$PSScriptRoot\README.md"
    $newText = [Regex]::Replace([File]::ReadAllText($readmePath), "Clowd\.Squirrel\\.+?\\tools", "clowd.squirrel\$version\tools", [RegexOptions]::Multiline + [RegexOptions]::IgnoreCase)
    [File]::WriteAllText($readmePath, $newText)
}