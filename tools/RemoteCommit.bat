@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
title SpineTools 提交到远程

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss"') do set TS=%%i
set "REPO_ROOT=%~dp0.."
set "FAILED=0"

echo ==============================================
echo   SpineTools 提交到远程  %TS%
echo ==============================================

rem SpinePet is part of the root repository, not a second Git repository.
call :push_repo "%REPO_ROOT%" "SpineTools 主仓库"
if errorlevel 1 set "FAILED=1"

echo.
if "!FAILED!"=="0" (
    echo 全部完成。
    set "EXIT_CODE=0"
) else (
    echo 存在失败，未完成全部推送。
    set "EXIT_CODE=1"
)
pause
endlocal & exit /b %EXIT_CODE%

:push_repo
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

for /f "delims=" %%r in ('git rev-parse --show-toplevel 2^>nul') do set "ACTUAL_ROOT=%%r"
if not defined ACTUAL_ROOT (
    echo   不是有效的 Git 工作区：%REPO_PATH%
    popd
    exit /b 1
)
for /f "delims=" %%u in ('git remote get-url origin 2^>nul') do set "REMOTE_URL=%%u"
if not defined REMOTE_URL (
    echo   未配置远程 origin，无法推送
    popd
    exit /b 1
)

echo.
echo [%~2] !ACTUAL_ROOT!
echo   远程：!REMOTE_URL!

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

for /f "delims=" %%b in ('git branch --show-current 2^>nul') do set "BRANCH=%%b"
if not defined BRANCH (
    echo   当前处于 detached HEAD，无法确定推送分支
    popd
    exit /b 1
)

git push origin "HEAD:refs/heads/!BRANCH!"
if errorlevel 1 (
    echo   推送失败，请检查远程地址、网络或凭据
    popd
    exit /b 1
)

set "LOCAL_HEAD="
set "REMOTE_HEAD="
for /f "delims=" %%h in ('git rev-parse HEAD 2^>nul') do set "LOCAL_HEAD=%%h"
for /f "tokens=1" %%h in ('git ls-remote origin "refs/heads/!BRANCH!" 2^>nul') do set "REMOTE_HEAD=%%h"
if not defined REMOTE_HEAD (
    echo   推送后无法读取远程分支，请检查远程仓库
    popd
    exit /b 1
)
if /i not "!LOCAL_HEAD!"=="!REMOTE_HEAD!" (
    echo   推送校验失败：远程提交与本地 HEAD 不一致
    echo   本地：!LOCAL_HEAD!
    echo   远程：!REMOTE_HEAD!
    popd
    exit /b 1
)
echo   已推送并校验远程 origin/!BRANCH!
popd
exit /b 0
