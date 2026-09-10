# SpinePet 素材导入指南 / Asset Import Guide

[English](IMPORT.en.md) | **中文**

面向使用者的导入说明。**开发/自动化流程的权威规范**在
[`Y_MultipleAgentWorkflow/Resources/Load/SpinePet_Resources_Load_Guide.md`](Y_MultipleAgentWorkflow/Resources/Load/SpinePet_Resources_Load_Guide.md)
（珍藏品另有
[`Favorite_Interactive_Import_Guide.md`](Y_MultipleAgentWorkflow/Resources/Load/Favorite_Interactive_Import_Guide.md)，
清理见 [`SpineResource_Match_Clean_Guide.md`](Y_MultipleAgentWorkflow/Resources/MatchClean/SpineResource_Match_Clean_Guide.md)，
射击状态模型见 [`Aim_Cover_Proposal.md`](Y_MultipleAgentWorkflow/Resources/StateSupport/Aim_Cover_Proposal.md)）。
本文是它们的**使用者视角汇总**；两者冲突时以规范文档为准。运行环境与裸机依赖清单见 [第 8 节](#8-运行环境要求裸机)。

## 导入流程速览（先看这一节）

### 六步

| 步骤 | 做什么 | 命令 / 产物 |
| --- | --- | --- |
| ① 定清单 | 写明这次只处理哪几套资源 | 一张清单——**禁止**扫描全库 |
| ② 入库 | zip 用脚本；文件夹**剪切**归档 | `Import-ResourceZip.ps1 -Zip '<zip>'` → `resources\Characters\` |
| ③ 预检 | Spine 4.1 ？三件套齐全？身份与新 ID？图标来源？ | 缺一项就停在外面，**不进** `res\` |
| ④ 写入 | 放成 `res\<资源全名>\<皮肤编号>\<状态>\` | 待机/爆裂/珍藏品：`Add` 或手工放；射击：审计 + 导入两条命令 |
| ⑤ 登记 | `CharacterNames.json` 加新 ID | 之后必须 `dotnet build`，否则名字不生效 |
| ⑥ 验收 | 退出应用 → 写入 → 启动 → `Scan` | 对照「出错看哪里」与 §4.1、§5 |

四类资源怎么走（一句话版）：

- **待机 standing**：`Add` 导入 `.skel`，或手工放进 `<皮肤>\standing\` → `Scan`。
- **射击 aim / cover**：`battle-catalog-importer --audit` 通过后执行导入；应用**自动生成** Battle 配置。
- **爆裂 skillcut**：当成待机导入，目录名以 ` Burst` 结尾；Battle 与 Lobby 相同就取 Lobby。
- **珍藏品 Favorite**：编号前加 `9`、皮肤固定 `00`，只放 `standing\`，**不进** Battle。

### 出错看哪里

| 症状 / 报错 | 看哪里 |
| --- | --- |
| 点 `Scan` 什么都不出现 | 本文 §5 排查顺序（8 步） |
| `Missing atlas page: xxx.png` | 本文 §5 第 3 步；示例 2：**页面名以 atlas 声明为准**，不一定等于骨骼名 |
| 卡片出现了但名字不对 / 是空的 | 本文 §2.4 → 改完名字表必须重新构建 |
| 提示角色 ID 不在名字表 | 本文 §2.4；珍藏品看 §1.4（本地编号 `9NNN`） |
| `A character resource already exists` | 本文 §5「冲突语义」：不覆盖，先备份再决定 |
| 导入被拒：骨骼版本不支持 | 本文 §7：必须 Spine 4.1.x |
| 两张卡互相吞掉 / 打不开 | 本文 §2：禁止共用前缀或显示名 |
| 头像不对（超高竖图、明显不是角色） | 本文 §7「图标别取错源」 |
| 图标下载失败 | 本文 §8（缺 Python）；应用会回退用立绘贴图，不影响使用 |
| 射击资源导不进去 | 本文 §1.2 与 §3.1：必须走审计/导入命令，`Add` **不接受** aim/cover |
| 覆盖 `res` 文件失败（`Device or resource busy`） | 本文 §4：先退出 SpinePet |
| zip 入库报「目标已存在」 | 本文 §2.1：脚本不覆盖，先人工确认 |
| 不知道目标机缺什么 | 发行包内 `Check-Environment.ps1`；本文 §8 |
| 想看开发/自动化流程的权威规范 | `Y_MultipleAgentWorkflow\Resources\Load\SpinePet_Resources_Load_Guide.md` |

**一句话**：出错先看本文对应小节；本文没写清的，去 §3.1 查该工具的输出说明；再不够，查
`Y_MultipleAgentWorkflow\` 下的权威规范。

## 完整示例：一次导入从头到尾

> 第 0–7 节是规范条文，**这一节是照着就能做的完整示例**。
> 下面所有落地路径都取自本机真实资源（可在 `SpinePet\res` 里直接对照）。

### 通用骨架（四类资源都一样）

```text
① 入库        zip → zip-intake 脚本；文件夹 → 剪切到 resources\Characters\<规范名>
② 定清单      这次只处理哪几套，写下来（禁止扫描全库）
③ 预检        骨骼 Spine 4.1？三件套齐全？身份与新 ID？头像来源？
④ 写入        res\<资源全名>\<皮肤编号>\<状态>\
⑤ 登记        CharacterNames.json 加新 ID → dotnet build 才生效
⑥ 验收        退出应用 → 写入 → 启动 → Scan → 见 §5
```

### 示例 1：待机（最省事，走应用内 Add）

- **输入**：`c472_00.skel` + `c472_00.atlas` + `c472_00.png`（同一次 Spine 4.1 导出，三件套同名）
- **动作**：面板 `Add` 选中 `c472_00.skel`；应用校验骨骼/atlas/贴图后**事务写入**，
  显示名来自 `CharacterNames.json` 的 `472 → Scarlet Overload`，随后自动尝试补该皮肤图标
- **落地结果**（真实）：

```text
res\Scarlet Overload\00\standing\c472_00.skel
res\Scarlet Overload\00\standing\c472_00.atlas
res\Scarlet Overload\00\standing\c472_00.png
res\Scarlet Overload\00\icons\c472_00_icon.png
```

- **验收**：点 `Scan` → 卡片 `Scarlet Overload` 出现，默认动画 `idle`，缩放 100% / 1.0 倍，
  默认隐藏（不自动显示）

### 示例 2：待机变体（要自己改号：`191_02` → `19102`）

- **输入**：源资源 `Alice Variant 02`，骨骼原名 `c191_02_00.skel`（源角色 ID `191`、皮肤 `02`）
- **动作**：
  1. 新 ID = `191` + `02` = `19102`；**只改角色数字段，皮肤号保留 `02`**
  2. `c191_02_00.skel` → `c19102_02_00.skel`，同名 `.atlas` 一并改名
  3. atlas 首行声明的**页面 png 名**与贴图文件名按新编号同步改名
     —— 页面名以 atlas 自身声明为准，**不一定等于骨骼名**：本机这套骨骼是
     `c19102_02_00.skel`，而页面是 `c19102_02.png` 与 `c19102_02_2.png`
     （多页 atlas 会自动带 `_2`、`_3` 后缀）
  4. `CharacterNames.json` 加 `"19102": "Alice Variant 02"`
  5. `dotnet build src\SpinePet\SpinePet.csproj -c Release`
- **落地结果**（真实）：

```text
res\Alice Variant 02\02\standing\c19102_02_00.skel
res\Alice Variant 02\02\standing\c19102_02_00.atlas
res\Alice Variant 02\02\standing\c19102_02.png        ← atlas 声明的第 1 页
res\Alice Variant 02\02\standing\c19102_02_2.png      ← atlas 声明的第 2 页
res\Alice Variant 02\02\icons\c19102_02_icon.png
```

### 示例 3：射击（成对，先审计后导入）

- **输入**：同一皮肤下的 `standing` / `aim` / `cover` 三套（示例：`Anis Star`，ID `0170`）
- **命令**：

```powershell
# ① 只审计，不写任何文件
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  --audit resources\Characters "Anis Star"

# ② 审计逐项确认后再导入
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  resources\Characters SpinePet\res "Anis Star"
```

- **审计输出长这样**（字段实测）：`CompleteSets` 里出现
  `Identity: ResourceName=c0170_aim, CharacterCode=0170, SkinCode=aim, DisplayName=Anis Star`，
  `Standing` / `Aim` / `Cover` 三套齐全才会计入
- **落地结果**（真实）：

```text
res\Anis Star\00\standing\c0170_00.skel / .atlas / .png
res\Anis Star\00\aim\c0170_aim_00.skel / .atlas / .png
res\Anis Star\00\cover\c0170_cover_00.skel / .atlas / .png
res\Anis Star\00\icons\c0170_00_icon.png
```

- **注意**：只有一边（例如只有 aim）时**不要导入**，它就当普通待机角色用；
  单状态资源（如珍藏品）会被审计直接跳过——实测指定 `Bay Favorite` 时进入
  `SkippedEntries`（aim/cover 均为 false）

### 示例 4：爆裂（zip 入库 → 取 Lobby → 改号）

- **输入 zip**：`PC _ Computer - Goddess of Victory_ Nikke - Burst - Helm_ Aquamarine.zip`
- **命令**：

```powershell
pwsh -NoProfile -File 'SpinePet\tools\zip-intake\Import-ResourceZip.ps1' -Zip '<zip 完整路径>'
```

- **实测输出**（脚本在临时目录里跑同一个真实 zip 的结果）：

```text
=== 骨骼集分析 ===
  Battle\Sprite Sheet and Other Assets\c353_00_skillcut.skel  角色 353  atlas:OK
  Lobby\Sprite Sheet and Other Assets\c353_00_skillcut.skel   角色 353  atlas:OK

已入库：resources\Characters\Helm - Aquamarine Burst\Aquamarine
zip 已移至：resources\zips
```

- **接着做**：Battle 与 Lobby 的 skillcut 相同 → **取 Lobby**；目录名以 ` Burst` 结尾；
  改号规则同示例 2
- **落地结果**（真实的 Cinderella 那套，源 `515_00` → 本地 `5150`）：

```text
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.skel
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.atlas
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.png
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.attachments.exclude   （做过清理才有）
res\Cinderella Crystal Wave Burst\00\icons\c5150_00_icon.png
CharacterNames.json: "5150": "Cinderella Crystal Wave Burst"
```

- **注意**：skillcut 的 `idle` 可能只有半身，这是源资源设计，不是导入错误

### 示例 5：珍藏品（本地编号前加 9）

- **输入**：`favorite_c072_00.skel` + `.atlas` + `.png`（原编号 `072` = Diesel）
- **动作**：
  1. 本地编号 = `9` + `072` = `9072`，皮肤固定 `00`，显示名 `Diesel Favorite`
  2. atlas 页引用与贴图改名 `c9072_00.png`
  3. 图标用**原编号**查 `si_c072_00_s.png`，复制后命名为 `c9072_00_icon.png`
  4. `CharacterNames.json` 加 `"9072": "Diesel Favorite"`
- **落地结果**（真实）：

```text
res\Diesel Favorite\00\standing\c9072_00.skel
res\Diesel Favorite\00\standing\c9072_00.atlas
res\Diesel Favorite\00\standing\c9072_00.png
res\Diesel Favorite\00\icons\c9072_00_icon.png
```

- **注意**：不进 Battle、不建 aim/cover；默认常驻动画是精确 `idle_merged`，
  点击动画回退到 `expression_merged`

### 全部导入完成后统一验收

```powershell
# 逐套检查 atlas 声明的贴图页是否齐全（规范 §5 的脚本）
$dir = 'SpinePet\res\<资源全名>\<皮肤编号>\standing'
Get-Content "$dir\<resource>.atlas" |
  Where-Object { $_.Trim() -match '\.(png|jpg|jpeg|webp)$' } |
  Select-Object -Unique | ForEach-Object {
    if (-not (Test-Path "$dir\$($_.Trim())")) { Write-Error "Missing atlas page: $($_.Trim())" } }
```

然后启动 SpinePet → `Scan` → 逐张确认：名称、头像、默认 `idle`、点击动画、拖动边界。

## 0. 先记住三件事

1. 程序只认一种布局：`res\<资源全名>\<皮肤编号>\<状态>\`，骨骼文件名必须带
   `c<角色ID>_<皮肤ID>` 前缀。
2. **游戏导出的原始资源几乎不能直接用**——目录名、角色编号、atlas 页引用、
   图标来源都要改，这正是"导入不方便"的根源（见第 2 节）。
3. 一套资源要能被 Scan 到，必须同目录同时具备：
   `<资源名>.skel` + **同名** `.atlas` + atlas 引用的**全部**贴图页，
   且由同一次 **Spine 4.1.x** 导出。缺一样就停在 `resources\`，不要放进 `res\`。

**全程原则（规范原文，简化版）：**

1. 身份只看**文件名前缀** `c<角色ID>_<皮肤ID>`，不看目录名猜。
2. **没有指定清单，不得扫描或导入整个资源库**——每次只处理你写明的那几套。
3. 写进 `res\` 的文件**原样复制**：不改内容、不改行尾，除非明确要求清理。
4. 源资源**只入库一次**；之后的调整只改 `res\` 或名字表，**不回写源**。
5. 图标属于**导入前预检**，不是事后补漏：定位不到就报 `MissingIcon` 并停该项。
6. 应用运行时只访问运行时的 `res\` 与包根 `tools\`，**不会**去扫 `resources\` 或上游镜像。

## 1. 四种资源类型

| 类型 | 目录特征 | 骨骼/动画 | 导入方式 | 关键注意 |
| --- | --- | --- | --- | --- |
| **待机** standing | `<皮肤>\standing\` | `idle` 常驻 | 应用内 **Add**（`.skel` 或 UnityFS bundle），或手工放入 | 最常规的一类 |
| **射击** aim / cover | `<皮肤>\aim\` + `<皮肤>\cover\` | 成对完整才生成 Battle | `battle-catalog-importer`（**必须给指定清单**） | 单边缺失就只当普通待机角色；**Add 不接受 aim/cover** |
| **爆裂** skillcut | 目录名以 ` Burst` 结尾 | 特写取景 | 同待机 | Battle 与 Lobby 的 skillcut 通常逐字节相同，**默认取 Lobby**；`idle` 可能只有半身，这是源资源设计而非导入错误 |
| **珍藏品** Favorite | 目录名以 ` Favorite` 结尾；源文件名 `favorite_cNNN_00` | 通常只有 `idle` 与 `expression_merged` | 同待机 | 本地编号固定 `9NNN`、皮肤固定 `00`；**不进 Battle**，不生成 aim/cover；默认常驻动画是精确 `idle_merged`，点击动画回退到 `expression_merged` |

### 1.0 目录职责与路径对照

看懂这张表就不会把文件放错地方：

| 位置 | 仓库里 | 发行包里 | 职责 |
| --- | --- | --- | --- |
| **运行时资源根** | `SpinePet\res\` | `res\` | 应用唯一扫描的目录，四类资源都落到这里 |
| **名字表** | `SpinePet\src\SpinePet\Data\CharacterNames.json` | 编译进 `app\SpinePet.dll` | 角色 ID → 显示名；**改完必须重新构建** |
| **源归档区** | `resources\Characters\` | —— | 只存在于开发机；源资源在这里留档，应用看不见 |
| **zip 归档** | `resources\zips\` | —— | 入库后的原始 zip 留档 |
| **上游证据镜像** | `resources\nikkedb\` | —— | 仅开发机（未入库，约 14 GB）：服装 ID 对照、索引、头像源 |
| **工具** | `SpinePet\tools\` | `tools\` | 下表所有脚本；包内位置在包根 |
| **可选导入 CLI** | `SpinePet\tools\battle-catalog-importer` | `tools\import\` | 自包含发布，用于射击 aim/cover |

关键区别：**`resources\` 是留档区，`res\` 才是运行时资源**。放错地方的表现是"点 Scan 什么也没有"。

### 1.1 待机（standing）

```text
res\<资源全名>\<皮肤编号>\standing\
  c<角色ID>_<皮肤ID>.skel
  c<角色ID>_<皮肤ID>.atlas
  <atlas 引用的全部纹理页>.png
  c<角色ID>_<皮肤ID>.attachments.exclude   （可选，仅清理过才有）
```

### 1.2 射击（aim / cover）

同一皮肤下 **Aim 与 Cover 都完整**时才生成 Battle；只有 standing，或只补齐了一边，
就仍按普通 Normal 角色使用。用指定清单工具审计通过后再写入：

```powershell
# 先只审计（不写任何文件）
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  --audit resources\Characters "<资源目录名>" "<另一个资源目录名>"

# 审计逐项确认后再导入
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  resources\Characters SpinePet\res "<资源目录名>" "<另一个资源目录名>"
```

资源目录清单是**必填**项，只填 `resources\Characters` 下的精确直属目录名。
审计只识别精确命名的 `Standing`/`Aim`/`Cover` 目录，像
`Aim (Chinese Censored Version)` 这种变体目录不会混进主资源。

### 1.3 爆裂（skillcut / Burst）

- 骨骼集选择顺序：**Standing > Lobby > Battle**；有 Standing 就选 Standing。
- Battle 与 Lobby 的 skillcut 通常相同，默认取 Lobby。
- skillcut 是特写取景，`idle` 可能只有半身，属正常现象。
- 目录名以 ` Burst` 结尾，例如 `Cinderella Crystal Wave Burst`。

### 1.4 珍藏品（Favorite）

- 源文件名形如 `favorite_cNNN_00`：从文件名取三位原编号 `NNN`。
- **本地编号 = `9NNN`**（原编号前加 `9`），皮肤固定 `00`，显示名为 `<Name> Favorite`。
  例：`favorite_c072_00` → Diesel → 本地 `9072` → `res\Diesel Favorite\00\standing\c9072_00.skel`。
- 查头像用**原编号**（`si_c072_00_s.png`），不能用 `9NNN` 反查——那个编号只属于本项目。
- 只有 `idle` 与 `expression_merged` 两个动画，属单状态互动资源。

## 2. 原始格式要改什么（"不方便"的具体来源）

| 要改的东西 | 规则 |
| --- | --- |
| **目录名** | 必须用**资源全名**（角色+皮肤/变体名），Burst 以 ` Burst` 结尾；**禁止只写角色名**；同一角色的不同变体各占一个目录 |
| **文件名前缀** | `c<角色ID>_<皮肤ID>`，程序按此前缀识别与去重 |
| **角色编号** | 每次导入**一律分配新 ID**：新 ID = `<源角色ID><皮肤号>` 拼接（`191_02` → `19102`、`260_80` → `26080`、`515_00` → `5150`）；只改角色数字段，**皮肤号保持源文件原值**（`c191_02_00.skel` → `c19102_02_00.skel`） |
| **atlas** | 首行引用的页面 png 名、`.attachments.exclude` 文件名同步改名 |
| **CharacterNames.json** | 为新 ID 加条目，显示名 = 资源全名（Burst 以 ` Burst` 结尾）；改完**必须重新构建**才生效（`dotnet build src\SpinePet\SpinePet.csproj -c Release`） |
| **禁令** | 禁止发明 `00cut` 之类皮肤代号；禁止两套骨骼共用同一前缀或同一显示名——**重名的模型打不开** |
| **图标** | 取索引方形图，不要用部件贴图，也不要用游戏竖版立绘（高 > 宽 × 1.25 说明取错了源） |

图标的查找位置与顺序（离线查询 nikkedb 证据库，应用运行时不访问它）：

```text
resources\nikkedb\github-repository\images\sprite\
  si_c<原角色ID>_<皮肤ID>_00_s.png  →  _s  →  _00  →  无后缀
```

命中后复制为 `<皮肤目录>\icons\<资源前缀>_icon.png`；皮肤没有专属头像时回退本体图标
`si_c<原角色ID>_00_s.png`。**找不到头像就不要先导本体**——报告 `MissingIcon` 而不是
事后无限期补图。

旧布局 `res\<角色>\standing\<自定义文件名>.skel` 仍可被扫描，但**不要新增**；
用 `SpinePet\tools\resource-layout\Migrate-CharacterResources.ps1`（先 `-WhatIf` 预览）迁移。

### 2.1 入库时的命名规范化（zip / 文件夹）

下载来的目录名与 zip 名一般是乱的，入库脚本会先规范化再归档：

**zip**（`Import-ResourceZip.ps1` 自动推导）：

1. 去掉 `PC _ Computer - Goddess of Victory_ Nikke - ` 前缀；
2. 剩余部分里的 `_ `（下划线+空格）替换为 ` - `；
3. **名字在前、Burst 放末尾**：以 `Burst - ` 开头时改为 `<名称> Burst`
   （`Burst - Helm_ Aquamarine` → `Helm - Aquamarine Burst`，实测见示例 4）；
4. 内层目录名 = 外层名去掉结尾 ` Burst`、再去掉首个稀有度前缀；
5. 解压前校验顶层只有一个文件夹；目标已存在则**报错不覆盖**，人工确认后再处理。

**文件夹**（手工归档，注意是**剪切**不是复制）：

```text
YYYY-MM-DD__名称 [cNNN_NN]   →   resources\Characters\名称
例：2025-12-30__Quency Escape Queen Variant 01 [c403_01]
    → resources\Characters\Quency Escape Queen Variant 01
```

### 2.2 只改该改的，其余原样

- `.skel` / `.atlas` / `.png` 一律**原样复制**，内容与行尾都不动（清理是独立步骤，见 §7）；
- 目标已存在同名文件时**不覆盖**，先人工确认；
- 同一套源资源**只入库一次**，后续所有调整改 `res\` 或名字表。

### 2.3 保留源角色 ID 的例外

"每次导入都分配新 ID"有一个例外：**当这套资源本身就是角色的首个模型**时，保留源角色 ID
（如 `Cinderella Crystal Wave` 保留 `515`、`Scarlet Overload` 保留 `472`）。
只要同一源角色已经存在任意本体或变体卡片，后续导入——**即使皮肤号是 `00`**——都必须分配新 ID。

### 2.4 `CharacterNames.json` 的硬约束

改名字表时踩这几条会被导入拒绝：

- 显示名 = **资源全名**，必须与 `res` 下的目录名一致；
- **不得含首尾空白**；
- 必须是**合法目录名**：不能出现 `\ / : * ? " < > |`；
- 不得与已有显示名重复；
- 数值键是角色 ID 字符串（如 `"19102": "Alice Variant 02"`）；
- 改完必须 `dotnet build src\SpinePet\SpinePet.csproj -c Release`，否则名字不生效。

### 2.5 导入后的默认值（规范 §4.4 / §4.5）

新导入的卡片按以下默认运行，不需要手工配置：

- **默认动画**：导入完成时默认状态是 `idle`（不预设 `action` 等）；
  启动与首次展示固定进入 Normal/standing 并播放 `idle`；骨骼里没有 `idle` 时用动画列表第一个；
  用户在面板里选过的动画优先于默认值。
- **缩放**：`ScaleBasePercent = 100`、`ScaleMultiplier = 1`、`Scale = 0.2`（即第一条拉满、第二条 1 倍）。
- **显隐**：`Visible = false`——新卡默认隐藏，导入或扫描完成后**不会**自动显示。

## 3. 项目提供的导入工具

| 工具 | 路径 | 作用 |
| --- | --- | --- |
| 应用内 **Add** | 配置面板 | 导入 standing 的 `.skel` 或 UnityFS bundle（文件名需以 `c<ID>_<皮肤ID>_<standing\|icons>_` 开头）；带事务、冲突不覆盖、失败回滚 |
| 应用内 **Scan / Folder** | 配置面板 | 重扫 `res\` 同步手工改动；打开当前资源目录（不存在会自动创建） |
| zip 一键入库 | `SpinePet\tools\zip-intake\Import-ResourceZip.ps1` | 解压 → 规范目录名 → 备份到 `resources\Characters\` → zip 归入 `resources\zips\` → 输出骨骼/atlas/缺号报告 |
| 射击审计与导入 | `SpinePet\tools\battle-catalog-importer` | 指定清单的 Standing/Aim/Cover 审计与成对导入（见 1.2） |
| 图标下载 | `SpinePet\tools\icons-downloader\Update-CharacterIcons.ps1` | 每次只接受**一个** ResourceId 和**一个**目标皮肤，不做全库扫描 |
| 图标/缩略图规范化 | 同上的 Add 流程 | Add 导入 standing 后会自动为该皮肤补图标；图标导入本身不创建卡片 |
| Atlas 清理与遮罩 | `SpinePet\tools\atlas-cleaner\Clean-Atlas.ps1` | 清理 atlas 中的背景/特效层与贴图遮罩（**按需**，默认不做） |
| 旧布局迁移 | `SpinePet\tools\resource-layout\Migrate-CharacterResources.ps1` | 把旧布局迁到规范布局，支持 `-WhatIf` |
| 骨骼附件检查 | `SpinePet\tools\skeleton-inspector` | 查看骨骼的附件/时间轴，用于排查点击区域与动画问题 |

UnityFS 导入依赖 Python 包：

```powershell
python -m pip install -r SpinePet\tools\requirements.txt
```


### 3.1 工具命令参考

命令在**仓库根目录**执行；发行包里把 `SpinePet\tools\` 换成 `tools\`。
所有 `.ps1` 都兼容 Windows 自带的 PowerShell 5.1。

**zip 一键入库**

```powershell
pwsh -NoProfile -File 'SpinePet\tools\zip-intake\Import-ResourceZip.ps1' -Zip '<zip 完整路径>'
# 可选：-CharactersDir <归档目录> -ZipsDir <zip 归档目录>
```
输出 `=== 骨骼集分析 ===` 逐条列出 `c<角色ID>_<皮肤ID>*.skel` 的位置与 `atlas:OK`，
末尾给出「已入库：…」与「zip 已移至：…」。

**射击 aim/cover 审计与导入**

```powershell
# ① 审计（不写任何文件）
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  --audit resources\Characters "<资源目录名>" "<另一个资源目录名>"
# ② 导入
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  resources\Characters SpinePet\res "<资源目录名>" "<另一个资源目录名>"
# 发行包内（自包含，目标机无需 .NET）
tools\import\BattleCatalogImporter.exe --audit resources\Characters "Anis Star"
```
参数顺序：**源目录 → 目标 `res` → 资源目录名清单（必填）**。
输出 JSON：审计有 `SourceDirectoryCount` / `CompleteSets`（含 `Identity`）/ `SkippedEntries`；
导入另有 `CompleteSetCount` / `ImportedBattleCount` / `AddedCharacterCount` / `AlreadyPresentCount`。

**图标下载（精确指定，没有全库模式）**

```powershell
pwsh -NoProfile -File 'SpinePet\tools\icons-downloader\Update-CharacterIcons.ps1' `
  -ResourceId 'c0170_00' -TargetSkinDirectory 'SpinePet\res\Anis Star\00'
# 其他参数：-ListOnly（只列候选不下载） -Force（覆盖已有图标）
#           -PythonCommand（默认 python） -ResourceDirectory
```
需要 Python（内部调 `extract_icon.py`）+ 网络。`Add` 导入 standing 后应用会**自动**做同一件事。

**atlas 清理与遮罩（按需）**

```powershell
pwsh -NoProfile -File 'SpinePet\tools\atlas-cleaner\Clean-Atlas.ps1' -Folder '<皮肤\standing 目录>' -WhatIf
# 去掉 -WhatIf 才真正写入；-CreateBackup 默认 $true
```

**旧布局迁移**

```powershell
pwsh -NoProfile -File 'SpinePet\tools\resource-layout\Migrate-CharacterResources.ps1' -WhatIf
# 可选：-ResourceDirectory <res 目录> -CharacterNamesPath <CharacterNames.json>
```

**骨骼附件检查**

```powershell
dotnet run --project SpinePet\tools\skeleton-inspector\SkeletonInspector.csproj -- `
  '<骨架.skel>' '<同名.atlas>'
```
输出动画名与附件/时间轴清单，用于核对点击区域与动画是否存在。

## 4. 推荐流程（规范化：指定清单 → 入库 → 预检 → 导入 → 验证）

1. **确定清单**：这次只处理哪几套资源，写下来。
2. **入库**：zip 用 `Import-ResourceZip.ps1`；文件夹按
   `YYYY-MM-DD__名称 [cNNN_NN]` → `resources\Characters\名称` **剪切**归档。
3. **预检**：骨骼完整性（`.skel`+`.atlas`+全部贴图）、Spine 版本、身份与目标 ID、
   图标来源——四件事都在写入 `res` **之前**做完。
4. **导入**：待机/爆裂/珍藏品按 §1 写入 `res\`；射击走 1.2 的审计→导入两步。
5. **验证**：见下一节；新卡片默认 `Visible = false`、缩放 100% / 1.0 倍。

**覆盖 `res` 里已有文件前先关闭 SpinePet**（Windows 文件锁会导致
`Device or resource busy`）。

### 4.1 导入完成后，应用应该写出什么

发行版实测：导入 `Anis Star` 与 `Alice Variant 02` 后启动应用，它扫描 `res` 写出的 `config.json` 形如：

```json
{
  "Version": "1.9",
  "Characters": [
    {
      "Name": "Anis Star",
      "SkelPath": "...\\res\\Anis Star\\00\\standing\\c0170_00.skel",
      "TexturePath": "...\\c0170_00.png",
      "ExtraTexturePaths": [],
      "Visible": false, "ScaleBasePercent": 100, "ScaleMultiplier": 1, "Scale": 0.2,
      "Battle": {
        "Aim":   { "SkelPath": "...\\00\\aim\\c0170_aim_00.skel" },
        "Cover": { "SkelPath": "...\\00\\cover\\c0170_cover_00.skel" },
        "Animations": {
          "AimIdle": "aim_idle", "ToAim": "to_aim",
          "AimFireLayers": [ { "Animation": "aim_fire", "Blend": "Replace", "Loop": true,
                               "ExcludeTimelines": [ "RotateTimeline@bone:gun_8" ] } ],
          "CoverIdle": "cover_idle", "ToCover": "to_cover", "ReloadSequence": [ "cover_reload" ]
        }
      }
    }
  ]
}
```

自查要点：

- 每套资源一个条目，`Name` 是你写进名字表的显示名；
- 路径指向 `res\`（**不是** `resources\`）；
- **多页 atlas 的第二页会进 `ExtraTexturePaths`**（例：`c19102_02_2.png`）；
- `Battle` 段是应用**自动生成**的，不需要手写；只有 aim/cover 成对存在时才会出现；
- `Visible=false`、`ScaleBasePercent=100`、`ScaleMultiplier=1`、`Scale=0.2` 符合规范；
- `Version` 等于应用当前的配置版本（当前 `1.9`）。

## 5. 验证与排查

先跑一遍 atlas 页齐全性检查（规范 §5）：

```powershell
$dir = 'SpinePet\res\<资源全名>\<皮肤编号>\standing'
Get-Content "$dir\<resource>.atlas" |
  Where-Object { $_.Trim() -match '\.(png|jpg|jpeg|webp)$' } |
  Select-Object -Unique | ForEach-Object {
    if (-not (Test-Path "$dir\$($_.Trim())")) {
      Write-Error "Missing atlas page: $($_.Trim())" } }
```

然后在应用里点 **Scan**，确认新卡出现、名字正确、头像正确、默认动画是 `idle`。

扫描不到的排查顺序：

1. 是否放在 `SpinePet\res\`（而不是 `resources\` 或索引目录）；
2. `.skel` 与 `.atlas` 是否**同名**；
3. atlas 声明的纹理页是否齐全；
4. 骨骼是否是 **Spine 4.1.x**；
5. 文件名是否含 `c<角色ID>_<皮肤ID>` 前缀；
6. 角色 ID 是否在 `CharacterNames.json`，改过是否**重新构建**；
7. 是否位于 `<资源全名>\<皮肤编号>\standing`；
8. 同一角色+皮肤前缀是否被两套骨骼占用（重名会被去重吞卡）。

**冲突与重复导入的语义**（应用内 Add 与导入 CLI 行为一致）：

- 目标已存在 → **拒绝**：即使源与目标内容相同、或你再次选中目标目录里的同一文件，也不算导入成功；
- 失败或取消 → 回滚本次创建的文件与空目录，并清理 `.SpinePet-Import-*` 临时目录；
  导入前就存在的文件保持不变；
- 同一套资源重复导入 → 报 `AlreadyPresent`，不会重复建卡；
- 骨骼落在**退役的旧状态目录**里 → 会被拒绝，按 §6.2 的思路迁移到规范布局。

## 6. AI 辅助导入（推荐）

导入的实质是"**读规范 → 改名改号 → 核对 → 跑命令 → 看报告**"的重复劳动，
出错点又都在细节上（编号拼接、atlas 页名、图标来源、重名），非常适合交给 AI，
你只负责确认判断项。

**把下面这些要求交给 AI：**

1. 先完整读
   `Y_MultipleAgentWorkflow/Resources/Load/SpinePet_Resources_Load_Guide.md`；
   珍藏品再读 `Favorite_Interactive_Import_Guide.md`。
2. 严格按 **指定清单 → 入库 → 本体与头像预检 → 导入登记 → 验证** 推进，
   **不得**扫描或导入整个资源库。
3. 每一步都给出**实际执行的命令与输出**作为证据，不要"应该导入成功"式结论。
4. 需要判断的地方（新角色 ID、显示名、图标来源）**先问你确认**，不允许凭名字猜 ID。
5. 完成后按 §5 跑 atlas 检查，并逐条报告：新卡名称 / ID / ID 是否已在
   `CharacterNames.json` / 默认动画 / 图标路径。

**不要让 AI 做的事：** 枚举未指定的同级目录；把 `res` 当试验场做清理
（清理放在 `resources\` 的暂存副本上）；覆盖文件前不关应用；用 `9NNN`
编号去 nikkedb 反查。

## 7. 已知坑

- **重名即失效**：两套骨骼共用同一 `c<ID>_<皮肤ID>` 前缀或同一显示名时，
  被去重吞卡，模型打不开。
- **皮肤号别乱改**：新 ID 只改角色数字段，皮肤号必须是源文件原值。
- **Spine 版本**：必须是 4.1.x。已知个例：某套资源战斗骨骼为 `4.0.47`，
  与当前 4.1 运行时不兼容，只能跳过。
- **改 `CharacterNames.json` 后忘记重新构建**：名称不生效。
- **文件锁**：应用运行中覆盖 `res` 文件会失败，先退出应用。
- **珍藏品不进 Battle**：`Favorite` 资源只有单状态，不要为它建 aim/cover。
- **清理是可选动作**：默认不清理；要做也只在暂存副本上做，
  `.attachments.exclude` 优先于贴图遮罩，`*_eyebg`（眼白）永远不要删。
- **图标别取错源**：卡片头像是超高竖图（高 > 宽 × 1.25，如 488×953）说明误用了游戏竖版立绘
  （`resources\Characters\<资源名>\Icons\c*_NN.png`）。正确来源是索引方形图
  `si_c<原角色ID>_<皮肤ID>_00_s.png`（约 128×128，查找顺序 `_00_s` → `_s` → `_00` → 无后缀）。
- **上游头像镜像不在仓库里**：`resources\nikkedb\` 约 14 GB，未入库，只有开发机有。
  普通使用者请走**应用内自动下载**或 `icons-downloader`（需要 Python + 网络）；
  拿不到图标时应用会回退用立绘贴图当缩略图，功能不受影响。
- **旧布局卡片的图标约定不同**：图标放在骨骼同目录，命名 `<骨骼主文件名>_icon.png`
  （如 `Blanc_WhiteRabbit_icon.png`），不是 `icons\` 子目录。
- **珍藏品点击动画链**：按 `action → click → touch → tap → reaction → interact → skillcut`
  取第一个存在的；都没有才回退 `expression_merged`；点击结束恢复当前常驻动画。
- **名字表改错会静默不生效**：`CharacterNames.json` 改后不重新构建，卡片的显示名不会变。

## 8. 运行环境要求（裸机）

应用本体是**自包含发布**：目标机**不需要**安装 .NET，也**不需要** VC++ 运行库
（包内自带 `hostfxr.dll`、`coreclr.dll`、`PresentationFramework.dll`、
`vcruntime140_cor3.dll`、`D3DCompiler_47_cor3.dll`），系统要求 Windows 10 或更高。

但并非所有能力都零依赖，按你要用的功能对号入座：

| 能力 | 裸机可跑？ | 需要什么 |
| --- | --- | --- |
| 启动桌宠、显示与交互、手动放文件 + `Scan` | ✅ | 无（Windows 10 及以上） |
| `Add` 导入 `.skel`（骨架 + atlas + 贴图） | ✅ | 无，纯 C# 路径 |
| `Add` 导入 **UnityFS bundle** | ❌ | Python 3 + `pip install -r tools\requirements.txt`（UnityPy、Pillow） |
| 自动 / 手动**图标下载** | ❌ | 同上；脚本本身走 Windows 自带 `powershell.exe`，但解包用 Python |
| **射击 aim/cover 导入** | ⚠️ | 需要 `BattleCatalogImporter`：装 .NET SDK 自行编译，或使用带 `-IncludeImportTools` 的发行包（内含自包含 exe，无需 .NET） |
| zip 入库 / atlas 清理 / 旧布局迁移 | ✅ | 无；这些 `.ps1` 已兼容 **Windows 自带的 PowerShell 5.1**（含中文输出，UTF-8 BOM 已确保不乱码） |
| 从源码构建 / 打包发布 | ❌ | .NET 9 SDK（打包脚本还需 `pwsh` 7，见 `tools\Publish.bat`） |

上机先跑一次自检，它会逐项报告缺什么、以及缺了会损失哪个功能：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Check-Environment.ps1
```

脚本在发行包根目录，输出 `[OK] / [--] / [!!]` 三态并在末尾给结论；它自身只依赖
Windows 自带的 PowerShell，不需要预先安装任何东西。

补依赖：

```powershell
# ① UnityFS bundle 导入 / 图标下载
python -m pip install -r tools\requirements.txt

# ② 射击 aim/cover 导入：在开发机上重新打包并附带自包含 CLI（目标机无需 .NET）
pwsh -NoProfile -File tools\Package-Release.ps1 -IncludeImportTools
```

**完全不装 Python 也可用**：把 `.skel` + 同名 `.atlas` + 全部贴图页按布局放进 `res\`，
或用 `Add` 直接导入 `.skel`，再点 `Scan`——这两条路径不碰 Python。