# 从下载的 Nikke Spine 资源压缩包一键入库。
# 用法：./Import-ResourceZip.ps1 -Zip "D:\下载\DOWNLOAD\PC _ Computer - Goddess of Victory_ Nikke - Burst - Helm_ Aquamarine.zip"
#
# 做三件事：
#   1. 解压到临时目录，整理为规范的目录名；
#   2. 备份解压后的文件到 resources\Characters\<外层名>\<内层名>\；
#   3. 把 zip 剪切到 resources\zips\。
#
# 命名规范：
#   zip 文件名去掉 "PC _ Computer - Goddess of Victory_ Nikke - " 前缀，
#   剩余部分里的 "_ "（下划线+空格）替换为 " - "，得到外层目录名，
#   例如 "Burst - Helm_ Aquamarine" -> "Burst - Helm - Aquamarine"。
#   内层目录名 = 外层名去掉首个 "Burst - " / "SSR - " 等稀有度前缀，
#   例如 "Helm - Aquamarine"。

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Zip,

    [string]$CharactersDir,
    [string]$ZipsDir
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($CharactersDir)) {
    $CharactersDir = Join-Path $projectRoot 'resources\Characters'
}
if ([string]::IsNullOrWhiteSpace($ZipsDir)) {
    $ZipsDir = Join-Path $projectRoot 'resources\zips'
}

if (-not (Test-Path -LiteralPath $Zip)) {
    throw "zip 不存在：$Zip`n详见 IMPORT.zh-CN.md「出错看哪里」与 §2.1（入库命名规范）"
}
$Zip = (Resolve-Path -LiteralPath $Zip).Path

# 1. 由 zip 文件名推导外层目录名
$base = [System.IO.Path]::GetFileNameWithoutExtension($Zip)
$prefix = 'PC _ Computer - Goddess of Victory_ Nikke - '
if ($base.StartsWith($prefix)) { $base = $base.Substring($prefix.Length) }
$outer = $base -replace '_ ', ' - '
# Burst 放到末尾：名字在前（"Burst - X - Y" -> "X - Y Burst"）
if ($outer.StartsWith('Burst - ')) {
    $outer = $outer.Substring('Burst - '.Length).TrimEnd() + ' Burst'
}
# 内层目录名 = 外层名去掉结尾 " Burst"，再去掉首个稀有度前缀
$inner = ($outer -replace ' Burst$', '') -replace '^[A-Za-z]+ - ', ''

$dest = Join-Path $CharactersDir "$outer\$inner"
if (Test-Path -LiteralPath $dest) {
    throw "目标已存在，先人工处理：$dest`n脚本不覆盖已有归档；确认后改名或删除目标，或先看 IMPORT.zh-CN.md §2.1"
}

# 2. 解压到临时目录，校验顶层只有一个文件夹，然后改名搬入 Characters
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("spinezip_" + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $Zip -DestinationPath $tmp
$tops = Get-ChildItem -LiteralPath $tmp
if ($tops.Count -ne 1 -or -not $tops[0].PSIsContainer) {
    Remove-Item -LiteralPath $tmp -Recurse -Force
    throw "zip 顶层不是唯一文件夹，中止：$($tops.Name -join ', ')`n请确认压缩包结构，详见 IMPORT.zh-CN.md §2.1"
}

New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
Move-Item -LiteralPath $tops[0].FullName -Destination $dest
Remove-Item -LiteralPath $tmp -Recurse -Force

# 3. zip 剪切到 zips 目录
New-Item -ItemType Directory -Path $ZipsDir -Force | Out-Null
Move-Item -LiteralPath $Zip -Destination (Join-Path $ZipsDir ([System.IO.Path]::GetFileName($Zip)))

# 4. 扫描骨骼集并输出导入分析报告（导入 res 由人工按报告执行）
Write-Host "`n=== 骨骼集分析 ==="
$sets = Get-ChildItem -LiteralPath $dest -Recurse -Filter '*.skel' |
    Where-Object { $_.Name -match '^c(\d+)_(\w+)' }
foreach ($s in $sets) {
    $atlas = [System.IO.Path]::ChangeExtension($s.FullName, '.atlas')
    $hasAtlas = Test-Path -LiteralPath $atlas
    if ($s.Name -match '^c(\d+)_(\w+?)') {
        $charId = $Matches[1]
    }
    $rel = $s.FullName.Substring($dest.Length + 1)
    Write-Host ("{0}  角色 {1}  atlas:{2}" -f $rel, $charId, $(if ($hasAtlas) { 'OK' } else { '缺失' }))
}
$charIds = $sets | ForEach-Object { if ($_.Name -match '^c(\d+)_') { $Matches[1] } } | Sort-Object -Unique
$namesPath = Join-Path $projectRoot 'SpinePet\src\SpinePet\Data\CharacterNames.json'
$json = Get-Content -LiteralPath $namesPath -Raw | ConvertFrom-Json
foreach ($id in $charIds) {
    if (-not $json.PSObject.Properties[$id]) {
        Write-Host "CharacterNames.json 缺少角色 $id —— 导入前需人工补充显示名" -ForegroundColor Yellow
    }
}

Write-Host "`n已入库：$dest"
Write-Host "zip 已移至：$ZipsDir"
