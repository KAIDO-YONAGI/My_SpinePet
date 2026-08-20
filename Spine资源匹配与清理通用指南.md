# Spine 资源匹配与清理规范

本文是**清理规范**：在导入 SpinePet 之前或之后，移除资源中的非角色
元素（背景、海面、特效、UI）并排查点击/动画问题。与
`SpinePet-通用资源导入指南.md` 配套使用——导入规范管"进 res"，
本文管"清什么、怎么清、清到什么程度"。

**所有相对路径以 `D:\SpineTools` 为根。**

## 0. 定位与前提

1. **清理是可选步骤，默认不做。** 只有使用方明确要求时才清理。
2. 清理发生在 `resources\` 下的**暂存副本**上，验证通过后才把成果
   放进 `SpinePet\res\`；不在 `resources\Characters\` 源备份上直接改，
   更不碰 `resources\nikkedb\`。
3. 清理对象永远是**单个资源**：排除规则、遮罩参数、验证结论
   都不跨资源复用。
4. 身份以文件名前缀 `c<角色ID>_<皮肤ID>` 为准；需要按名字反查 ID 时
   用 `resources\nikkedb\data\indexes\resource-date-index.json`，服装名到
   ID 的对应关系查 `resources\nikkedb\NIKKE服装ID对照表.md`。

## 1. 三层结构与清理原则

Spine 资源三层信息：

1. Skeleton：骨骼、Slot、Skin、Attachment、动画。
2. Atlas：附件名称到贴图区域的映射。
3. Texture：实际像素。

**优先在 Attachment 层移除对象，Texture 层只做辅助。**
只擦贴图像素会留下仍在运行的网格、错误的角色边界和异常点击区域；
顺序应当是"先排除附件，再按需擦残留像素"。

推荐处理顺序：

```text
复制到 resources\ 暂存
  → 导出附件清单（TSV）
  → 制定排除规则
  → 验证规则命中
  → 应用附件排除 + 验证渲染/边界/点击
  → （按需）贴图透明化
  → 复制进 SpinePet\res\ → Scan 验证
```

## 2. 流程一：分析（附件清单）

### 2.1 导出 TSV

```powershell
$project = 'SpinePet\tools\skeleton-inspector\SkeletonInspector.csproj'
$work = 'resources\<暂存目录>\standing'

dotnet run --project $project -- `
  (Join-Path $work 'character.skel') `
  (Join-Path $work 'character.atlas') |
  Set-Content -LiteralPath (Join-Path $work 'character-attachments.tsv') `
    -Encoding utf8
```

输出字段：`skin slot placeholder type attachment path region`，
并给出骨骼版本（应为 Spine 4.1.x）。

### 2.2 初筛关键词（只用于筛查，不能直接当删除条件）

```text
bg  background  scene  water  wave  jelly  particle
effect  fx  glow  light  foreground
```

这些词也可能出现在角色高光、头发、衣服或身体遮罩里；
`*_eyebg` 是眼白，**永远不删**。

### 2.3 核对清单

1. 按 `slot`、`path`、`region` 搜索背景、前景、粒子、场景命名；
2. 统计每条候选规则的命中数量；
3. 单独检查名称可疑但未命中的附件；
4. 确认角色身体、头发、服装、高光、遮罩没有被误选；
5. 特写/UI 类元素（相机取景框等）按使用方要求决定去留。

## 3. 流程二：排除规则（附件层）

### 3.1 排除文件

与骨骼同目录、同主文件名：

```text
character.skel
character.atlas
character.png
character.attachments.exclude
```

语法：

```text
# 注释和空行会被忽略
prefix:bg_
prefix:background_
prefix:water_
name:shared_effect_texture
```

- `prefix:` 不区分大小写前缀匹配；`name:` 完整名称匹配；
- 两侧空白去除；未知规则或空值应报错，避免静默漏删。

### 3.2 匹配字段（五个都要查）

同一资源可能通过不同字段引用，不能只匹配 region：

1. Skin placeholder 名称
2. Attachment 名称
3. Region/Mesh 的 Path
4. Atlas region 名称
5. Slot 名称

任一字段命中即标记排除：

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

```csharp
bool Matches(string candidate) =>
    IsPrefix
        ? candidate.StartsWith(Value, StringComparison.OrdinalIgnoreCase)
        : candidate.Equals(Value, StringComparison.OrdinalIgnoreCase);
```

### 3.3 移除时机（关键）

附件必须在**加载 SkeletonData 之后、创建 Skeleton 之前**移除，
即先复制成数组再删除，避免遍历时修改集合：

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

在边界计算前移除，可以同时解决：背景仍被绘制、透明附件撑大边界、
角色被夹在屏幕边缘、点击空白区命中角色、拖动锚点与视觉不一致。

## 4. 流程三：验证

1. 排除后仍能生成可渲染几何（不崩溃、无空引用）；
2. Setup Bounds / Animation Envelope 不再包含场景尺寸；
3. 点击角色身体触发动画，点击周围透明区域不触发
   （两阶段命中：先包围矩形、再渲染三角形）；
4. 点击动画结束后恢复待机（见 §6）；
5. 与清理目标比对命中数量，防止多删/漏删；
6. 通过后才复制进 `SpinePet\res\` 并 **Scan** 复验。

## 5. 流程四：贴图透明化（按需，高风险）

附件排除足够时**不做**这一步。仍有视觉残留（如共享 region）时用：

```powershell
python 'SpinePet\tools\atlas-cleaner\Mask-AtlasTexture.py' `
  --atlas  'resources\<暂存>\standing\character.atlas' `
  --texture 'resources\<暂存>\standing\character.png' `
  --output 'resources\<暂存>\standing\character-only.png' `
  --prefix bg_ --prefix water_ --name shared_effect_texture
```

工具特性：不覆盖输入、输出命中 region 数与像素数、正确处理
`rotate:90/270`、不区分大小写。输出用新文件名，目视和运行验证后
才允许替换运行时纹理。

### 图集重叠风险

不同 region 可能共享或重叠像素：

- 直接擦除命中矩形：背景清除彻底，但重叠的角色贴图会损坏
  （表现为"人物不完整"）；
- 保护保留区域：角色不误删，但重叠处可能残留背景像素。

最稳妥组合：**附件层负责禁止渲染，贴图层只清不与角色冲突的像素**。
出现角色残缺时，回退方案是"保留排除规则 + 恢复原版贴图"。

## 6. 动画排查

### 6.1 点击动画匹配

命名不统一，按以下优先级找：

```text
action → click → touch → tap → reaction → interact → skillcut
```

每个候选词先匹配完整名称，再匹配 `词_` 前缀（如 `action_1`）。
点击动画以非循环方式播放：`SetAnimation(0, name, false)`。

### 6.2 待机回退

点击结束后显式恢复循环待机，否则停在最后一帧：

1. 完整名称 `idle`；
2. 以 `idle` 开头的第一个动画；
3. 动画列表第一个动画。

```csharp
animationState.AddAnimation(0, idleName, true, 0);
```

点击动画与待机相同时不必重复排队。

## 7. Atlas 结构清理（少用）

`Clean-Atlas.ps1` 删除 `.skel` 字节内容中未出现的 region，
默认创建 `.bak`。先 `-WhatIf` 预览：

```powershell
& 'SpinePet\tools\atlas-cleaner\Clean-Atlas.ps1' -Folder $work -WhatIf
```

前置条件：`.skel`/`.atlas` 同目录同名、存在以骨骼主名开头的 PNG、
region 名可在骨骼二进制 UTF-8 字符串中核对。输出
`SKIP: no complete resource sets` 表示未执行，不代表资源干净。
**不要为绕过检查改纹理名**——改纹理名必须同步更新 atlas 页面引用
并重新验证。该工具不判断角色语义，来源不明或共享区域多的资源
保留原结构、用排除文件更稳妥。

## 8. 常见失败原因

### 仍有背景残留

- 只擦了 Texture，没有移除 Attachment；
- 保护逻辑恢复了重叠像素；
- region 名未命中，但 slot / path / placeholder 含背景命名；
- 共享 region 需要完整名称规则。

### 角色贴图被擦坏

- 背景和角色 region 共用像素；
- 未处理旋转 region；
- 规则关键词过宽；
- 直接改原图且没有备份。

### 点击没有动画 / 点击后停住

- 只找固定名称 `action`，实际命名不同；
- 点击区域仍被场景附件撑大，角色几何未命中；
- 点击动画非循环播放但没有排队恢复 `idle`。

## 9. 清理边界

可以清理：

- `resources\` 暂存区中可再生的 TSV、预览图、失败输出；
- Atlas 工具产生且已对照过的 `.bak`；
- 未被 atlas 引用、不属于图标或证据的重复暂存纹理；
- 空的暂存工作目录。

不能按"看起来杂乱"直接删除：

- `resources\nikkedb\` 全部内容（l2d、indexes、metadata、evidence、
  archive、github-repository、Preview）；
- `resources\Characters\` 的源备份和 `resources\zips\`；
- `SpinePet\res\` 中 atlas 引用的任何纹理页；
- 与 `.skel` 同名的 `.attachments.exclude`。

删除前至少完成三项检查：

1. 确认目标绝对路径位于预期目录内；
2. 检查 atlas、脚本、索引、应用代码是否仍引用目标；
3. 对相关资源完成 Scan 和动画验证。

## 10. 推荐暂存结构

```text
resources\
  Characters\                     源备份（只进不改）
  zips\                           入库后的 zip
  <暂存目录>\
    standing\
      character.skel
      character.atlas
      character.png
      character-attachments.tsv    分析产物
      character-only.png           遮罩产物（验证后才替换）
```

`SpinePet\res\` 的最终布局见导入规范 §4；排除文件随骨骼主名走，
资源改 ID 或改名时同步（导入规范 §3.2）。
