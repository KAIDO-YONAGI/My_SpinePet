@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
title SpineTools 提交到本地

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss"') do set TS=%%i
set "REPO_ROOT=%~dp0.."
set "FAILED=0"

echo ==============================================
echo   SpineTools 提交到本地  %TS%
echo ==============================================

rem SpinePet is part of the root repository, not a second Git repository.
call :commit_repo "%REPO_ROOT%" "SpineTools 主仓库"
if errorlevel 1 set "FAILED=1"

echo.
if "!FAILED!"=="0" (
    echo 全部完成。
    set "EXIT_CODE=0"
) else (
    echo 存在失败，未完成全部提交。
    set "EXIT_CODE=1"
)
pause
endlocal & exit /b %EXIT_CODE%

:commit_repo
set "REPO_PATH=%~f1"
if not exist "%REPO_PATH%" (
    echo   仓库目录不存在：%REPO_PATH%
    exit /b 1
)

pushd "%REPO_PATH%" >nul 2>&1
if errorlevel 1 (
    echo   无法进入仓库目录：%REPO_PATH%
    exit /b 1
)

set "ACTUAL_ROOT="
for /f "delims=" %%r in ('git rev-parse --show-toplevel 2^>nul') do set "ACTUAL_ROOT=%%r"
if not defined ACTUAL_ROOT (
    echo   不是有效的 Git 工作区：%REPO_PATH%
    popd
    exit /b 1
)

echo.
echo [%~2] !ACTUAL_ROOT!

git add -A
if errorlevel 1 (
    echo   暂存失败，请检查 Git 输出
    popd
    exit /b 1
)

git diff --cached --quiet
set "DIFF_EXIT=!errorlevel!"
if "!DIFF_EXIT!"=="0" (
    echo   无变更，跳过
    popd
    exit /b 0
)
if not "!DIFF_EXIT!"=="1" (
    echo   无法检查暂存区状态，请检查 Git 输出
    popd
    exit /b 1
)

git commit -m "backup: %TS%"
if errorlevel 1 (
    echo   提交失败，请检查 Git 输出
    popd
    exit /b 1
)
echo   已提交 backup: %TS%

popd
exit /b 0
