@echo off

setlocal
pushd %~dp0

:parse_args
if /i "%1"=="release" set _C=/p:Configuration=Release
if /i "%1"=="init" set _INIT=1
if /i "%1"=="initialize" set _INIT=1
if /i "%1"=="inc" set _INCREMENTAL=1
if /i "%1"=="incremental" set _INCREMENTAL=1
if /i "%1"=="clean" set _INCREMENTAL= & set _CLEAN=1
if not "%1"=="" shift & goto parse_args

if not "%_INCREMENTAL"=="1" rd /s /q build packages 2> nul
if not "%_CLEAN%"=="" goto end

if "%_INIT%"=="1" git submodule update --init --recursive

nuget restore

msbuild -Restore %_C% -m -nr:false -v:m

:end
popd
endlocal
