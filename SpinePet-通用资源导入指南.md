# SpinePet 资源导入规范

本文是流程化规范：资源从拿到手到出现在 SpinePet 里，按
**入库 → 判定 → 导入 → 验证** 四个阶段推进，每个阶段写明执行顺序和
硬性要求。

**本文所有相对路径均以 `D:\SpineTools` 为根**，例如
`resources\Characters\` 指 `D:\SpineTools\resources\Characters\`。
背景清理等进阶操作见 `Spine资源匹配与清理通用指南.md`。

## 0. 流程总览

```text
zip 压缩包 ──┐
            ├─→ 阶段一 入库 ──→ 阶段二 判定 ──→ 阶段三 导入 ──→ 阶段四 验证
文件夹/桌面 ─┘   resources\       选骨骼集/       SpinePet\res\     Scan
                Characters\      分新ID/定名      <名>\<皮肤>\
                                  standing\
```

核心原则（全程适用）：

1. 身份以**文件名前缀** `c<角色ID>_<皮肤ID>` 为准，不查外部编号库。
2. 所有源资源先进入 `resources\Characters\`，`SpinePet\res\` 只放
   已确认可运行的最终资源；两者不混用。
3. 导入 `res` 的文件**原样复制**：不改行尾、不加清理规则，
   除非明确要求清理。
4. 源资源只入库一次；后续调整都改 `res` 或 `CharacterNames.json`，
   不回写源。

## 1. 目录职责

| 相对路径 | 职责 |
|---|---|
| `resources\Characters\` | 所有源资源的唯一入口与备份区 |
| `resources\zips\` | 入库后的 zip 存放地 |
| `resources\nikkedb\` | 上游证据库，只读；子目录：`data\`（indexes / metadata / evidence / archive 及核验报告）、`l2d`、`github-repository`、`Preview` |
| `resources\` 其他子目录 | 清理、试验的暂存工作区 |
| `SpinePet\res\` | 应用唯一扫描的运行时资源根 |
| `SpinePet\src\SpinePet\Data\CharacterNames.json` | 角色 ID → 显示名映射（嵌入资源） |
| `SpinePet\tools\zip-intake\` | zip 一键入库脚本 |
| `SpinePet\tools\skeleton-inspector\` | 骨骼附件检查器 |
| `SpinePet\tools\atlas-cleaner\` | Atlas 清理与贴图遮罩 |
| `SpinePet\tools\resource-layout\` | 旧布局迁移脚本 |
| `SpinePet\tools\icons-downloader\` | 图标下载脚本（应用运行时调用） |
| `tools\`（仓库根） | nikkedb 维护工具（contact sheet 生成、资源提取、骨骼转换） |

`SpinePet\res\` 是应用唯一资源根；不要让应用扫描 `resources\`。
`resources\nikkedb\l2d` 目录名的 `YYYY-MM-DD__` 前缀只是排序日期，
不得带入 `res`。

`github-repository` 已通过 `git sparse-checkout` 排除 `l2d/`
（与 `nikkedb\l2d` 内容重复，节省约 5 GB 工作树空间）。
以后直接 `git pull` 更新即可，不会重新拉出该目录；
如需恢复：`git sparse-checkout disable`。
图标源 `images\sprite\` 不受影响。

## 2. 阶段一：入库

**要求：任何来源的资源，先全部进入 `resources\Characters\`，再谈导入。**

### 2.1 zip 入口（脚本自动）

```powershell
pwsh -NoProfile -File 'SpinePet\tools\zip-intake\Import-ResourceZip.ps1' `
  -Zip '<zip 完整路径>'
```

脚本顺序执行四步，任一步失败即中止并保留现场：

1. 解压到临时目录；顶层必须是唯一文件夹。
2. 备份到 `resources\Characters\<外层名>\<内层名>\`。
3. zip 剪切到 `resources\zips\`。
4. 输出分析报告：每个 `c<角色ID>_<皮肤ID>*.skel` 的位置、atlas
   齐全性、`CharacterNames.json` 缺失的角色 ID。

zip 命名规范（脚本自动推导）：

- 去掉 `PC _ Computer - Goddess of Victory_ Nikke - ` 前缀；
- 剩余部分中 `_ `（下划线+空格）替换为 ` - `；
- **名字在前，Burst 放末尾**：以 `Burst - ` 开头时改为
  `<名称> Burst`，如 `Burst - Helm_ Aquamarine` → `Helm - Aquamarine Burst`；
- 内层名 = 外层名去掉结尾 ` Burst`、再去掉首个稀有度前缀，
  如 `Helm - Aquamarine`；
- 目标已存在时报错不覆盖，人工确认后处理。

### 2.2 文件夹入口（剪切，不是复制）

直接拿到的文件夹（如桌面上的日期目录）**剪切**进
`resources\Characters\`，命名规范：

```text
YYYY-MM-DD__名称 [cNNN_NN]  →  resources\Characters\名称
```

去掉 `YYYY-MM-DD__` 日期前缀和 `[cNNN_NN]` 后缀，保留中间名称。
例：`2025-12-30__Quency Escape Queen Variant 01 [c403_01]` →
`resources\Characters\Quency Escape Queen Variant 01`。

### 2.3 资源完整性（入库后立即检查）

每套可导入骨骼必须满足：

```text
<resource>.skel + 同名 .atlas + atlas 引用的全部 .png
```

- 同一次 Spine 4.1.x 导出，置于同一目录；
- aim、cover 不可导入，忽略；
- 不满足时资源停留在 `resources\`，不得进入 `res\`。

## 3. 阶段二：判定

在入库资源上依次确定三件事：

### 3.1 选骨骼集（顺序要求）

```text
Standing  >  Lobby  >  Battle
```

- 有 Standing 必选 Standing；
- Battle 与 Lobby 的 skillcut 通常逐字节相同，默认选 Lobby；
- skillcut 是特写取景，idle 可能只有半身——这是源资源设计，
  不是导入错误。

### 3.2 分配独立角色 ID（每次导入必做）

**每次导入一律分配新的角色 ID，不根据名字判断是否冲突。**
Variant（变体）与 Burst/cutscene 同等对待，都是独立模型。

规则：

1. 新 ID = `<源角色ID><皮肤号>` 拼接，如 `191_02` → `19102`、
   `260_80` → `26080`、`515_00` → `5150`；
2. 只改文件名前缀中的角色数字段，皮肤号保持源文件原值
   （`c191_02_00.skel` → `c19102_02_00.skel`）；
3. atlas 首行的页面 png 引用、`.attachments.exclude` 文件名同步改名；
4. `CharacterNames.json` 为新 ID 添加条目，显示名 = 资源全名
   （与 `resources\Characters\` 入库名一致，Burst 资源以 ` Burst` 结尾）；
5. **禁止**发明 `00cut` 之类皮肤代号；扫描器按 `c<角色ID>_<皮肤ID>`
   前缀去重，也**禁止**两个卡片共用同一前缀或同一显示名——
   **重名的模型无法打开**。

只有资源本身就是角色本体的首个模型时才保留源角色 ID
（如 `Cinderella Crystal Wave` 保留 `515`、`Scarlet Overload` 保留 `472`）。
基础角色的原始 ID 条目（`CharacterNames.json` 中的 `191`、`260` 等）
保留不动，供本体使用。

正确示例：

```text
res\Modernia Variant 80\80\standing\c26080_80_00.skel
res\Blanc Variant 03\03\standing\c27003_03_00.skel
res\Helm Aquamarine\00\standing\c353_00_skillcut.skel
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.skel
```

## 4. 阶段三：导入

**目录命名要求：`res` 下的角色目录必须用资源全名（角色+变体/皮肤名），
与 `resources\Characters\` 的入库名一致（Burst 资源以 ` Burst` 结尾），
禁止只写角色名。** 同一角色的不同变体各占一个目录；只有资源本身
就是角色本体时才允许目录名与角色名相同。

目标布局（唯一规范布局）：

```text
SpinePet\res\<资源全名>\<皮肤ID>\
  standing\
    c<角色ID>_<皮肤ID>.skel
    c<角色ID>_<皮肤ID>.atlas
    <atlas 引用的全部纹理页>.png
    c<角色ID>_<皮肤ID>.attachments.exclude   （可选，清理时才有）
  icons\
    c<角色ID>_<皮肤ID>_icon.png              （可选）
```

执行顺序与要求：

1. 创建 `SpinePet\res\<资源全名>\<皮肤ID>\standing\`。
2. 原样复制 `.skel`、`.atlas`、全部纹理页；MD5 与源一致；
   不覆盖目标中已存在的同名文件。
3. 改过 `CharacterNames.json` 时必须重新构建才生效：
   ```powershell
   Set-Location 'SpinePet'
   dotnet build src\SpinePet\SpinePet.csproj -c Release
   ```
   `global.json` 已放宽为 `rollForward: Major`，本机 SDK 10 可直接构建。
4. 覆盖 `res` 内已有文件前，先关闭 SpinePet（Windows 文件锁会导致
   Device or resource busy）。
5. 按下节配置图标；图标导入本身不创建角色卡。

角色列表按名字字母序自动排序，无需手动排。

### 4.1 图标（icon）配置

头像从本地 nikkedb 索引取，不要用部件贴图（纹理页 png）充当图标：

1. 图标源目录：
   `resources\nikkedb\github-repository\images\sprite\`，
   按 `si_c<角色ID>_<皮肤ID>_00_s.png` 查找；
2. 查找顺序：`si_c<角色ID>_<皮肤ID>_00_s.png` →
   `si_c<角色ID>_<皮肤ID>_s.png` → `..._00.png` → 无后缀；
3. 新 ID 卡片（`c<原ID><皮肤号>_<皮肤号>`）反推源 ID：
   新角色数字段去掉结尾的皮肤号数字即原 ID，
   如 `c27003` + 皮肤 `03` → 原 `c270`；
4. 命中后复制为 `<皮肤目录>\icons\<资源前缀>_icon.png`，
   资源前缀 = 骨骼文件名前两段（如 `c27003_03`）；
5. 皮肤无专属头像时，回退本体图标 `si_c<原角色ID>_00_s.png`；
6. `icons` 下已存在图标则跳过，不覆盖；
7. 旧布局卡（`res\<角色>\standing\`，自定义文件名）的图标约定不同：
   放在骨骼同目录，命名为 `<骨骼主文件名>_icon.png`
   （如 `Blanc_WhiteRabbit_icon.png`）。身份用
   `resources\nikkedb\data\indexes\resource-date-index.json` 按
   displayName / targetName 反查角色 ID 再找 `si_` 头像；
   没有这张图时应用会退回显示部件贴图（应避免）；
8. 索引里只有泛化名（`Variant 01/02`）时，服装名到 ID 的对应关系查
   `resources\nikkedb\NIKKE服装ID对照表.md`（可持续维护，核验一条加一行；
   如 Noise - Classic Diva = `c430_02`、Shell Princess = `c513_03`、
   Abyss Flower = `c513_01`），不要默认用本体头像；
   核验过程证据在 `NIKKE资源核验报告.md`（已冻结的历史存档）。

**超高缩略图检查（头像过窄过高时的修正）：**

- 卡片头像一旦是超高竖图（高 > 宽 × 1.25，如 488×953、752×1762），
  说明图标取错了源——通常是误从角色自身文件夹
  `resources\Characters\<资源名>\Icons\c*_NN.png`（游戏内竖版立绘）
  复制，而不是 nikkedb 索引图。
- 正确方形图**只在项目资源目录取**：
  `resources\nikkedb\github-repository\images\sprite\` 下按
  `si_c<原角色ID>_<皮肤ID>_00_s.png`（约 128×128 方形）查找
  （顺序 `_00_s` → `_s` → `_00` → 无后缀）；
  **不要从 `resources\Characters\<资源名>\Icons\` 里找。**
- 覆盖前先 `taskkill //IM SpinePet.exe //F`，再复制为
  `<皮肤目录>\icons\<资源前缀>_icon.png` 覆盖错误图标；
  原错误图标可先备份。

### 4.2 默认动画规则

应用层默认动画逻辑（导入后无需配置即按此运行）：

1. **每次导入完成时，卡片默认状态必须是 `idle`**——不预设 `action`
   等其他动画作为初值；
2. 桌面待机默认播放 `idle`；
3. 骨骼中没有 `idle` 时，使用动画列表的第一个动画；
4. 用户在配置模式选择的动画优先于默认值（配置保留到桌面模式）。

## 5. 阶段四：验证

1. 启动 SpinePet，配置面板点 **Scan**。
2. 确认新卡出现、名字正确（字母序位置自动排入）、头像正确。
3. 检查待机动画（idle）、点击动画、结束回归待机、拖动边界。
4. atlas 页齐全性检查：
   ```powershell
   $dir = 'SpinePet\res\<资源全名>\<皮肤ID>\standing'
   Get-Content "$dir\<resource>.atlas" |
     Where-Object { $_.Trim() -match '\.(png|jpg|jpeg|webp)$' } |
     Select-Object -Unique | ForEach-Object {
       if (-not (Test-Path "$dir\$($_.Trim())")) {
         Write-Error "Missing atlas page: $($_.Trim())" } }
   ```

扫描不到时的排查顺序：

1. 是否放在 `SpinePet\res\`（而非源资源或索引目录）；
2. `.skel` 与 `.atlas` 是否同名；
3. atlas 声明的纹理页是否齐全；
4. 骨骼版本是否 Spine 4.1.x；
5. 文件名是否含 `c<角色ID>_<皮肤ID>` 前缀；
6. 角色 ID 是否在 `CharacterNames.json`（改过则是否重新构建）；
7. 是否位于 `<资源全名>\<皮肤ID>\standing`；
8. 同一角色+皮肤前缀是否被两套骨骼占用（去重吞卡，见 3.2）。

## 6. 附则

### 6.1 背景清理（按需，默认不做）

只在使用方明确要求时清理，完整方法见
`Spine资源匹配与清理通用指南.md`。要点：

- 在 `resources\` 的暂存副本上做，不在 `Characters` 源或 `res` 上
  直接试验；
- 附件层排除（`.attachments.exclude`）优先，贴图遮罩有损坏风险；
- `*_eyebg` 是眼白，永远不删；
- 清理规则只作用于对应资源自身，不跨资源复用。

### 6.2 旧布局迁移

`SpinePet\res\<角色>\standing\` 旧布局仍可扫描，但不要新增。规范名
资源用 `SpinePet\tools\resource-layout\Migrate-CharacterResources.ps1`
迁移（先 `-WhatIf` 预览）；自定义文件名资源（如 `Burst_LM.skel`）须先
按阶段二确认身份并规范化。身份无法确认的资源保留暂存区，不猜 ID。

### 6.3 UnityFS 导入

应用 **Add** 支持 `.skel` 或 UnityFS bundle；bundle 文件名以
`c<角色ID>_<皮肤ID>_<standing|icons>_` 开头。依赖：
`python -m pip install -r SpinePet\tools\requirements.txt`。

### 6.4 aim / cover

不可导入、不可渲染；限制说明见 `SpinePet\AIM_COVER_SUPPORT_NOTES.md`。
