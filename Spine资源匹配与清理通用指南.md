# Spine 资源匹配与清理通用指南

本文总结一套适用于 Spine 资源导入、非角色元素清理、动画选择和点击
问题排查的通用方法。文中的执行路径已按当前 `D:\SpineTools` 目录结构
更新，所有暂存和输出都留在 D 盘。

## 0. 当前目录边界

```text
D:\SpineTools\
  nikkedb\
    l2d\                 按首次提交日期整理的源资源副本
      mapped\            已确认名称
      unmapped\          尚未唯一确认名称
    indexes\             当前名称、日期和未确认项索引
    metadata\            上游原始元数据快照
    evidence\            联系表、来源和核验结果
    archive\             回滚资料，不作为当前输入
    github-repository\   上游完整 Git 仓库
  resources\             清理、试验和人工核对的暂存区
  SpinePet\
    res\                 应用实际扫描的运行时资源
    tools\               可复用的检查、清理和迁移工具
```

这些目录不能互相替代：

- `nikkedb\l2d` 是按日期组织的源资源库，不直接作为 SpinePet 资源根。
- `nikkedb\indexes` 是当前索引入口，`archive` 中的旧结果不能代替它。
- `resources` 用于复制后分析和试验，允许产生中间结果。
- `SpinePet\res` 只保存已经确认且可运行的最终资源。
- `github-repository` 用于核对上游历史和准备 PR，不是清理缓存目录。

`nikkedb\l2d` 中的 `YYYY-MM-DD__` 目录前缀只表示首次 Git 加入日期。
它不能单独证明角色身份，导入 `SpinePet\res` 时也不应保留为角色目录名。

当前应用仍兼容 `SpinePet\res\<角色>\standing` 旧布局，但如果骨骼文件名
没有 `c<角色ID>_<皮肤ID>` 前缀，扫描器只能用文件夹名作为显示名，角色
ID 和皮肤 ID 会留空。这类资源可能可以渲染，却不适合作为长期规范结构。

## 1. 基本原则

Spine 资源通常包含三层信息：

1. Skeleton：骨骼、Slot、Skin、Attachment 和动画。
2. Atlas：附件名称到贴图区域的映射。
3. Texture：实际像素。

清理非角色元素时，优先在 Attachment 层移除对象，再按需处理 Texture。只擦除贴图像素可能留下仍在运行的网格、错误的角色边界和异常点击区域。

建议处理顺序：

1. 通过 `nikkedb\indexes`、提交日期和资源内容确认源资源身份。
2. 从 `nikkedb\l2d` 复制到 `D:\SpineTools\resources`，保留源资源不动。
3. 导出骨骼附件清单。
4. 根据名称、类型和用途制定排除规则。
5. 加载 SkeletonData 后移除命中的附件。
6. 再创建 Skeleton 和计算动画边界。
7. 必要时对 Atlas 贴图进行辅助透明化。
8. 验证后复制到 `SpinePet\res\<角色>\<皮肤>\standing`。
9. 在 SpinePet 中执行 **Scan**，验证待机、点击、边界和命中区域。

## 2. 附件排除文件

可为每个骨骼资源提供一个同目录排除文件：

```text
<骨骼文件名，不含扩展名>.attachments.exclude
```

示例：

```text
character.skel
character.atlas
character.png
character.attachments.exclude
```

推荐规则语法：

```text
# 注释和空行会被忽略
prefix:bg_
prefix:background_
prefix:water_
prefix:scene_
prefix:effect_
prefix:fx_
name:shared_effect_texture
```

规则含义：

- `prefix:`：不区分大小写的前缀匹配。
- `name:`：不区分大小写的完整名称匹配。
- 规则和值两侧的空白应被去除。
- 未知规则或空值应明确报错，避免静默漏删。

不要直接把所有包含 `light`、`hair`、`cloth` 或 `water` 的名称都删除。这些词也可能出现在角色高光、头发、衣服或身体遮罩中。优先使用稳定前缀，无法确定时先检查附件实际用途。

## 3. 匹配字段

不要只匹配 Atlas region。一个附件可能通过不同字段引用同一资源。

推荐同时检查：

1. Skin placeholder 名称。
2. Attachment 名称。
3. RegionAttachment 或 MeshAttachment 的 Path。
4. Atlas region 名称。
5. Slot 名称。

任意字段命中规则即可将附件标记为排除。

通用伪代码：

```csharp
bool ShouldExclude(AttachmentInfo attachment, Rule rule)
{
    string?[] identifiers =
    [
        attachment.PlaceholderName,
        attachment.AttachmentName,
        attachment.Path,
        attachment.AtlasRegionName,
        attachment.SlotName
    ];

    return identifiers.Any(rule.Matches);
}
```

匹配规则：

```csharp
bool Matches(string candidate)
{
    return IsPrefix
        ? candidate.StartsWith(Value, StringComparison.OrdinalIgnoreCase)
        : candidate.Equals(Value, StringComparison.OrdinalIgnoreCase);
}
```

## 4. 正确的移除时机

附件应在加载 `SkeletonData` 后、创建 `Skeleton` 前移除：

```text
加载 Atlas
  -> 加载 SkeletonData
  -> 读取排除规则
  -> 从所有 Skin 移除附件
  -> 创建 Skeleton
  -> 计算 Setup Bounds 和 Animation Envelope
  -> 开始渲染
```

通用处理：

```csharp
foreach (Skin skin in skeletonData.Skins)
{
    SkinEntry[] excluded = skin.Attachments
        .Where(entry => ShouldExclude(entry, rules))
        .ToArray();

    foreach (SkinEntry entry in excluded)
        skin.RemoveAttachment(entry.SlotIndex, entry.Name);
}
```

先复制成数组再删除，避免遍历集合时修改集合。

在边界计算前移除附件可以同时解决：

- 背景或特效仍然被绘制。
- 透明附件继续撑大角色边界。
- 角色被错误地限制在屏幕边缘。
- 点击空白区域也会命中角色。
- 拖动锚点与视觉角色不一致。

## 5. Atlas 贴图透明化

贴图透明化适合减小视觉残留，但不应替代附件移除。

当前工具的命令形式：

```powershell
$tool = 'D:\SpineTools\SpinePet\tools\atlas-cleaner\Mask-AtlasTexture.py'
$work = 'D:\SpineTools\resources\<资源目录>\standing'

python $tool `
  --atlas (Join-Path $work 'character.atlas') `
  --texture (Join-Path $work 'character.png') `
  --output (Join-Path $work 'character-only.png') `
  --prefix bg_ `
  --prefix water_ `
  --prefix fx_ `
  --name shared_effect_texture
```

`Mask-AtlasTexture.py` 不覆盖输入纹理，并会创建输出目录。输出文件应使用
新名称，目视和运行验证后再决定是否替换运行时纹理。

贴图处理程序应：

1. 正确识别 Atlas page。
2. 只处理属于目标 Texture 的 region。
3. 支持多个前缀和完整名称。
4. 不区分大小写。
5. 正确处理 `rotate:90` 和 `rotate:270`。
6. 输出命中 region 数量和透明化像素数量。
7. 保留原图备份或写入新文件。

### 图集重叠风险

不同 Atlas region 可能共享或重叠像素。

如果直接擦除命中的矩形：

- 背景可以被彻底清除。
- 与背景重叠的角色贴图也可能损坏。

如果优先保护保留区域：

- 角色贴图不会被误删。
- 重叠部分可能仍包含背景像素。

因此最稳妥的组合是：

1. Attachment 层负责禁止非角色对象参与渲染。
2. Texture 层只负责清理不与角色区域冲突的像素。

## 6. 骨骼附件检查

检查器应输出以下字段：

```text
skin
slot
placeholder
type
attachment
path
region
```

推荐检查流程：

1. 使用检查器导出全部附件为 TSV：

```powershell
$project = 'D:\SpineTools\SpinePet\tools\skeleton-inspector\SkeletonInspector.csproj'
$work = 'D:\SpineTools\resources\<资源目录>\standing'

dotnet run --project $project -- `
  (Join-Path $work 'character.skel') `
  (Join-Path $work 'character.atlas') |
  Set-Content -LiteralPath (Join-Path $work 'character-attachments.tsv') `
    -Encoding utf8
```

检查器输出骨骼版本以及 `skin`、`slot`、`placeholder`、`type`、
`attachment`、`path`、`region` 字段。

2. 按 `slot`、`path`、`region` 搜索背景、前景、粒子和场景命名。
3. 统计每条规则的命中数量。
4. 单独检查名称可疑但未命中的附件。
5. 确认角色身体、头发、服装、高光和遮罩没有被误选。
6. 将稳定规则写入资源旁的排除文件。

可疑关键词只能用于初筛，不能直接作为删除条件：

```text
bg
background
scene
water
wave
jelly
particle
effect
fx
glow
light
foreground
```

## 7. 点击动画匹配

不同资源对点击动画的命名不统一，不应只查找 `action`。

可配置一组通用优先级：

```text
action
click
touch
tap
reaction
interact
skillcut
```

对每个候选词按以下顺序匹配：

1. 完整名称，例如 `action`。
2. 下划线前缀，例如 `action_1`。
3. 当前词未命中时检查下一个候选词。

通用实现：

```csharp
foreach (string preferredName in preferredNames)
{
    string? exact = animationNames.FirstOrDefault(name =>
        name.Equals(
            preferredName,
            StringComparison.OrdinalIgnoreCase));
    if (exact != null)
        return exact;

    string? prefixed = animationNames.FirstOrDefault(name =>
        name.StartsWith(
            preferredName + "_",
            StringComparison.OrdinalIgnoreCase));
    if (prefixed != null)
        return prefixed;
}
```

点击动画应以非循环方式播放：

```csharp
animationState.SetAnimation(0, clickAnimation, false);
```

## 8. 待机动画回退

点击动画结束后需要显式恢复循环待机，否则角色可能停在最后一帧。

推荐待机匹配顺序：

1. 完整名称 `idle`。
2. 以 `idle` 开头的第一个动画。
3. 动画列表中的第一个动画。

```csharp
string? idle = animationNames.FirstOrDefault(name =>
    name.Equals("idle", StringComparison.OrdinalIgnoreCase));

idle ??= animationNames.FirstOrDefault(name =>
    name.StartsWith("idle", StringComparison.OrdinalIgnoreCase));

idle ??= animationNames.FirstOrDefault();
```

将待机动画加入队列：

```csharp
animationState.AddAnimation(0, idle, true, 0);
```

如果点击动画和待机动画相同，则不需要重复排队。

## 9. 点击命中验证

只使用矩形包围范围进行点击判断会命中大量透明区域。推荐两阶段命中：

1. 先检查当前帧的屏幕包围矩形。
2. 再检查点击点是否位于实际渲染三角形内。

附件排除必须发生在几何构建前。否则透明背景附件虽然看不见，仍可能产生网格和命中区域。

至少验证：

- 点击角色身体会触发动画。
- 点击角色周围透明区域不会触发。
- 动画结束后恢复待机。
- 拖动和点击不会互相误判。
- 清理背景后角色位置不会被异常夹紧。

## 10. 测试建议

### 规则解析

- 注释和空行被忽略。
- `prefix:` 正确执行前缀匹配。
- `name:` 只执行完整匹配。
- 匹配不区分大小写。
- 空规则和未知规则明确失败。

### 附件移除

- 命中附件全部从 Skin 移除。
- 未命中附件保持不变。
- 实际移除数量符合预期。
- 清理后仍能生成可渲染几何。
- Setup Bounds 和 Animation Envelope 不再包含场景尺寸。

### 动画回退

- 完整 `action` 优先于其他点击动画。
- 没有 `action` 时可选择 `click_1`、`reaction_1` 或 `skillcut_1`。
- 点击动画结束后恢复 `idle`。
- 只有 `idle2` 时能够正常回退。

## 11. 常见失败原因

### 仍有背景或水母残留

- 只擦了 Texture，没有移除 Attachment。
- Atlas region 与角色区域重叠，保护逻辑恢复了部分像素。
- 附件的 region 名未命中，但 slot、path 或 placeholder 包含背景命名。
- 某些效果使用共享 region，需要完整名称规则。

### 角色贴图被擦坏

- 背景和角色 region 共用像素。
- 没有处理旋转 region。
- 使用了范围过宽的关键词规则。
- 直接修改原图且没有备份。

### 点击没有动画

- 代码只查找固定名称 `action`。
- 点击动画使用其他命名或带数字后缀。
- 点击区域仍由场景附件撑大，实际角色几何没有命中。

### 点击后停住

- 点击动画以非循环方式播放，但没有排队恢复待机。
- 待机名称不是完整的 `idle`，且没有前缀回退。

## 12. Atlas 结构清理

`Clean-Atlas.ps1` 会删除未在同名 `.skel` 字节内容中出现的 Atlas region。
它默认在原 `.atlas` 旁创建一次 `.bak`，因此先在暂存区预览：

```powershell
$tool = 'D:\SpineTools\SpinePet\tools\atlas-cleaner\Clean-Atlas.ps1'
$work = 'D:\SpineTools\resources\<资源目录>\standing'

& $tool -Folder $work -WhatIf
```

确认输出中的待删除 region 都是无用项后再执行：

```powershell
& $tool -Folder $work
```

这个工具当前有三个前置条件：

- `.skel` 与 `.atlas` 位于同一目录且主文件名相同；
- 同目录至少存在一个以骨骼主文件名开头的非图标 PNG；
- Atlas region 名可以在骨骼二进制的 UTF-8 字符串中核对。

例如 `c017_01_00.skel`、`c017_01_00.atlas` 配
`c017_01.png` 虽然可以被 SpinePet 正常加载，但不满足清理器的 PNG
前缀检查，预览会输出：

```text
SKIP missing files
SKIP: no complete resource sets
```

这表示工具没有执行清理，不表示资源已经干净。不要为了绕过检查直接改
纹理名；改纹理名时还必须同步更新 `.atlas` 页面引用并重新验证。

这个工具不会判断角色语义，也不能替代附件清单核对。对不满足前置条件、
来源不明、共享区域较多或无法运行验证的资源，保留 Atlas 原结构并使用
`.attachments.exclude` 更稳妥。

## 13. 旧布局迁移限制

`Migrate-CharacterResources.ps1` 只迁移以
`c<数字角色ID>_<皮肤ID>` 开头的完整资源集。先预览：

```powershell
Set-Location 'D:\SpineTools\SpinePet'
.\tools\resource-layout\Migrate-CharacterResources.ps1 -WhatIf
```

`Liberalio_Base.skel`、`Burst_LM.skel` 这类自定义名会显示
`Unrecognized or incomplete file left in place`，不会被迁移。处理这类
资源时，应先复制到 `D:\SpineTools\resources`，核实角色和皮肤身份，再
把同一组 `.skel`、`.atlas` 和 `.attachments.exclude` 统一为规范主文件
名。Atlas 页面可以保留原名，只要 `.atlas` 引用仍然正确。

身份不能闭环确认时，不要通过猜测文件名前缀强行迁移。

## 14. 清理边界

可以清理的内容：

- `D:\SpineTools\resources` 中已确认可再生成的 TSV、预览图和失败输出；
- Atlas 工具产生且已经完成对照的 `.bak`；
- 未被 `.atlas` 引用、也不属于图标或核验证据的重复暂存纹理；
- 空的暂存工作目录。

不能按“看起来杂乱”直接删除的内容：

- `nikkedb\l2d` 中的日期化源资源；
- `nikkedb\indexes` 当前五个索引文件；
- `nikkedb\metadata` 原始元数据；
- `nikkedb\evidence` 的核验资料；
- `nikkedb\archive` 的回滚记录；
- `nikkedb\github-repository` 的 Git 数据和工作树；
- `SpinePet\res` 中 Atlas 引用的任何纹理页；
- 与 `.skel` 同名的 `.attachments.exclude`。

删除前至少完成三项检查：

1. `Get-ChildItem` 确认目标绝对路径位于预期的 D 盘目录。
2. 检查 `.atlas`、脚本、索引和应用代码是否仍引用目标。
3. 对最终资源运行 SpinePet 扫描和动画验证。

## 15. 推荐复用结构

```text
D:\SpineTools\
  nikkedb\
    l2d\
      mapped\
        YYYY-MM-DD__角色名 [cNNN]\
      unmapped\
        YYYY-MM-DD__待确认资源\
    indexes\
      rename-map.json
      resource-date-index.csv
      unresolved-index.json
    metadata\
    evidence\
    archive\
    github-repository\
  resources\
    <资源目录>\
      standing\
        character.skel
        character.atlas
        character.png
        character-attachments.tsv
        character-only.png
  SpinePet\
    res\
      <角色显示名>\
        <皮肤ID>\
          standing\
            c<角色ID>_<皮肤ID>.skel
            c<角色ID>_<皮肤ID>.atlas
            <atlas 页面>.png
            c<角色ID>_<皮肤ID>.attachments.exclude
          icons\
            c<角色ID>_<皮肤ID>_icon.png
    tools\
      atlas-cleaner\
      resource-layout\
      skeleton-inspector\
```

这套结构把上游证据、分析暂存、运行时资源和通用工具分开。新资源只在
完成身份确认和运行验证后进入 `SpinePet\res`；旧的
`SpinePet\res\<角色>\standing` 布局应先使用
`tools\resource-layout\Migrate-CharacterResources.ps1 -WhatIf` 检查：
规范名资源可以自动迁移，自定义名资源需要先人工确认身份并规范化。
