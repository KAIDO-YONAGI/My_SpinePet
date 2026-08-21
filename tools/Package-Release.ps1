# 打包 SpinePet 便携版（app\ 子文件夹布局）：
#   release\app\      程序本体（publish 自包含多文件，仅 zh-Hans 语言资源）
#   release\res\      默认角色资源
#   release\config.json / Launch.bat / README.txt
#   → dist\SpinePet-<日期>.zip
# 用法: pwsh -NoProfile -File Package-Release.ps1 [-Character '角色名']
[CmdletBinding()]
param(
    [string] $Character = 'Scarlet Overload'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Root = Split-Path -Parent $PSScriptRoot
$Release = Join-Path $Root 'release'
$AppDir = Join-Path $Release 'app'
$Dist = Join-Path $Root 'dist'
$Project = Join-Path $Root 'SpinePet\src\SpinePet\SpinePet.csproj'
$ResourceRoot = Join-Path $Root 'SpinePet\res'

# 1) 清理旧产物（release 可整体重建；bin 下的 win-x64 是 publish 中间产物）
if (Test-Path $Release) {
    Remove-Item $Release -Recurse -Force
}
$stalePublish = Join-Path $Root 'SpinePet\src\SpinePet\bin\Release\net9.0-windows\win-x64'
if (Test-Path $stalePublish) {
    Remove-Item $stalePublish -Recurse -Force
}

# 2) 发布到 app\：自包含多文件 + 只保留简体中文语言资源
& dotnet publish $Project -c Release -f net9.0-windows -r win-x64 `
    --self-contained true -p:DebugType=none -p:SatelliteResourceLanguages=zh-Hans `
    -o $AppDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)"
}

# 3) 兜底清理：非 zh-Hans 的卫星语言目录（内含 *.resources.dll）与 pdb
Get-ChildItem $AppDir -Directory |
    Where-Object {
        $_.Name -ne 'zh-Hans' -and
        (Test-Path (Join-Path $_.FullName '*.resources.dll'))
    } |
    Remove-Item -Recurse -Force
Get-ChildItem $AppDir -Recurse -Filter '*.pdb' | Remove-Item -Force

# 4) 复制默认角色资源（决定开箱默认显示哪只桌宠）
$characterSource = Join-Path $ResourceRoot $Character
if (-not (Test-Path $characterSource)) {
    throw "默认角色不存在：$characterSource"
}
Copy-Item -Path $characterSource -Destination (New-Item -ItemType Directory -Path (Join-Path $Release 'res') -Force) -Recurse

# 5) 便携 config.json（放在 app\ 上一级，应用会向上识别）：
#    Characters 留空——首启时应用会扫描 res 并自动填入默认角色。
$configJson = @'
{
  "Version": "1.5",
  "Global": {
    "AllowRenderDrag": true,
    "TargetFrameRate": 60,
    "LibraryThumbnailScalePercent": 100
  },
  "Characters": []
}
'@
Set-Content -Path (Join-Path $Release 'config.json') -Value $configJson -Encoding utf8

# 6) Launch.bat（内容纯 ASCII，避免编码问题）
$launcher = "@echo off`r`nstart `"`" `"%~dp0app\SpinePet.exe`"`r`n"
[IO.File]::WriteAllText((Join-Path $Release 'Launch.bat'), $launcher)

# 7) README.txt（文件名用英文，内容中文）
$readme = @"
SpinePet 桌宠（NIKKE Spine 桌面宠物）使用说明
================================================

【启动】
双击「Launch.bat」，或直接双击 app\SpinePet.exe。
开箱默认显示一只桌宠（$Character），在屏幕底部边缘找她。
右下角托盘图标：左键打开设置面板 / 显示全部 / 隐藏全部 / 退出。
界面卡死时的紧急退出热键：Ctrl+Alt+Shift+F12。

【目录结构】
Launch.bat        启动入口，效果同直接运行 app\SpinePet.exe
config.json       便携配置：角色、位置、缩放、动画速度都记录在这里，随文件夹走
res\              角色资源库（默认已带 $Character）
app\              程序本体：exe、运行时组件、内部工具，请勿改动或改名
Logs\             运行日志（首次运行后生成，排查问题用）

【添加新角色】
把角色资源放进 res\，按以下结构（皮肤号没有就建一个 00）：
    res\<角色名>\<皮肤号>\standing\<资源名>.skel / .atlas / .png
    res\<角色名>\<皮肤号>\icons\<资源名>_icon.png    （角色头像，可选）
重启程序后在设置面板的角色库里即可看到并显示。
也可以直接在设置面板里导入 NIKKE 资源 zip，程序会自动入库。

【卸载】
直接删除整个文件夹即可；便携模式下不在注册表或 AppData 留下任何东西。

SpinePet · 构建日期 $(Get-Date -Format 'yyyy-MM-dd')
"@
Set-Content -Path (Join-Path $Release '使用说明.txt') -Value $readme -Encoding utf8BOM

# 8) 打 zip（顶层带 SpinePet\ 文件夹，防止解压时文件散落）
New-Item -ItemType Directory -Path $Dist -Force | Out-Null
$zipPath = Join-Path $Dist ('SpinePet-' + (Get-Date -Format 'yyyyMMdd') + '.zip')
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
$stage = Join-Path ([IO.Path]::GetTempPath()) ('SpinePet-stage-' + [guid]::NewGuid().ToString('N'))
$stageRoot = New-Item -ItemType Directory -Path $stage -Force
Copy-Item -Path $Release -Destination (Join-Path $stageRoot 'SpinePet') -Recurse
Compress-Archive -Path (Join-Path $stageRoot 'SpinePet') -DestinationPath $zipPath
Remove-Item $stage -Recurse -Force

$rootEntryCount = (Get-ChildItem $Release -Force).Count
$appFileCount = (Get-ChildItem $AppDir -Recurse -File).Count
$zipSizeMb = '{0:N1}' -f ((Get-Item $zipPath).Length / 1MB)
Write-Host "release: $Release（根目录 $rootEntryCount 项，app 内 $appFileCount 个文件）"
Write-Host "zip:     $zipPath（$zipSizeMb MB）"
