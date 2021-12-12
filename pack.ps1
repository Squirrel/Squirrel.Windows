Set-Location "$PSScriptRoot"
$ErrorActionPreference = "Stop"

# get current git version
$gitVerJson = (&nbgv get-version -f json) | ConvertFrom-Json
$version = $gitVerJson.SimpleVersion + $gitVerJson.PrereleaseVersion

& "$PSScriptRoot\vendor\NuGet.exe" pack "$PSScriptRoot\src\Clowd.Squirrel.nuspec" `
-BasePath "$PSScriptRoot\src" `
-OutputDirectory "$PSScriptRoot\build" `
-Version $version

& "$PSScriptRoot\vendor\7zip\7z.exe" a "$PSScriptRoot\build\SquirrelTools-$version.zip" -tzip -aoa -y -mmt on "$PSScriptRoot\build\publish\*"