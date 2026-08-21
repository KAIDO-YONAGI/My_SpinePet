@echo off
setlocal
rem Package portable SpinePet release (see Package-Release.ps1 for details):
rem   publish with zh-Hans only -> copy default character res -> config/readme/launcher -> dist zip
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Package-Release.ps1" %*
exit /b %ERRORLEVEL%
