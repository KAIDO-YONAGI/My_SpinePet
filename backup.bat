@echo off
chcp 65001 >nul
setlocal
title SpineTools 本地备份

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss"') do set TS=%%i

echo ==============================================
echo   SpineTools 本地备份  %TS%
echo ==============================================

call :backup_repo "D:\SpineTools" "SpineTools 主仓库"
call :backup_repo "D:\SpineTools\SpinePet" "SpinePet"

echo.
echo 全部完成。
pause
exit /b 0

:backup_repo
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
    )
) else (
    echo   无变更，跳过
)
exit /b 0
