@echo off
setlocal
rem Create one immutable release using the required local timestamp format.
for /f "usebackq delims=" %%I in (`pwsh -NoProfile -Command "Get-Date -Format 'yyyy-MM-dd-HH mm ss'"`) do set "BUILD_TIMESTAMP=%%I"
if not defined BUILD_TIMESTAMP (
    echo Failed to generate the release timestamp.
    exit /b 1
)

set "RELEASE_NAME=SpinePet-Release-%BUILD_TIMESTAMP%"
echo Publishing %RELEASE_NAME%...
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Package-Release.ps1" -ReleaseName "%RELEASE_NAME%" %*
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" echo Publish failed with exit code %EXIT_CODE%.
exit /b %EXIT_CODE%
