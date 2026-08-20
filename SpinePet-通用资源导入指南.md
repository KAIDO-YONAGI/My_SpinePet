# SpinePet 通用资源导入指南

本文按当前 `D:\SpineTools` 目录结构编写。所有资源暂存、分析、导入和
验证都在 D 盘完成。

## 1. 路径约定

```text
D:\SpineTools\
  resources\                         待分析、待加工的暂存资源
  SpinePet\
    res\                             SpinePet 实际扫描的运行时资源根
    src\SpinePet\Data\
      CharacterNames.json            角色 ID 与显示名映射
    tools\
      atlas-cleaner\                 Atlas 检查和贴图遮罩工具
      resource-layout\               旧目录迁移工具
      skeleton-inspector\            骨骼附件检查器
  nikkedb\
    l2d\
      mapped\                        已确认名称的日期化源资源
      unmapped\                      尚未唯一确认名称的日期化源资源
    metadata\                        上游原始元数据快照
    indexes\                         当前名称、日期和未确认项索引
    evidence\                        核验资料
    archive\                         回滚资料和旧结果
```

`D:\SpineTools\SpinePet\res` 是应用的唯一资源根。不要让 SpinePet 扫描
`resources`、`nikkedb\l2d`、`nikkedb\indexes`、`evidence` 或 `archive`。

`nikkedb\l2d` 目录名中的 `YYYY-MM-DD__` 前缀只用于按首次提交日期排序，
不是角色显示名或运行时资源名。导入时不要把该日期前缀带入
`SpinePet\res`。

## 2. 资源要求

每个可显示的角色皮肤至少需要一套 Spine 4.1 standing 资源：

```text
<resource>.skel
<resource>.atlas
<atlas 中引用的全部 .png>
```

骨骼、图集和 PNG 必须来自同一次 Spine 导出。`.atlas` 中声明的每个
纹理页都必须与 `.atlas` 位于同一 `standing` 目录。当前运行时要求
骨骼版本为 Spine `4.1.x`。

## 3. 文件命名

资源名必须以角色 ID 和皮肤 ID 开头：

```text
c<character-id>_<skin-id>
```

例如：

```text
c017_01.skel
c017_01.atlas
c017_01_FullNude.png
```

文件名末尾允许有额外后缀，但前两段必须能解析为角色 ID 和皮肤 ID。
迁移和导入工具按以下形式识别资源：

```text
^c<数字角色ID>_<皮肤ID>
```

角色 ID 必须存在于：

```text
D:\SpineTools\SpinePet\src\SpinePet\Data\CharacterNames.json
```

角色和皮肤身份以资源自身的文件名前缀为准。指定导入的资源直接按
`^c<数字角色ID>_<皮肤ID>` 从文件名解析即可，不需要查询 NIKKE 数据库或
其他编号索引。

## 4. 运行时目录布局

新增资源统一使用当前布局：

```text
D:\SpineTools\SpinePet\res\
  <角色显示名>\
    <皮肤ID>\
      standing\
        c<角色ID>_<皮肤ID>.skel
        c<角色ID>_<皮肤ID>.atlas
        <atlas 中引用的全部纹理页>.png
        c<角色ID>_<皮肤ID>.attachments.exclude
      icons\
        c<角色ID>_<皮肤ID>_icon.png
```

`icons` 和 `.attachments.exclude` 都是可选内容。排除文件必须与对应
`.skel` 同目录、同主文件名。

当前 `SpinePet\res` 中仍有部分旧布局：

```text
D:\SpineTools\SpinePet\res\<角色显示名>\standing\
```

应用仍能扫描旧布局。对于 `Liberalio_Base.skel`、`Burst_LM.skel`
这类不含 `c<角色ID>_<皮肤ID>` 的自定义名，扫描器只能退回到上级文件夹
作为显示名，角色 ID 和皮肤 ID 都会为空。它们可能可以显示，但皮肤归组、
官方图标和自动迁移不可靠。

不要继续向旧布局添加新资源。已使用规范文件名前缀的旧资源可以交给迁移
脚本；自定义文件名资源必须先确认身份并规范化，不能假定迁移脚本会处理。

## 5. 压缩包一键流程（入库 + 导入）

拿到下载的资源压缩包（形如
`PC _ Computer - Goddess of Victory_ Nikke - <名称>.zip`）后，整个流程分两段，
前段由脚本自动完成，后段由操作者按报告执行，两段连续做完即完成导入。

### 第一段：脚本自动入库并出报告

```powershell
pwsh -NoProfile -File 'D:\SpineTools\SpinePet\tools\zip-intake\Import-ResourceZip.ps1' `
  -Zip 'D:\下载\DOWNLOAD\PC _ Computer - Goddess of Victory_ Nikke - Burst - Helm_ Aquamarine.zip'
```

脚本自动执行：

1. 解压 zip 到临时目录，要求顶层是唯一文件夹，否则报错中止。
2. 按命名规范备份解压内容到
   `D:\SpineTools\resources\Characters\<外层名>\<内层名>\`。
3. 把 zip 剪切到 `D:\SpineTools\resources\zips\`。
4. 递归扫描备份目录中的 `c<角色ID>_<皮肤ID>*.skel`，输出每个骨骼集的
   位置、atlas 是否齐全，以及 `CharacterNames.json` 中缺失的角色 ID。

入库命名规范：

- zip 文件名去掉 `PC _ Computer - Goddess of Victory_ Nikke - ` 前缀，
  剩余部分中的 `_ `（下划线+空格）替换为 ` - `，得到外层目录名，
  例如 `Burst - Helm_ Aquamarine` → `Burst - Helm - Aquamarine`。
- 内层目录名 = 外层名去掉首个 `Burst - ` 等稀有度前缀，
  例如 `Helm - Aquamarine`。
- 目标目录已存在时脚本会报错并保留现场，由人工确认后处理，
  不会静默覆盖。

### 第二段：按报告导入 res

脚本不自动写 `res` 和 `CharacterNames.json`，因为显示名和骨骼集选择
需要人工判断。按报告执行：

1. 选择骨骼集：优先 Standing，其次 Lobby，最后 Battle；
   Battle/Lobby 的 `skillcut` 骨骼是特写取景，可能出现下半身不可见，
   属于源资源设计而非导入错误。
2. `CharacterNames.json` 缺角色 ID 时，参考外层目录名补充显示名。
3. 将选中骨骼集的 `.skel`、`.atlas` 和 atlas 引用的全部纹理页原样复制到
   `D:\SpineTools\SpinePet\res\<显示名>\<皮肤ID>\standing\`。
   不做行尾规范化、不附加排除规则，除非另行要求清理。
4. 启动 SpinePet，点击 **Scan** 验证。

### 注意事项

- **Burst/cutscene（skillcut）资源视作单独一个模型配置**，不是同一角色
  的一张皮肤。参考现有正确配置：Helm 站立版是 `c352_02`、Helm Aquamarine
  burst 版是 `c353_00`，各自独立目录、独立角色 ID。
- burst 的独立目录名 = zip 外层名去掉稀有度前缀，
  例如 `Burst - Cinderella - Crystal Wave` → `res\Cinderella Crystal Wave\00\standing\`。
- 扫描器按文件名前缀 `c<角色ID>_<皮肤ID>` 作为唯一键去重。burst 源文件
  与已导入站立版共用角色 ID 时（如都是 `c515_00`），为 burst 分配一个
  未使用的本地角色 ID（如 `515` → `5150`），只改文件名前缀中的角色数字段，
  皮肤号保持源文件原值；同时在 `CharacterNames.json` 为新 ID 添加显示名。
  改名时同步修改 atlas 首行的页面 png 引用，`.attachments.exclude`
  文件名跟随骨骼主名。**不要发明 `00cut` 之类皮肤代号**——应用里不存在
  这种配置方式。
- `CharacterNames.json` 是嵌入资源，修改后必须重新构建
  （`dotnet build src/SpinePet/SpinePet.csproj -c Release`）才生效。
  仓库 `global.json` 已放宽为 `rollForward: Major`，本机 SDK 10 可直接构建。
- Battle 与 Lobby 的 skillcut 文件通常逐字节相同，任选其一（默认 Lobby）。

## 6.## 6. 手动导入步骤

1. 从 `D:\SpineTools\resources` 或 `D:\SpineTools\nikkedb\l2d` 选择源资源。
2. 从文件名前缀解析角色 ID 和皮肤 ID；指定导入的资源直接进入下一步。
3. 检查 `.skel`、同名 `.atlas` 和 atlas 引用的全部纹理页是否齐全。
4. 在 `CharacterNames.json` 中确认角色 ID 对应的显示名。
5. 创建
   `D:\SpineTools\SpinePet\res\<角色显示名>\<皮肤ID>\standing`。
6. 复制完整 standing 资源；不要覆盖目标中的同名文件。
7. 有独立图标时，放入同一皮肤下的 `icons` 目录。
8. 启动 SpinePet，在配置面板点击 **Scan**，再选择对应角色和皮肤。

不要直接在 `nikkedb\l2d` 内修改原始骨骼、Atlas 或 PNG。需要清理或试验
时，先复制到 `D:\SpineTools\resources`，验证完成后再导入运行时目录。

## 7. 迁移现有旧布局

迁移脚本只识别以 `c<数字角色ID>_<皮肤ID>` 开头的资源。先从 SpinePet
仓库根目录预览：

```powershell
Set-Location 'D:\SpineTools\SpinePet'
.\tools\resource-layout\Migrate-CharacterResources.ps1 -WhatIf
```

确认预览没有冲突后执行：

```powershell
.\tools\resource-layout\Migrate-CharacterResources.ps1
```

也可以显式指定路径：

```powershell
.\tools\resource-layout\Migrate-CharacterResources.ps1 `
  -ResourceDirectory 'D:\SpineTools\SpinePet\res' `
  -CharacterNamesPath 'D:\SpineTools\SpinePet\src\SpinePet\Data\CharacterNames.json' `
  -WhatIf
```

迁移脚本只处理完整的 `standing` 和 `icons` 资源集，遇到冲突会拒绝覆盖。
无法识别的文件、映射表中缺少的角色，以及旧 `aim`、`cover` 目录会保留
原位。

如果预览出现：

```text
Unrecognized or incomplete file left in place
```

说明该资源不能自动迁移。按以下方式处理：

1. 在资源文件名和资源内容中确认角色 ID 与皮肤 ID。
2. 把完整资源复制到 `D:\SpineTools\resources`，不要直接改源资源。
3. 将 `.skel` 和 `.atlas` 改为同一规范主文件名，例如
   `c220_00.skel` 与 `c220_00.atlas`。
4. 如果存在 `.attachments.exclude`，同步改为
   `c220_00.attachments.exclude`。
5. Atlas 中声明的纹理页名称保持原样；只有在同时更新 `.atlas` 引用并
   完成验证时才改纹理文件名。
6. 将验证后的完整集合放入
   `SpinePet\res\<角色显示名>\<皮肤ID>\standing`。
7. 执行 **Scan** 并确认角色、皮肤、图标和动画都正常，再清理旧副本。

不要为了消除迁移警告而猜测 ID。身份无法确认的资源保留在暂存区。

## 8. UnityFS 导入

应用的 **Add** 支持现有 `.skel` 或 UnityFS bundle。bundle 文件名应以
以下格式开头：

```text
c<character-id>_<skin-id>_<standing|icons>_
```

UnityFS 导入需要 Python、UnityPy 和 Pillow。依赖安装：

```powershell
Set-Location 'D:\SpineTools\SpinePet'
python -m pip install -r .\tools\requirements.txt
```

standing bundle 会提取完整骨骼、图集和 atlas 页面到对应皮肤的
`standing` 目录；icons bundle 只写入该皮肤的 `icons` 目录。图标导入
本身不会创建角色卡。

## 9. 导入后验证

检查 atlas 引用的 PNG 是否齐全：

```powershell
$dir = 'D:\SpineTools\SpinePet\res\<角色显示名>\<皮肤ID>\standing'
$atlasPath = Join-Path $dir '<resource>.atlas'

Get-Content -LiteralPath $atlasPath |
  Where-Object { $_.Trim() -match '\.(png|jpg|jpeg|webp)$' } |
  ForEach-Object { $_.Trim() } |
  Select-Object -Unique |
  ForEach-Object {
    $pagePath = Join-Path $dir $_
    if (-not (Test-Path -LiteralPath $pagePath -PathType Leaf)) {
      Write-Error "Missing atlas page: $pagePath"
    }
  }
```

如果应用扫描不到资源，依次检查：

- 是否放在 `D:\SpineTools\SpinePet\res`，而不是源资源或索引目录；
- `.skel` 与 `.atlas` 是否同名；
- atlas 中声明的纹理页是否全部存在；
- 骨骼版本是否为 Spine 4.1.x；
- 文件名是否包含 `c<character-id>_<skin-id>`；
- 角色 ID 是否已加入 `CharacterNames.json`；
- 新资源是否位于 `<角色显示名>\<皮肤ID>\standing`；
- 同一显示名和皮肤目录中是否混入两个角色 ID。

`aim` 和 `cover` 当前不属于可导入或可渲染状态。具体限制见：

```text
D:\SpineTools\SpinePet\AIM_COVER_SUPPORT_NOTES.md
```
