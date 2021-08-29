Set-Location "$PSScriptRoot"
$ErrorActionPreference = "Stop"

# get current git version
$gitVerJson = (&nbgv get-version -f json) | ConvertFrom-Json
$version = $gitVerJson.SimpleVersion + $gitVerJson.PrereleaseVersion

& "$PSScriptRoot\.nuget\NuGet.exe" pack "$PSScriptRoot\src\Clowd.Squirrel.nuspec" `
-BasePath "$PSScriptRoot\src" `
-OutputDirectory "$PSScriptRoot\build" `
-Version $version