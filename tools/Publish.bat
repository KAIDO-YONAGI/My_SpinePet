@echo off
setlocal
rem 自包含打包 SpinePet 到工作区 release\（已加入 .gitignore，不入库）
set "ROOT=%~dp0.."
dotnet publish "%ROOT%\SpinePet\src\SpinePet\SpinePet.csproj" -c Release -f net9.0-windows -r win-x64 --self-contained true -p:DebugType=none -o "%ROOT%\release"
if errorlevel 1 (
    echo Publish failed.
    exit /b 1
)
if not exist "%ROOT%\release\res" mkdir "%ROOT%\release\res"
echo SpinePet published to "%ROOT%\release" (self-contained, win-x64).
