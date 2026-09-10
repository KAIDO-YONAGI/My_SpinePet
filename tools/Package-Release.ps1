# 打包 SpinePet 便携版（时间戳目录 + app\ 子文件夹布局）：
#   release\<构建名>\app\      程序本体（publish 自包含多文件，仅 zh-Hans 语言资源）
#   release\<构建名>\LICENSE / NOTICE / THIRD_PARTY_NOTICES.md / ASSETS.md
#   release\<构建名>\licenses\  各第三方许可证原文（合规必需，勿删）
#   release\<构建名>\res\       空目录 + 放置说明（素材由使用者自行准备）
#   release\<构建名>\config.json / Launch.bat / UserTips.txt
#   → dist\<构建名>.zip
#
# 重要：本脚本不再把任何角色资源打进发行包。res\ 以空目录（含放置说明）随包分发，
# 以保证应用把资源目录解析为 <发布包>\res，而不是回退到 <发布包>\app\res。
#
# 用法: pwsh -NoProfile -File Package-Release.ps1 [-ReleaseName '构建名']
# 注：历史参数 -Character 已废弃，仅为兼容旧调用而保留。
[CmdletBinding()]
param(
    [string] $ReleaseName = ('SpinePet-Release-' + (Get-Date -Format 'yyyy-MM-dd-HH mm ss')),
    [string] $Character = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Root = Split-Path -Parent $PSScriptRoot
$ReleaseRoot = Join-Path $Root 'release'
$Release = Join-Path $ReleaseRoot $ReleaseName
$WorkRelease = Join-Path $ReleaseRoot ('.publishing-' + [guid]::NewGuid().ToString('N'))
$AppDir = Join-Path $WorkRelease 'app'
$Dist = Join-Path $Root 'dist'
$Project = Join-Path $Root 'SpinePet\src\SpinePet\SpinePet.csproj'
$UserTipsSource = Join-Path $Root 'UserTips.txt'
$LicenseSource = Join-Path $Root 'LICENSE'
$LicenseDirectory = Join-Path $Root 'licenses'
$ComplianceFiles = @(
    'LICENSE',
    'NOTICE',
    'THIRD_PARTY_NOTICES.md',
    'ASSETS.md'
)
$stage = $null
$tempZipPath = $null

if ([string]::IsNullOrWhiteSpace($ReleaseName) -or
    $ReleaseName -ne [IO.Path]::GetFileName($ReleaseName)) {
    throw "构建名必须是单个有效目录名：$ReleaseName"
}
if (Test-Path $Release) {
    throw "构建目录已存在：$Release"
}
if (-not (Test-Path -LiteralPath $UserTipsSource -PathType Leaf)) {
    throw "使用说明不存在：$UserTipsSource"
}

# 合规前置检查：授权文件缺失时直接失败，避免产出不含许可证的发行包。
foreach ($name in $ComplianceFiles) {
    $path = Join-Path $Root $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "合规文件不存在：$path"
    }
}
if (-not (Test-Path -LiteralPath $LicenseDirectory -PathType Container)) {
    throw "第三方许可证目录不存在：$LicenseDirectory"
}
$licenseFileCount = (Get-ChildItem -LiteralPath $LicenseDirectory -File).Count
if ($licenseFileCount -eq 0) {
    throw "第三方许可证目录为空：$LicenseDirectory"
}

if (-not [string]::IsNullOrWhiteSpace($Character)) {
    Write-Warning "参数 -Character 已废弃：发行包不再包含任何角色资源，该参数被忽略。"
}

New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null

# 1) 清理旧版平铺产物和 publish 中间产物；历史时间戳构建保留
$legacyReleaseEntries = @(
    'app',
    'res',
    'config.json',
    'Launch.bat',
    'UserTips.txt',
    '使用说明.txt'
)
foreach ($entry in $legacyReleaseEntries) {
    $legacyPath = Join-Path $ReleaseRoot $entry
    if (Test-Path $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Recurse -Force
    }
}

$stalePublish = Join-Path $Root 'SpinePet\src\SpinePet\bin\Release\net9.0-windows\win-x64'
if (Test-Path $stalePublish) {
    Remove-Item $stalePublish -Recurse -Force
}

try {
    # 2) 发布到临时 app\：自包含多文件 + 只保留简体中文语言资源
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

    # 4) 合规文件：许可证、声明与素材政策随发行包分发
    #    （Spine Runtimes License 要求任何形式的再分发都必须附带许可证与版权声明）
    foreach ($name in $ComplianceFiles) {
        Copy-Item -LiteralPath (Join-Path $Root $name) -Destination $WorkRelease -Force
    }
    Copy-Item -LiteralPath $LicenseDirectory -Destination $WorkRelease -Recurse -Force

    # 5) 空的 res\ 目录 + 放置说明：让应用把资源目录解析为 <发布包>\res
    #    （该目录不存在时 AppPaths 会回退到 <发布包>\app\res）。
    #    素材本身仍由使用者自行准备，本脚本不复制任何角色资源。
    New-Item -ItemType Directory -Path (Join-Path $WorkRelease 'res') -Force | Out-Null
    $resReadme = @'
SpinePet 角色资源目录
====================

本目录为空是正常的：发布包不含任何游戏素材，需要你自己准备。

放入资源后回到配置面板点 Scan 即可刷新。目录结构：

  res\<角色名>\<皮肤编号>\standing\<资源名>.skel / .atlas / .png
  res\<角色名>\<皮肤编号>\icons\<资源名>_icon.png        可选

皮肤编号缺省时可使用 00。也可以直接用面板上的 Add 导入 .skel 或 UnityFS bundle。

请保留本目录本身（里面的说明文件可以删除）：应用按此目录解析资源位置。
素材来源与授权边界见 ASSETS.md，完整使用说明见 UserTips.txt。
'@
    Set-Content `
        -LiteralPath (Join-Path $WorkRelease 'res\README.txt') `
        -Value $resReadme `
        -Encoding utf8BOM

    # 6) 便携 config.json（放在 app\ 上一级，应用会向上识别）：
    #    Characters 留空——首启时应用会扫描 res 并自动填入；发行包内没有资源，
    #    因此首启为空角色库，用户导入后才出现卡片。
    $configJson = @'
{
  "Version": "1.6",
  "Global": {
    "AllowRenderDrag": true,
    "TargetFrameRate": 60,
    "LibraryThumbnailScalePercent": 100,
    "BattleRules": {
      "StartupMode": "Normal",
      "DefaultBattleState": "Cover",
      "RightHoldThresholdMs": 300,
      "ContinuousFireWhileHeld": true,
      "ReloadOnRelease": true,
      "ShortRightClickOpensPanel": true
    }
  },
  "Characters": []
}
'@
    Set-Content -Path (Join-Path $WorkRelease 'config.json') -Value $configJson -Encoding utf8

    # 7) Launch.bat（内容纯 ASCII，避免编码问题）
    $launcher = "@echo off`r`nif not defined WINDIR if defined SystemRoot set `"WINDIR=%SystemRoot%`"`r`nstart `"`" `"%~dp0app\SpinePet.exe`" >nul 2>&1`r`n"
    [IO.File]::WriteAllText((Join-Path $WorkRelease 'Launch.bat'), $launcher)

    # 8) 使用根目录 UserTips.txt 作为唯一说明来源，并附加本次打包信息
    $userTips = [IO.File]::ReadAllText($UserTipsSource).TrimEnd()
    $packageInfo = @"


【本发布包】
- 构建时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
- 角色素材：无。发行包不含任何游戏素材，res\ 内只有一份放置说明，请自行准备后导入。
- 授权：本项目代码 GPL-3.0-or-later（见 LICENSE）；第三方组件见 THIRD_PARTY_NOTICES.md。
- 对应源码：https://gitee.com/KAIDOYONAGI/my_-spine-pet
            https://github.com/KAIDO-YONAGI/My_SpinePet
"@
    Set-Content `
        -LiteralPath (Join-Path $WorkRelease 'UserTips.txt') `
        -Value ($userTips + $packageInfo) `
        -Encoding utf8BOM

    # 9) 打 zip（顶层带时间戳目录，防止解压时文件散落）
    New-Item -ItemType Directory -Path $Dist -Force | Out-Null
    $zipPath = Join-Path $Dist ($ReleaseName + '.zip')
    if (Test-Path $zipPath) {
        throw "构建压缩包已存在：$zipPath"
    }
    $tempZipPath = Join-Path $Dist ('.publishing-' + [guid]::NewGuid().ToString('N') + '.zip')
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('SpinePet-stage-' + [guid]::NewGuid().ToString('N'))
    $stageRoot = New-Item -ItemType Directory -Path $stage -Force
    Copy-Item -Path $WorkRelease -Destination (Join-Path $stageRoot $ReleaseName) -Recurse
    Compress-Archive -Path (Join-Path $stageRoot $ReleaseName) -DestinationPath $tempZipPath

    # 10) 所有步骤成功后，再把临时产物切换成可运行的正式构建
    Move-Item -LiteralPath $WorkRelease -Destination $Release
    Move-Item -LiteralPath $tempZipPath -Destination $zipPath

    $finalAppDir = Join-Path $Release 'app'
    $rootEntryCount = (Get-ChildItem $Release -Force).Count
    $appFileCount = (Get-ChildItem $finalAppDir -Recurse -File).Count
    $zipSizeMb = '{0:N1}' -f ((Get-Item $zipPath).Length / 1MB)
    Write-Host "release: $Release（根目录 $rootEntryCount 项，app 内 $appFileCount 个文件）"
    Write-Host "合规:    $($ComplianceFiles -join ', ') + licenses\（$licenseFileCount 份许可证原文）"
    Write-Host "素材:    未包含任何角色资源（res\ 内仅一份放置说明）"
    Write-Host "zip:     $zipPath（$zipSizeMb MB）"
}
finally {
    if ($stage -and (Test-Path $stage)) {
        Remove-Item $stage -Recurse -Force
    }
    if (Test-Path $WorkRelease) {
        Remove-Item $WorkRelease -Recurse -Force
    }
    if ($tempZipPath -and (Test-Path $tempZipPath)) {
        Remove-Item $tempZipPath -Force
    }
}
