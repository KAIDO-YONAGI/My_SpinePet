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

## 2. nikkedb 编号导入

应用的 **DB** 入口接收资源编号，例如 `c017_01`。编号以不区分大小写的
精确值匹配：

```text
resources\nikkedb\data\indexes\rename-map.json
```

命中后沿 `currentRelativePath` 到 `resources\nikkedb\l2d` 定位资源。
standing 始终导入；aim 和 cover 只有双方完整时才一起导入，否则双方均跳过。
目标已存在时整次导入失败且不覆盖，失败产生的文件和空目录会回滚。

普通 **Add** 的 Skeleton/UnityFS 导入仍只接受 standing，避免把单套战斗
资源写成可用 Battle。

### 2.1 `resources\Characters` 全量导入

已经入库的角色目录使用 `SpinePet\tools\battle-catalog-importer` 统一审计：

```powershell
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- --audit resources\Characters
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- resources\Characters SpinePet\res
```

工具只把精确 `Standing`、`Aim`、`Cover` 目录视为主状态，且三套资源都必须
通过 Spine 4.1 兼容性、骨骼实际解析、atlas 和全部纹理页验证。完整资源映射
到现有角色或分配独立 ID；重复运行保持幂等。2026-08-23 实际审计 67 个目录，
43 套通过并已导入。`Dolla Dark Rose` 的 Aim/Cover 为 Spine `4.0.47`，
不生成 Battle。

## 3. 配置 1.8

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
      "AimFire": "aim_fire",
      "BattleEffects": [
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

可选动画名和 `BattleEffects` 为 `null` 时不序列化；`ReloadSequence`
可为空。每个附加效果显式配置动画、混合模式、透明度和循环语义：

- `Blend` 支持 `Replace` 和 `Add`，未知值规范化为 `Replace`。
- `Alpha` 规范化到 `0..1`；0 值效果删除。
- `Loop` 决定附加轨是否循环。
- 动画缺失、没有时间轴或时长为 0 时，运行时不建立附加轨。

Aim/Cover 资源不完整时整个 `Battle` 省略。旧配置升级到 1.8 时保留位置、
缩放、速度、可见性和 Normal 常驻动画，不从旧 aim/cover 路径拼装不完整
Battle。1.7 的 `AimFireEffects` 仅通过已审计的骨骼档案迁移；未列入档案的
旧效果会被删除，避免名字相同但时间轴用途不同的资源被误配。

## 4. 动画识别与回退

Aim：

1. idle：`aim_idle`，再匹配同时含 `aim`、`idle`，最后任意 `idle`。
2. 进入：`to_aim`，再匹配同时含 `to`、`aim`。
3. 开火：`aim_fire`，再匹配同时含 `aim`、`fire`，最后任意 `fire`。
4. 开火附加效果：不做名称前缀自动识别。只有本指南第 6 节列出的骨骼文件
   才写入显式 `BattleEffects`；同名空占位、零时长静态姿势和未审计资源
   均不配置。

Cover：

1. idle：`cover_idle`，再匹配同时含 `cover`、`idle`，最后任意 `idle`。
2. 进入：`to_cover`，再匹配同时含 `to`、`cover`。
3. 换弹优先单段 `cover_reload`；否则按可用项播放
   `reload_start -> reload_loop 一次 -> reload_end`。

缺少进入动画时直接进入目标 idle；缺少 fire 时保持 Aim idle；缺少换弹时
直接回到 Cover idle。

主 `aim_fire` 自身包含的 bone、deform、attachment 等时间轴由 Spine 在
第 0 轨完整应用，不得被附加轨代替。独立的 `BattleEffects` 从第 1 轨开始
并行播放，延迟量等于 `to_aim` 时长，并与连续开火一起循环。切换 Aim idle、
Cover 或 Normal 时清除全部附加轨并恢复 setup pose，避免头发、臀部附件或
静态形变残留。当前四套档案均使用 `Replace + Alpha 1`，因为它们是作者
独立制作的覆盖/附件轨；`Add` 和 Alpha 调整只用于后续逐资源视觉校准。

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
| 开火主体 | `aim_fire` | 41/41；40 套含动态时间轴，Rouge Variant 01 为零时长静态边界；Snow White Heavy Arms 运动量很小 | 保持 `AimFire` 第 0 主轨，完整应用 bone/deform/attachment |
| 显式开火附加 | `aim_fire_hair` | 4 套动态档案 | `BattleEffects` 独立轨，逐骨骼配置 |
| 显式开火附加 | `aim_fire_hip` | Sugar 唯一动态档案 | `BattleEffects` 独立轨，逐骨骼配置 |
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

Sugar 同时配置 `aim_fire_hair` 和 `aim_fire_hip`，其余三套只配置 hair，
共 4 个资源档案、5 条附加轨。Cinderella 的 hair 为持续动态形变；
Laplace 两套主要切换火花附件；Sugar 的 hair/hip 分别提供上身和臀部动态。

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
   已在主轨正常出现的内容，不要重复写入 `BattleEffects`。
2. 再单独播放候选附加动画，并检查时长、时间轴类型、目标 bone/slot 和
   attachment。只有主轨确实缺失、候选轨又具有动态内容时才建立档案。
3. 对比候选轨与主轨的目标集合。大量重叠且候选时长为 0，通常是静态姿势
   或导出占位，应跳过；否则高轨 Replace 会把主轨物理冻结。
4. 只控制独立附件或作者明确拆出的覆盖轨时优先 `Replace`。只有确认时间轴
   表示相对增量、叠加后不会产生双倍位移或颜色溢出时才使用 `Add`。
5. `Alpha` 从 1 开始做视觉核验；效果过强时逐级降到 0.75、0.5、0.25。
   不要用低 Alpha 掩盖错误目标或错误混合模式。
6. 连续射击配套循环效果使用 `Loop = true`。一次性闪光、抛壳或短促后坐应
   使用 `Loop = false`，并确认每次主射击循环是否需要重新触发。
7. 档案键使用 Aim 骨骼文件名（不含扩展名），例如 `c515_aim_00`，不要用
   显示名或目录名；显示名和皮肤目录可能变化或冲突。

代码档案位于
`CharacterBattleConfigFactory.AimFireEffectProfiles`。每次增加档案必须同时：

- 用 Spine 解析结果确认候选动画 `Duration > 0` 且存在时间轴。
- 在 `CharacterBattleConfigFactoryTests` 写入精确骨骼与预期效果。
- 让资源扫描重写实际 `config.json`，确认只生成预期档案。
- 分别观察单发、持续按住、松开换弹、切 Cover、切 Normal，确认没有姿势
  残留、组件消失、双倍位移、过曝或动画暂停。
- 若枪械缺失，先核对 atlas/attachment 是否真的含枪；只有
  `GunMountPoint` 时应归类为外部武器资源缺失，不能添加假的动画效果补救。

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
41 个具有完整 Battle；4 个资源档案具有 5 条 BattleEffects。程序启动仍
固定为 Normal。
