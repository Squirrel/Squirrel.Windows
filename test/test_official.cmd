@echo off

setlocal
pushd %~dp0

set _C=Release
set _S=src\build_official.cmd

:parse_args
if /i "%1"=="debug" (set _C=Debug) & (set _S=devbuild.cmd)
if not "%1"=="" shift & goto parse_args

:: Init

if "%VCToolsVersion%"=="" call :StartDeveloperCommandPrompt || exit /b


:: Test

if not exist ..\build\%_C%\test\net45\Squirrel.Tests.dll (echo Run %_S% before running test\test_official.cmd) & (exit -1)

VSTest.Console.exe ..\build\%_C%\test\net45\*.Tests.dll || exit /b


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
