# SpinePet 运行环境自检 / environment self-check
#
# 用途：在目标机器上确认"哪些功能可用、缺什么"。不需要先装任何东西。
# 用法（Windows 自带的 PowerShell 5.1 即可）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File Check-Environment.ps1
# 参数：
#   -ResDirectory <路径>   指定要检查的资源目录（默认取脚本同目录下的 res\）
#
# 说明：应用本体是自包含发布，目标机不需要安装 .NET，也不需要 VC++ 运行库。
#       需要额外依赖的只有两个可选能力：UnityFS bundle 导入与图标下载（都要 Python）。

[CmdletBinding()]
param(
    [string] $ResDirectory = ''
)

# 发行包布局：脚本在包根，res\ 与 app\ 是同级目录。
# 仓库布局：脚本在 SpinePet\tools\，res\ 在上一层。
if ([string]::IsNullOrWhiteSpace($ResDirectory)) {
    $ResDirectory = Join-Path $PSScriptRoot 'res'
    if (-not (Test-Path -LiteralPath $ResDirectory)) {
        $ResDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'res'
    }
}

$ErrorActionPreference = 'Continue'

function Write-Result {
    param([string] $State, [string] $Name, [string] $Detail)
    Write-Host ('  [{0}] {1,-22} {2}' -f $State, $Name, $Detail)
}

function Test-CommandExists {
    param([string] $Name)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    return ($null -ne $cmd)
}

$results = @()

Write-Host ''
Write-Host 'SpinePet 运行环境自检'
Write-Host '======================'
Write-Host ''

# --- 1. Windows 版本 ---
$osVersion = [Environment]::OSVersion.Version
$osOk = ($osVersion.Major -ge 10)
Write-Result $(if ($osOk) { 'OK' } else { '!!' }) 'Windows' ('{0} (需要 Windows 10 或更高)' -f $osVersion)
$results += [pscustomobject]@{ Name = 'Windows 10+'; Ok = $osOk; Needed = '必需' }

# --- 2. PowerShell ---
$psVersion = $PSVersionTable.PSVersion
Write-Result 'OK' 'PowerShell' ('{0} (5.1 即可)' -f $psVersion)
$results += [pscustomobject]@{ Name = 'PowerShell'; Ok = $true; Needed = '必需' }

# --- 3. 应用本体与自包含运行时 ---
$appDirectory = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path -LiteralPath $appDirectory)) {
    $appDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'app'
}
$appPresent = Test-Path -LiteralPath $appDirectory
$appExe = Join-Path $appDirectory 'SpinePet.exe'
$appOk = $appPresent -and (Test-Path -LiteralPath $appExe)
$selfContained = $appPresent -and (Test-Path -LiteralPath (Join-Path $appDirectory 'hostfxr.dll'))
if ($appPresent) {
    Write-Result $(if ($appOk) { 'OK' } else { '!!' }) 'SpinePet.exe' $(if ($appOk) { '找到' } else { '缺少 app\SpinePet.exe' })
    $runtimeText = if ($selfContained) { '自包含，无需安装 .NET' } else { '缺少 hostfxr.dll：目标机需要 .NET' }
    Write-Result $(if ($selfContained) { 'OK' } else { '!!' }) '.NET 运行时' $runtimeText
    $results += [pscustomobject]@{ Name = '应用可运行'; Ok = ($appOk -and $selfContained); Needed = '必需' }
} else {
    Write-Result '--' 'SpinePet.exe' '未找到 app\ 目录（在源码树中运行时属正常）'
    Write-Result '--' '.NET 运行时' '跳过'
}

# --- 4. 图形能力（D3D11 / DirectComposition）---
$sys32 = Join-Path $env:SystemRoot 'System32'
$d3dOk = (Test-Path (Join-Path $sys32 'd3d11.dll')) -and (Test-Path (Join-Path $sys32 'dxgi.dll'))
$dcompOk = Test-Path (Join-Path $sys32 'dcomp.dll')
Write-Result $(if ($d3dOk) { 'OK' } else { '!!' }) 'Direct3D 11' $(if ($d3dOk) { '系统组件存在' } else { '缺少 d3d11.dll / dxgi.dll' })
Write-Result $(if ($dcompOk) { 'OK' } else { '!!' }) 'DirectComposition' $(if ($dcompOk) { '系统组件存在' } else { '缺少 dcomp.dll' })
$results += [pscustomobject]@{ Name = '桌面渲染'; Ok = ($d3dOk -and $dcompOk); Needed = '必需' }

# --- 5. Python（仅 UnityFS bundle 导入与图标下载需要）---
$pythonCommand = $null
foreach ($candidate in 'python', 'py') {
    if (Test-CommandExists $candidate) { $pythonCommand = $candidate; break }
}
if ($pythonCommand) {
    $pyVersion = (& $pythonCommand --version 2>&1 | Select-Object -First 1)
    Write-Result 'OK' 'Python' ('{0} -> {1}' -f $pythonCommand, $pyVersion)
} else {
    Write-Result '--' 'Python' '未找到（仅影响 UnityFS bundle 导入与图标下载）'
}

$modulesOk = $false
if ($pythonCommand) {
    $probe = 'import UnityPy, PIL; print("UnityPy+Pillow OK")'
    $moduleOutput = (& $pythonCommand -c $probe 2>&1 | Select-Object -First 1)
    $modulesOk = ($moduleOutput -match 'OK')
    $requirementsPath = Join-Path $PSScriptRoot 'app\Tools\requirements.txt'
    if (-not (Test-Path -LiteralPath $requirementsPath)) {
        $requirementsPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\requirements.txt'
    }
    Write-Result $(if ($modulesOk) { 'OK' } else { '--' }) 'UnityPy + Pillow' $(if ($modulesOk) { '已安装' } else { ('未安装：python -m pip install -r "{0}"' -f $requirementsPath) })
} else {
    Write-Result '--' 'UnityPy + Pillow' '跳过（无 Python）'
}
$results += [pscustomobject]@{ Name = 'UnityFS 导入 / 图标下载'; Ok = $modulesOk; Needed = '可选' }

# --- 6. 资源目录 ---
if (Test-Path -LiteralPath $ResDirectory) {
    $characters = @(Get-ChildItem -LiteralPath $ResDirectory -Directory -ErrorAction SilentlyContinue)
    $skeletons = @(Get-ChildItem -LiteralPath $ResDirectory -Recurse -Filter '*.skel' -File -ErrorAction SilentlyContinue)
    $writable = $true
    try {
        $probeFile = Join-Path $ResDirectory ('.spinepet-write-probe-' + [guid]::NewGuid().ToString('N') + '.tmp')
        Set-Content -LiteralPath $probeFile -Value 'probe' -Encoding ASCII
        Remove-Item -LiteralPath $probeFile -Force
    } catch {
        $writable = $false
    }
    Write-Result 'OK' 'res 目录' ('{0} 个角色目录 / {1} 个骨骼' -f $characters.Count, $skeletons.Count)
    Write-Result $(if ($writable) { 'OK' } else { '!!' }) 'res 可写' $(if ($writable) { '可写' } else { '不可写（导入需要写权限）' })
    $results += [pscustomobject]@{ Name = '资源目录可写'; Ok = $writable; Needed = '必需' }
} else {
    Write-Result '--' 'res 目录' ('不存在：{0}（应用会在需要时创建）' -f $ResDirectory)
    $results += [pscustomobject]@{ Name = '资源目录可写'; Ok = $false; Needed = '必需' }
}

# --- 7. 可选：.NET SDK（仅从源码构建 / 运行 CLI 导入工具时需要）---
if (Test-CommandExists 'dotnet') {
    $sdkVersion = (& dotnet --version 2>&1 | Select-Object -First 1)
    Write-Result 'OK' '.NET SDK' ('{0}（从源码构建或运行 import 工具时需要）' -f $sdkVersion)
} else {
    Write-Result '--' '.NET SDK' '未安装（仅从源码构建 / 用 dotnet run 跑导入工具时需要）'
}

# --- 8. 随包的导入 CLI 工具 ---
$importTool = Join-Path $PSScriptRoot 'tools\import\BattleCatalogImporter.exe'
if (Test-Path -LiteralPath $importTool) {
    Write-Result 'OK' '导入 CLI 工具' 'tools\import\BattleCatalogImporter.exe（自包含，可直接运行）'
} else {
    Write-Result '--' '导入 CLI 工具' '本包未附带（射击 aim/cover 导入需要它；用 -IncludeImportTools 打包）'
    $results += [pscustomobject]@{ Name = '射击 aim/cover 导入'; Ok = $false; Needed = '可选' }
}

# --- 结论 ---
Write-Host ''
Write-Host '结论'
Write-Host '----'
$blocking = @($results | Where-Object { -not $_.Ok -and $_.Needed -eq '必需' })
if ($blocking.Count -eq 0) {
    Write-Host '  必需项全部满足：把角色资源放进 res\ 后启动应用，点 Scan 即可。'
} else {
    Write-Host '  以下必需项未满足：'
    foreach ($item in $blocking) { Write-Host ('   - {0}' -f $item.Name) }
}
Write-Host ''
Write-Host '  缺 Python 时：仍可导入 .skel、手工放文件、播放与交互；'
Write-Host '  只是不能用 UnityFS bundle 导入，也不会自动下载图标。'
Write-Host '  缺导入 CLI 工具时：待机 / 爆裂 / 珍藏品仍可手工导入，射击 aim/cover 需该工具。'
Write-Host ''
