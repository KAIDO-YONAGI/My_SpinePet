@echo off
chcp 65001 >nul
setlocal
title SpineTools 提交到远程

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss"') do set TS=%%i

echo ==============================================
echo   SpineTools 提交到远程  %TS%
echo ==============================================

call :push_repo "%~dp0.." "SpineTools 主仓库"
call :push_repo "%~dp0..\SpinePet" "SpinePet"

echo.
echo 全部完成。
pause
exit /b 0

:push_repo
cd /d "%~1"
echo.
echo [%~2] %~1
git add -A
git diff --cached --quiet
if errorlevel 1 (
    git commit -q -m "backup: %TS%"
    if errorlevel 1 (
        echo   提交失败，请检查输出
    ) else (
        echo   已提交 backup: %TS%
        git push
        if errorlevel 1 (
            echo   推送失败，请检查网络或凭据
        ) else (
            echo   已推送到远程 origin
        )
    )
) else (
    echo   无变更，跳过
)
exit /b 0
