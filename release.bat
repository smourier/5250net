@echo off
setlocal

rem the workflow builds the committed version on GitHub, so push changes before running this file.
rem the release tag comes from Version in 5250net.csproj. An existing release is never replaced.

set "RELEASE=true"
if /i "%~1"=="build" set "RELEASE=false"
if not "%~1"=="" if /i not "%~1"=="build" goto usage
if not "%~2"=="" goto usage

where gh >nul 2>&1
if errorlevel 1 (
    echo The GitHub CLI is required. Install it from https://cli.github.com and run gh auth login.
    exit /b 1
)

pushd "%~dp0"
if errorlevel 1 exit /b 1

set "DISPATCH_OUTPUT=%TEMP%\5250net-release-%RANDOM%-%RANDOM%.txt"
echo Starting the Windows x64 and Linux x64 builds with release=%RELEASE%.
gh workflow run build.yml -f release=%RELEASE% > "%DISPATCH_OUTPUT%"
if errorlevel 1 (
    if exist "%DISPATCH_OUTPUT%" del /q "%DISPATCH_OUTPUT%"
    popd
    exit /b 1
)

type "%DISPATCH_OUTPUT%"

rem use the returned run URL to avoid watching another user's concurrent build.
set "RUNID="
for /f "tokens=7 delims=/" %%i in ('findstr /c:"/actions/runs/" "%DISPATCH_OUTPUT%"') do set "RUNID=%%i"
del /q "%DISPATCH_OUTPUT%"
if defined RUNID for /f "delims=0123456789" %%i in ("%RUNID%") do set "RUNID="
if not defined RUNID (
    echo The workflow was started. Follow its progress in the repository's Actions tab.
    popd
    exit /b 0
)

gh run watch %RUNID% --exit-status
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%

:usage
echo Usage: release.bat [build]
echo Run without arguments to build and publish a GitHub release.
echo Use build to create build artifacts without publishing a release.
exit /b 2
