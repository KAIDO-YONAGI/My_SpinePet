# Aim / Cover 配置与交互指南

> 文档 ID：`STATE-AIM-COVER-GUIDE`
> 状态：`Active`
> 最后核验：`2026-08-23`

本文记录 SpinePet 已实现的 Normal/Battle 资源模型、配置格式、导入条件、
动画回退和输入规则。文件名保留 `Aim_Cover_Proposal.md` 以兼容既有索引，
内容已不再是未实施提案。

## 1. 状态模型

- 程序启动、首次展示和重新启动始终进入 `Normal`，加载 `standing`。
- Normal 的默认常驻动画为精确 `idle`，缺失时使用现有默认动画回退。
- 只有用户手动选择 `Battle` 才进入战斗资源，默认状态为 `Cover`。
- Battle 中可手动选择 `Cover` 或 `Aim`；手动 Aim 会保持 Aim idle。
- 切回 Normal 会立即恢复 standing 和正常动画。
- 状态切换以渲染层实际完成资源槽交换为准；目标槽缺失或加载失败时保留
  上一个已渲染状态，并把界面选择回滚到该状态。
- 当前模式与战斗状态只存在于运行时，不写入配置。

统一目录：

```text
SpinePet\res\<角色或资源名>\<皮肤>\
  standing\
  aim\
  cover\
  icons\
```

扫描按“角色 + 皮肤 + 状态”归组。standing 可独立存在；只有 aim、cover
两套均通过骨骼、atlas 和全部纹理页验证时，该皮肤才拥有 Battle。
单边缺失、文件损坏或动画解析失败时，整个 `Battle` 字段省略；已有失效字段
会在扫描或配置规范化时清除。

## 2. 指定清单导入

应用内已无 DB/nikkedb 导入入口。普通 **Add** 的 Skeleton/UnityFS 导入只
接受 standing，避免把单套战斗资源写成可用 Battle。离线自动化可以把
nikkedb 当只读证据源，但应用运行时不得定位或扫描它。

已经入库的角色目录使用 `SpinePet\tools\battle-catalog-importer` 按明确清单
审计：

```powershell
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- --audit resources\Characters "<资源目录名>" "<另一个资源目录名>"
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- resources\Characters SpinePet\res "<资源目录名>" "<另一个资源目录名>"
```

工具只把精确 `Standing`、`Aim`、`Cover` 目录视为主状态，且三套资源都必须
通过 Spine 4.1 兼容性、骨骼实际解析、atlas 和全部纹理页验证。完整资源映射
到现有角色或分配独立 ID；重复运行保持幂等。目录清单必填，工具不提供默认
全库扫描。2026-08-23 历史审计曾检查 67 个目录，
43 套通过并已导入。`Dolla Dark Rose` 的 Aim/Cover 为 Spine `4.0.47`，
不生成 Battle。

## 3. 配置 1.9

`Global.BattleRules` 固定记录当前交互规则：

```json
{
  "StartupMode": "Normal",
  "DefaultBattleState": "Cover",
  "RightHoldThresholdMs": 300,
  "ContinuousFireWhileHeld": true,
  "ReloadOnRelease": true,
  "ShortRightClickOpensPanel": true
}
```

完整皮肤的角色项包含可空 `Battle`：

```json
{
  "Battle": {
    "Aim": {
      "SkelPath": "...\\aim\\c017_01_aim_00.skel",
      "AtlasPath": "...\\aim\\c017_01_aim_00.atlas",
      "TexturePath": "...\\aim\\c017_aim_01.png",
      "ExtraTexturePaths": []
    },
    "Cover": {
      "SkelPath": "...\\cover\\c017_01_cover_00.skel",
      "AtlasPath": "...\\cover\\c017_01_cover_00.atlas",
      "TexturePath": "...\\cover\\c017_cover_01.png",
      "ExtraTexturePaths": []
    },
    "Animations": {
      "AimIdle": "aim_idle",
      "ToAim": "to_aim",
      "AimFireLayers": [
        {
          "Animation": "aim_fire",
          "Blend": "Replace",
          "Alpha": 1,
          "Loop": true,
          "ExcludeTimelines": [
            "RGBATimeline@slot:gun_8"
          ]
        },
        {
          "Animation": "aim_fire_hair",
          "Blend": "Replace",
          "Alpha": 1,
          "Loop": true
        }
      ],
      "CoverIdle": "cover_idle",
      "ToCover": "to_cover",
      "ReloadSequence": ["cover_reload"]
    }
  }
}
```

可选动画名和 `AimFireLayers` 为 `null` 时不序列化；`ReloadSequence`
可为空。每条射击层显式配置动画、混合模式、透明度、循环和时间轴过滤：

- `Blend` 支持 `Replace` 和 `Add`，未知值规范化为 `Replace`。
- `Alpha` 规范化到 `0..1`；0 值效果删除。
- `Loop` 决定该层是否循环。
- `IncludeTimelines` 非空时是精确允许列表。
- `ExcludeTimelines` 是精确排除列表；键格式为
  `TimelineType@Target`，例如 `RotateTimeline@bone:hair_1`、
  `RGBATimeline@slot:gun_8`、
  `DeformTimeline@slot:sweat_4/sweat_4`。
- 动画缺失、没有时间轴、时长为 0 或过滤后为空时，运行时不建立该层。

Aim/Cover 资源不完整时整个 `Battle` 省略。旧配置升级到 1.9 时保留位置、
缩放、速度、可见性和 Normal 常驻动画，不从旧 aim/cover 路径拼装不完整
Battle。旧 `AimFire`、`AimFireEffects` 和 `BattleEffects` 会在能够读取
真实 Aim 骨骼时重新生成 `AimFireLayers`；迁移成功后旧字段删除。面向未来
版本的未知配置只做兼容读取，不擅自清除未知字段。

## 4. 动画识别与回退

Aim：

1. idle：`aim_idle`，再匹配同时含 `aim`、`idle`，最后任意 `idle`。
2. 进入：`to_aim`，再匹配同时含 `to`、`aim`。
3. 开火：`aim_fire`，再匹配同时含 `aim`、`fire`，最后任意 `fire`。
4. 开火层：主 `aim_fire` 对所有完整资源做结构化审计；额外
   `aim_fire_hair` / `aim_fire_hip` 仍只采用第 6 节的已审计档案。同名
   空占位、零时长静态姿势和未审计额外动画均不配置。

Cover：

1. idle：`cover_idle`，再匹配同时含 `cover`、`idle`，最后任意 `idle`。
2. 进入：`to_cover`，再匹配同时含 `to`、`cover`。
3. 换弹优先单段 `cover_reload`；否则按可用项播放
   `reload_start -> reload_loop 一次 -> reload_end`。

缺少进入动画时直接进入目标 idle；缺少 fire 时保持 Aim idle；缺少换弹时
直接回到 Cover idle。

Aim 的第 0 轨始终承担 `to_aim -> aim_idle(loop)`，因此 aim idle 中持续的
身体、胸部、头发、衣物和武器摆动不会因连续射击丢失。`AimFireLayers`
从第 1 轨开始并行播放，延迟量等于 `to_aim` 时长，并与连续开火一起循环。

生成每条射击层时使用以下结构规则：

```text
若射击动画的精确 TimelineType@Target 只有一帧，
且 aim_idle 中同一精确键为多帧动态，
则把该键写入该层 ExcludeTimelines。
```

这不是按骨骼名称猜“物理部位”，而是逐文件比较实际时间轴。单帧射击姿势
不会再用高轨 `Replace` 冻结底轨的动态抖动；射击层中的动态骨骼、颜色、
attachment、draw-order 和 deform 仍保留。切换 Cover 或 Normal 时清除
全部射击层并恢复 setup pose，避免头发、臀部附件或静态形变残留。

Normal 的 `ConfiguredAnimation` 只允许应用到 standing 资源。配置面板显隐、
Normal 动画刷新或角色常驻动画恢复不得重选 Aim/Cover 的动画轨道，否则缺少
同名 Normal idle 时会错误回退到战斗骨骼的首个动画。

切回 Normal 时必须清除临时播放和全部附加轨，并从第 0 帧循环 standing 的
`ConfiguredAnimation`；配置无效时按 `idle -> idle* -> 第一动画` 回退。
standing、aim、cover 中当前未显示的资源仍保存在角色资源槽中，因此 GPU
纹理清理必须把当前资源和全部资源槽一起视为存活；若 GPU 缓存被清除，纹理
上传允许从原 PNG 重新解码，不能依赖“CPU 像素只会消费一次”的前提。

## 5. 输入与界面

- 右侧详情使用显式 `Normal | Battle` 模式选择。
- 原动画下拉框右侧是同样式的 `Cover | Aim` 下拉框。
- Normal 启用原动画下拉框并禁用战斗状态；Battle 反之。
- Battle 进入时预加载 Aim/Cover，默认 Cover。
- Battle 中右键按住满 300ms：切 Aim，播放 `to_aim` 后连续
  `aim_fire`；无 fire 时停在 Aim idle。
- 长按松开或捕获丢失：切 Cover，播放 `to_cover`、换弹回退序列和
  Cover idle。
- 模式、战斗状态和右键操作使用运行时代次；用户手动切换后，先前尚未完成
  的异步长按或回 Cover 任务必须失效，不能晚到并覆盖当前选择。
- 界面可先呈现用户选择，但资源交换失败或抛出异常时必须立即同步回最后一个
  成功状态；不得出现显示为 Normal、模型仍在 Battle 且动画停止的分裂状态。
- 未满 300ms 的右键短按保持原面板打开/关闭行为。
- Normal 或无 Battle 的角色，右键行为保持原样。
- 左键点击、拖动与位置保存规则不变。

## 6. 当前战斗动画资源清单

以下统计基于 2026-08-23 对 `SpinePet\res` 中 41 套完整 Aim/Cover 的实际
骨骼解析；“有效”表示动画至少包含一个 Spine 时间轴。

| 情况 | 动画 | 资源情况 | 配置方案 |
|---|---|---|---|
| Aim 常驻物理 | `aim_idle` | 41/41；28 套含射击层未覆盖的动态键 | 第 0 轨持续循环，承载身体、头发、衣物等基础动态 |
| 开火主体 | `aim_fire` | 41/41；40 套为有效动态，Rouge Variant 01 为零时长静态占位 | `AimFireLayers` 第 1 轨；逐时间轴过滤静态覆盖 |
| 显式开火附加 | `aim_fire_hair` | 4 套动态档案 | 后续高轨，使用同一逐时间轴过滤规则 |
| 显式开火附加 | `aim_fire_hip` | Sugar 唯一动态档案 | 后续高轨，使用同一逐时间轴过滤规则 |
| 静态冲突附加 | `aim_fire_hair` | Blanc 两套有时间轴但时长为 0，并与主轨大量重叠 | 不配置；运行时也跳过零时长效果 |
| 瞄准方向 | `aim_x`、`aim_y` | 41/41 均含 DeformTimeline，时长约 1 秒 | 后续由指针方向采样/混合，不作为循环射击附加轨 |
| Aim 受击 | `aim_hit` | 41/41，约 0.4-0.667 秒 | 后续 `Hit.Aim` 一次性覆盖后恢复当前 Aim 状态 |
| Cover 受击 | `cover_hit` | 41/41，约 0.433-0.833 秒 | 后续 `Hit.Cover` 一次性覆盖后恢复 Cover |
| Cover 眩晕 | `cover_stun` | 41/41，约 0.033-0.067 秒，接近姿势保持 | 后续 `Stun.Cover` 循环或保持，解除时回 idle |
| Cover 死亡 | `cover_death` | 名称 7 套，有效 5 套 | 后续可空 `Death.Cover`，播放后停末帧 |
| Aim 技能 | `aim_skill_1`、`aim_skill_fire` | Sugar 1 套；Red Hood 两套 | 后续 `Skills.Aim[]`，由显式技能命令触发 |
| Cover 技能 | `cover_skill_1` | Privaty Variant04 1 套 | 后续 `Skills.Cover[]` |
| 分段换弹 | `cover_reload_start/loop/end` | Sugar 1 套，同时存在 `cover_reload` | 保持单段优先；仅单段缺失时使用三段回退 |
| 切换事件 | `SwitchToAimSkin`、`SwitchToCoverSkin` | 81/82 套 Aim/Cover 资源包含；Trony Sweet Step Cover 缺少 | 当前由资源槽切换完成，不重复绑定事件 |

已写入显式开火附加档案的实际位置：

```text
SpinePet\res\Cinderella Crystal Wave\00\aim\c515_aim_00.skel
SpinePet\res\Laplace Neo\00\aim\c103_aim_00.skel
SpinePet\res\Laplace Neo Variant01\01\aim\c10301_01_aim_00.skel
SpinePet\res\Sugar - Wild Backyard\02\aim\c14002_aim_02.skel
```

Sugar 同时配置 `aim_fire_hair` 和 `aim_fire_hip`，其余三套只配置 hair。
全量配置共 40 条有效主射击层和 5 条额外层。Cinderella 的 hair 为持续
动态形变；Laplace 两套主要切换火花附件；Sugar 的 hair/hip 过滤后分别
仅保留 `aim_holster` 和 `aim_body_12` 的真正动态键。

以下两套 Blanc 的 `aim_fire_hair` 虽有时间轴，但时长为 0，且和主
`aim_fire` 的骨骼/槽位大量重叠。把它们放在高轨 `Replace` 会冻结或覆盖
主射击中的身体、胸部和头发运动，因此明确不写入配置：

```text
SpinePet\res\Blanc - White Rabbit\aim\c270_aim_01.skel
SpinePet\res\Blanc Variant 03\03\aim\c27003_03_aim_00.skel
```

Liter Guardfish、Modernia Variant 80、Rouge Variant 01 的 hair，以及
Blanc、Laplace、Liter、Modernia、Noise、Rouge 的部分 hip 只有空动画名，
同样跳过。

### 6.1 武器和挂点

41/41 套 Aim 骨骼都存在 `GunMountPoint`，但它是 Spine
`PointAttachment`：只提供坐标和旋转锚点，本身不可绘制。资源包内已有的
枪械 mesh/region 会随主 `aim_fire` 正常渲染；只有人体和挂点、没有枪械
图片/mesh 的资源无法由动画配置补出武器。

当前 `Battle` 配置没有“外部武器骨骼/atlas/贴图”字段，也没有把
`GunMountPoint` 绑定到另一套渲染资源的合成逻辑。若后续补外部枪械，应新增
可空 `Weapon` 资源配置，并在同一角色渲染帧中按挂点矩阵绘制，而不是把
PointAttachment 当作丢失贴图。Anis Star、Snow White Heavy Arms 等资源的
武器已经嵌入 Aim 骨骼；Blanc White Rabbit、Alice 等部分包则确实只有人体。

### 6.2 其余动画的配置边界

- `aim_x` / `aim_y` 是瞄准方向形变，需要输入轴和采样权重；不能简单循环。
- `aim_hit`、`cover_hit` 是一次性受击状态，结束后恢复触发前 Aim/Cover。
- `cover_stun` 更接近姿势保持，需要显式解除事件。
- `cover_death` 只在有效资源中播放一次并停末帧。
- `aim_skill_*` / `cover_skill_*` 需要技能命令和技能结束恢复规则。
- `SwitchToAimSkin` / `SwitchToCoverSkin` 是资源动画事件；当前已有资源槽
  切换，不再重复绑定，避免同一次切换执行两遍。

### 6.3 逐资源配置技巧

新增或修正资源时按以下顺序判断，不能只看动画名称：

1. 先单独播放主 `aim_fire`。身体后坐、胸部/头发抖动、枪械、火花和 deform
   已在主层正常出现的内容，不要重复写入额外 `AimFireLayers`。
2. 再单独播放候选附加动画，并检查时长、时间轴类型、目标 bone/slot 和
   attachment。只有主轨确实缺失、候选轨又具有动态内容时才建立档案。
3. 对比候选轨与 `aim_idle` 的精确目标集合。射击层单帧、idle 同键多帧时，
   把该键写入 `ExcludeTimelines`；不能因为 bone 名含 body/hair 就整块删除。
4. `AttachmentTimeline`、`DrawOrderTimeline`、动态 `DeformTimeline` 和枪械
   颜色/位置轨必须逐条确认。自动规则不会仅因它们是附件或枪械而删除。
5. 只控制独立附件或作者明确拆出的覆盖轨时优先 `Replace`。只有确认时间轴
   表示相对增量、叠加后不会产生双倍位移或颜色溢出时才使用 `Add`。
6. `Alpha` 从 1 开始做视觉核验；效果过强时逐级降到 0.75、0.5、0.25。
   不要用低 Alpha 掩盖错误目标或错误混合模式。
7. 连续射击配套循环效果使用 `Loop = true`。一次性闪光、抛壳或短促后坐应
   使用 `Loop = false`，并确认每次主射击循环是否需要重新触发。
8. 档案键使用 Aim 骨骼文件名（不含扩展名），例如 `c515_aim_00`，不要用
   显示名或目录名；显示名和皮肤目录可能变化或冲突。

代码档案位于
`CharacterBattleConfigFactory.AimFireEffectProfiles`；所有完整资源的主
`aim_fire` 由工厂自动生成。每次增加额外档案必须同时：

- 用 Spine 解析结果确认候选动画 `Duration > 0` 且存在时间轴。
- 在 `CharacterBattleConfigFactoryTests` 写入精确骨骼与预期效果。
- 让资源扫描重写实际 `config.json`，确认只生成预期档案。
- 分别观察单发、持续按住、松开换弹、切 Cover、切 Normal，确认没有姿势
  残留、组件消失、双倍位移、过曝或动画暂停。
- 若枪械缺失，先核对 atlas/attachment 是否真的含枪；只有
  `GunMountPoint` 时应归类为外部武器资源缺失，不能添加假的动画效果补救。

### 6.4 全量射击层过滤清单

主 `aim_fire` 审计共发现 19 个静态覆盖冲突档案、367 个精确键；其中 Rouge
的主动画时长为 0，整层跳过，因此实际配置落盘为 18 个档案、323 个主层
排除键。Sugar 两条额外层再增加 27 个排除键，最终为 18 个角色档案、
350 个持久化排除键。

```text
c19102_02_aim_00   3
c01701_01_aim_00  73
c0170_aim_00      15
c583_aim_00        5
c270_aim_01      135
c27003_03_aim_00   2
c07002_02_aim_00  21
c07204_04_aim_00   5
c35202_aim_02     10
c32101_01_aim_00   3
c26080_80_aim_00   1
c26002_02_aim_00   1
c40301_01_aim_00   7
c82001_01_aim_00   2
c82002_02_aim_00   2
c27201_01_aim_00  44  (零时长，整层跳过)
c22501_01_aim_00  29
c4011_aim_01       1
c14002_aim_02      8  (+ hair 13, hip 14)
```

主层冲突类型包括 Translate 107、Rotate 88、Scale 69、Shear 63、RGBA 37、
Deform 1、TranslateX 1、TranslateY 1。自动规则没有排除任何
`AttachmentTimeline` 或 `DrawOrderTimeline`，避免枪械、火花、弹壳和遮挡
顺序因通用过滤消失。视觉复核优先级最高的是 `c270_aim_01`、
`c01701_01_aim_00`、`c27201_01_aim_00`、`c35202_aim_02` 和
`c22501_01_aim_00`。

### 6.5 GitHub 公开实现与物理配置取证

截至 2026-08-23，没有找到 Shift Up 官方公开的 NIKKE 客户端源码，也没有
找到能验证为原版反编译代码的 Aim/Cover 状态机。以下只能分级作为实现参考：

- Spine 官方运行时和文档可确认 slot、skin、attachment、mesh、deform、
  draw order 与 PointAttachment 的通用语义，可信度高：
  `https://github.com/EsotericSoftware/spine-runtimes/tree/4.1`
- `alesha229/anime-helper` 公开了逐角色
  `aim-physics-*`、`aim-shooting-physics-*`、`cover-physics-*` 配置，
  字段包含射击冲力、随机角度、RK4、质量、阻尼、弹簧、位移/角度限制和
  逐骨骼混合模式，可信度中高，但仓库没有证明这些文件是官方原样导出：
  `https://github.com/alesha229/anime-helper/tree/09a1171bca6b686ee26fddbffafd8308d96161cb/anime-overlay/public/physics`
- `alesha229/spine-nikke-physic` 展示了“Spine 时间线 + 射击冲量 + 逐骨骼
  弹簧/阻尼求解”的社区实现，可信度中等：
  `https://github.com/alesha229/spine-nikke-physic/tree/a0abde5c21cc40d145ab1e4816c7771802bada5f`
- `DickyDicky7/godot-nikke` 和 `alesha229/anime-helper` 的播放器都采用
  Aim idle、fire、to_aim、to_cover 等分层状态，但轨号不同，说明固定轨号
  是播放器实现细节，不能写成“原版规则”。

公开物理 JSON 进一步证明胸部、髋部、头发、斗篷、裙摆、领带、绑带、翅膀
等骨骼集合是逐角色、逐状态配置，不能只按骨骼前缀或动画名猜测。本项目
1.9 已把现有 Spine 时间线能真实执行的内容全部落为显式射击层；外部弹簧
JSON 尚未接入，因为当前 C# 渲染器没有对应求解器。未来接入时建议新增：

```json
{
  "Physics": {
    "AimIdle": { "Source": "...", "Bones": {} },
    "AimShooting": { "Source": "...", "Bones": {} },
    "Cover": { "Source": "...", "Bones": {} }
  }
}
```

只有同时完成配置解析、逐骨骼求解、射击冲量、状态切换和视觉回归后，才可把
外部物理标记为 Active；当前不得仅写入未消费字段并宣称抖动已生效。

有效 `cover_death` 位于：

```text
SpinePet\res\Liter Guardfish\01\cover\c08201_01_cover_00.skel
SpinePet\res\Modernia Variant 80\80\cover\c26080_80_cover_00.skel
SpinePet\res\Moran Variant 02\02\cover\c28102_02_cover_00.skel
SpinePet\res\Rouge Variant 01\01\cover\c27201_01_cover_00.skel
SpinePet\res\Sakura Midnight Stealth\01\cover\c28201_01_cover_00.skel
```

技能与分段换弹位置：

```text
SpinePet\res\Sugar - Wild Backyard\02\aim\c14002_aim_02.skel
SpinePet\res\Red Hood Variant 01\01\aim\c47001_01_aim_00.skel
SpinePet\res\Red Hood Variant 02\02\aim\c47002_02_aim_00.skel
SpinePet\res\Privaty Variant 04\04\cover\c17004_04_cover_00.skel
SpinePet\res\Sugar - Wild Backyard\02\cover\c14002_cover_02.skel
```

## 7. 验证基线

真实基线资源为 `c017_01`。其 Aim 应识别 `aim_idle`、`to_aim`、
`aim_fire`，Cover 应识别 `cover_idle`、`to_cover`、`cover_reload`。
验证必须同时覆盖完整三状态、standing-only、单边缺失、多页纹理、冲突、
配置迁移、扫描补齐/清除/幂等、显式档案过滤、零时长保护、附加轨延迟与
清理，以及 Normal/Battle 输入边界。当前资源扫描基线为 61 个角色，其中
41 个具有完整 Battle；40 个角色生成有效 `AimFireLayers`，共 45 条射击
层。18 个角色的 350 个精确时间轴排除键会持久化到配置；Rouge Variant 01
的零时长主射击层跳过。程序启动仍固定为 Normal。

实际 `config.json` 必须为 1.9，旧 `AimFire`、`AimFireEffects` 和
`BattleEffects` 均不得残留。验证还必须确认所有自动排除项中不存在
`AttachmentTimeline` 或 `DrawOrderTimeline`，Sugar 的 hair/hip 动态键
保留，Anis 的枪械 RGBA 静态覆盖被过滤而 idle 动态仍在第 0 轨。

2026-08-23 最终基线：Debug 全量自动化测试 `288/288`；Release Build
0 警告、0 错误。发布脚本受机器时钟影响生成了未来日期命名的产物
`release\SpinePet-Release-2026-08-24-06 13 00`，zip 为 76.7 MB。
按验证指南补入 `WINDIR` 环境回退后，Run 启动 PID 32632，
`Responding=True`；同一机器时钟写入的未来日期日志时间戳为
`2026-08-24 06:14:13.240 [App] startup-complete`。真实配置核对为
61 个角色、41 个 Battle、40 个有效射击档案、45 条射击层、18 个过滤
档案、350 个排除键，旧字段和 attachment/draw-order 误排除均为 0。
