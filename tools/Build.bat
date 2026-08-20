@echo off
setlocal
title SpineTools Build

set "ROOT=%~dp0"
set "PROJECT=%ROOT%SpinePet"

if not exist "%PROJECT%\SpinePet.sln" (
    echo SpinePet.sln was not found:
    echo %PROJECT%\SpinePet.sln
    set "EXIT_CODE=1"
    goto :done
)

echo ==============================================
echo   SpineTools Release Build
echo ==============================================
echo.

pushd "%PROJECT%"
dotnet build SpinePet.sln -c Release --nologo
set "EXIT_CODE=%ERRORLEVEL%"
popd

:done
echo.
if "%EXIT_CODE%"=="0" (
    echo Build succeeded.
) else (
    echo Build failed. Exit code: %EXIT_CODE%
)
pause
exit /b %EXIT_CODE%
