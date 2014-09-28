Set-StrictMode -version Latest
$ErrorActionPreference = "Stop"

Write-Host "Building Squirrel.SampleApp..." -ForegroundColor Green

# ==================================== Functions

Function GetMSBuildExe {
	[CmdletBinding()]
	$DotNetVersion = "4.0"
	$RegKey = "HKLM:\software\Microsoft\MSBuild\ToolsVersions\$DotNetVersion"
	$RegProperty = "MSBuildToolsPath"
	$MSBuildExe = Join-Path -Path (Get-ItemProperty $RegKey).$RegProperty -ChildPath "msbuild.exe"
	Return $MSBuildExe
}

# ==================================== Variables

$NuGet = "$PSScriptRoot\..\.nuget\NuGet.exe"
$Squirrel = "$PSScriptRoot\..\src\Update\bin\Debug\Update.exe"

$BuildPath = "$PSScriptRoot\Squirrel.SampleApp\bin\Debug"
$NuSpecPath = "$PSScriptRoot\Squirrel.SampleApp\Squirrel.SampleApp.nuspec"
$ReleasesFolder = "$PSScriptRoot\Releases"

# ==================================== NuSpec Metadata

$NuSpecXml = [xml](Get-Content $NuSpecPath)
$Version = $NuSpecXml.package.metadata.version

# ==================================== Build

If(Test-Path -Path $BuildPath) {
	Remove-Item -Confirm:$false "$BuildPath\*.*"
}

&(GetMSBuildExe) ../Squirrel.sln `
	/t:Clean`;Rebuild `
	/p:Configuration=Debug `
	/p:AllowedReferenceRelatedFileExtensions=- `
	/p:DebugSymbols=false `
	/p:DebugType=None `
	/clp:ErrorsOnly `
	/v:m

# ==================================== Squirrel

$NuPkgPath = "$PSScriptRoot\Squirrel.SampleApp\Squirrel.SampleApp.$Version.nupkg"
&($NuGet) pack $NuSpecPath

$SquirrelFullNuPkgOutputPath = "$PSScriptRoot\Releases\Squirrel.SampleApp-$Version-full.nupkg"
If(Test-Path -Path $SquirrelFullNuPkgOutputPath) {
	Remove-Item -Confirm:$false $SquirrelFullNuPkgOutputPath
}

$SquirrelDeltaNuPkgOutputPath = "$PSScriptRoot\Releases\Squirrel.SampleApp-$Version-delta.nupkg"
If(Test-Path -Path $SquirrelDeltaNuPkgOutputPath) {
	Remove-Item -Confirm:$false $SquirrelDeltaNuPkgOutputPath
}

&($Squirrel) --releasify $NuPkgPath -r "$PSScriptRoot\Releases"

# ==================================== Cleanup

If(Test-Path -Path $NuPkgPath) {
	Remove-Item -Confirm:$false $NuPkgPath
}

# ==================================== Complete

Write-Host "Build complete!" -ForegroundColor Green
