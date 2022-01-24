using namespace System.IO
using namespace System.Text.RegularExpressions

Set-Location "$PSScriptRoot"
$ErrorActionPreference = "Stop"

# get current git version
$gitVerJson = (&nbgv get-version -f json) | ConvertFrom-Json
$version = $gitVerJson.SimpleVersion + $gitVerJson.PrereleaseVersion

# build nuget package
& "$PSScriptRoot\vendor\NuGet.exe" pack "$PSScriptRoot\src\Clowd.Squirrel.nuspec" `
-BasePath "$PSScriptRoot\src" `
-OutputDirectory "$PSScriptRoot\build" `
-Version $version

# build zip for github releases
& "$PSScriptRoot\vendor\7zip\7z.exe" a "$PSScriptRoot\build\SquirrelTools-$version.zip" -tzip -aoa -y -mmt on "$PSScriptRoot\build\publish\*"

# update readme examples with latest version
$readmePath = "$PSScriptRoot\README.md"
$newText = [Regex]::Replace([File]::ReadAllText($readmePath), "Clowd\.Squirrel\\.+?\\tools", "clowd.squirrel\$version\tools", [RegexOptions]::Multiline + [RegexOptions]::IgnoreCase)
[File]::WriteAllText($readmePath, $newText)