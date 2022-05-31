@echo off

setlocal
pushd %~dp0

:parse_args
@if not "%1"=="" shift & goto parse_args

:: Init

@if "%VCToolsVersion%"=="" call :StartDeveloperCommandPrompt || exit /b


:: Clean

rd /s /q ..\build ..\packages ..\test\obj ..\test\bin 2> nul


:: Build

nuget restore ..\Squirrel.sln || exit /b

msbuild -Restore ..\Squirrel.sln -p:Configuration=Release -v:m -m -nr:false -bl:..\build\logs\build.binlog || exit /b


:: Pack .nupkg

nuget pack Squirrel.nuspec -OutputDirectory ..\build\artifacts || exit /b


:: Layout electron-winstaller
::
:: The NPM package electron-winstaller allows developers to
:: build Windows installers for Electron apps using Squirrel
:: (https://github.com/electron/windows-installer)
::
:: The following copies the required files into a single folder
:: which can then be copied to the electron-winstaller/vendor folder
:: (either manually or in an automated way).

md ..\build\artifacts\electron-winstaller\vendor

copy ..\build\Release\net45\Update.exe ..\build\artifacts\electron-winstaller\vendor\Squirrel.exe || exit /b
copy ..\build\Release\net45\update.com ..\build\artifacts\electron-winstaller\vendor\Squirrel.com || exit /b
copy ..\build\Release\net45\Update.pdb ..\build\artifacts\electron-winstaller\vendor\Squirrel.pdb || exit /b
copy ..\build\Release\Win32\Setup.exe ..\build\artifacts\electron-winstaller\vendor || exit /b
copy ..\build\Release\Win32\Setup.pdb ..\build\artifacts\electron-winstaller\vendor || exit /b
copy ..\build\Release\net45\Update-Mono.exe ..\build\artifacts\electron-winstaller\vendor\Squirrel-Mono.exe || exit /b
copy ..\build\Release\net45\Update-Mono.pdb ..\build\artifacts\electron-winstaller\vendor\Squirrel-Mono.pdb || exit /b
copy ..\build\Release\Win32\StubExecutable.exe ..\build\artifacts\electron-winstaller\vendor || exit /b
copy ..\build\Release\net45\SyncReleases.exe ..\build\artifacts\electron-winstaller\vendor || exit /b
copy ..\build\Release\net45\SyncReleases.pdb ..\build\artifacts\electron-winstaller\vendor || exit /b
copy ..\build\Release\Win32\WriteZipToSetup.exe ..\build\artifacts\electron-winstaller\vendor || exit /b
copy ..\build\Release\Win32\WriteZipToSetup.pdb ..\build\artifacts\electron-winstaller\vendor || exit /b


goto LExit

:StartDeveloperCommandPrompt
if not "%SquirrelSkipVsDevCmd%"=="" (
  echo Skipping initializing developer command prompt
  exit /b
)

echo Initializing developer command prompt

if not exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
  "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
  exit /b 2
)

for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -version [16.0^,18.0^) -property installationPath`) do (
  if exist "%%i\Common7\Tools\vsdevcmd.bat" (
    call "%%i\Common7\Tools\vsdevcmd.bat" -no_logo
    exit /b
  )
  echo developer command prompt not found in %%i
)

echo No versions of developer command prompt found
exit /b 2

:LExit

popd
endlocal
