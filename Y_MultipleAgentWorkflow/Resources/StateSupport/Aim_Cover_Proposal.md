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

## 3. 配置 1.7

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
      "AimFireEffects": ["aim_fire_hair", "aim_fire_hip"],
      "CoverIdle": "cover_idle",
      "ToCover": "to_cover",
      "ReloadSequence": ["cover_reload"]
    }
  }
}
```

可选动画名和 `AimFireEffects` 为 `null` 时不序列化；`ReloadSequence` 可为空。
Aim/Cover 资源不完整时整个 `Battle` 省略。旧配置升级到 1.7 时保留位置、
缩放、速度、可见性和 Normal 常驻动画，不从旧 aim/cover 路径拼装不完整
Battle。

## 4. 动画识别与回退

Aim：

1. idle：`aim_idle`，再匹配同时含 `aim`、`idle`，最后任意 `idle`。
2. 进入：`to_aim`，再匹配同时含 `to`、`aim`。
3. 开火：`aim_fire`，再匹配同时含 `aim`、`fire`，最后任意 `fire`。
4. 开火附加效果：收集名称以 `aim_fire_` 开头且至少含一个 Spine 时间轴的
   动画。目前资源中的有效项为 `aim_fire_hair`、`aim_fire_hip`；同名空
   占位不会写入配置，也不会误当成主开火动画。

Cover：

1. idle：`cover_idle`，再匹配同时含 `cover`、`idle`，最后任意 `idle`。
2. 进入：`to_cover`，再匹配同时含 `to`、`cover`。
3. 换弹优先单段 `cover_reload`；否则按可用项播放
   `reload_start -> reload_loop 一次 -> reload_end`。

缺少进入动画时直接进入目标 idle；缺少 fire 时保持 Aim idle；缺少换弹时
直接回到 Cover idle。

主 `aim_fire` 自身包含的 bone、deform、attachment 等时间轴由 Spine 在
主轨完整应用，无需拆成额外配置。独立的 `AimFireEffects` 从第 1 轨开始
并行播放，延迟量等于 `to_aim` 时长，并与连续开火一起循环。切换 Aim idle、
Cover 或 Normal 时清除全部附加轨并恢复 setup pose，避免头发、臀部附件或
静态形变残留。

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
| 开火主体 | `aim_fire` | 41/41；内部 bone/deform/attachment 直接生效 | 现有 `AimFire` 主轨 |
| 开火附加 | `aim_fire_hair` | 名称 9 套，有效 6 套 | `AimFireEffects` 附加轨 |
| 开火附加 | `aim_fire_hip` | 名称 9 套，有效仅 Sugar 1 套 | `AimFireEffects` 附加轨 |
| 瞄准方向 | `aim_x`、`aim_y` | 41/41，时长约 1 秒 | 后续增加瞄准轴输入和独立混合轨，不绑定右键 |
| Aim 受击 | `aim_hit` | 41/41，约 0.4-0.667 秒 | 后续 `Hit.Aim` 一次性覆盖后恢复当前 Aim 状态 |
| Cover 受击 | `cover_hit` | 41/41，约 0.433-0.833 秒 | 后续 `Hit.Cover` 一次性覆盖后恢复 Cover |
| Cover 眩晕 | `cover_stun` | 41/41，约 0.033-0.067 秒，接近姿势保持 | 后续 `Stun.Cover` 循环或保持，解除时回 idle |
| Cover 死亡 | `cover_death` | 名称 7 套，有效 5 套 | 后续可空 `Death.Cover`，播放后停末帧 |
| Aim 技能 | `aim_skill_1`、`aim_skill_fire` | Sugar 1 套；Red Hood 两套 | 后续 `Skills.Aim[]`，由显式技能命令触发 |
| Cover 技能 | `cover_skill_1` | Privaty Variant04 1 套 | 后续 `Skills.Cover[]` |
| 分段换弹 | `cover_reload_start/loop/end` | Sugar 1 套，同时存在 `cover_reload` | 保持单段优先；仅单段缺失时使用三段回退 |
| 切换事件 | `SwitchToAimSkin`、`SwitchToCoverSkin` | 81/82 套 Aim/Cover 资源包含；Trony Sweet Step Cover 缺少 | 当前由资源槽切换完成，不重复绑定事件 |

有效开火附加效果的实际位置：

```text
SpinePet\res\Blanc - White Rabbit\aim\c270_aim_01.skel
SpinePet\res\Blanc Variant 03\03\aim\c27003_03_aim_00.skel
SpinePet\res\Cinderella Crystal Wave\00\aim\c515_aim_00.skel
SpinePet\res\Laplace Neo\00\aim\c103_aim_00.skel
SpinePet\res\Laplace Neo Variant01\01\aim\c10301_01_aim_00.skel
SpinePet\res\Sugar - Wild Backyard\02\aim\c14002_aim_02.skel
```

Sugar 同时具有 `aim_fire_hair` 和 `aim_fire_hip`；其余五套只有有效 hair。
Blanc 两套有效 hair 为时长 0 的静态时间轴，仍需在射击时应用。Liter
Guardfish、Modernia Variant 80、Rouge Variant 01 的 hair，以及 Blanc、
Laplace、Liter、Modernia、Noise、Rouge 的部分 hip 只有空动画名，已跳过。

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
配置迁移、扫描补齐/清除/幂等、空效果占位过滤、附加轨延迟与清理，以及
Normal/Battle 输入边界。当前实际配置基线为 61 个角色，其中 41 个具有完整
Battle；程序启动仍固定为 Normal。
