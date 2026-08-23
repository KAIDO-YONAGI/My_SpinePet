# Resources State Support Developer Log

## 2026-08-23：按资源显式配置射击物理与附件轨

- 配置升级到 1.8，把字符串 `AimFireEffects` 替换为结构化
  `BattleEffects`，显式记录动画、Replace/Add 混合、Alpha 和循环语义；
  旧 1.7 配置只通过已审计骨骼档案迁移。
- 全量解析 41 套 Aim：主 `aim_fire` 保留在第 0 轨，身体、胸部、头发、
  后坐、deform 和 attachment 时间轴由主轨完整应用；独立附加轨不再按
  `aim_fire_*` 名称自动猜测。
- 仅为 Cinderella、Laplace 两套和 Sugar 写入 4 个资源档案、5 条动态
  BattleEffects。Sugar 配置 hair + hip，其余三套配置 hair。
- Blanc 两套 hair 是时长 0 的静态姿势且与主射击大量重叠，已从配置档案
  排除；运行时也跳过零时长效果，避免高轨 Replace 覆盖身体抖动。
- 核验 41/41 的 `GunMountPoint` 均为不可绘制的 PointAttachment。嵌入
  骨骼的武器会随主轨显示；只有人体和挂点的资源需要未来新增外部 Weapon
  资源和挂点合成，无法靠动画名称补齐。
- `aim_x/y` 的 DeformTimeline、受击、眩晕、死亡和技能资源继续保留为
  独立状态方案，本次不擅自绑定输入。
- 指南新增逐资源配置技巧：主轨/附加轨判定、目标重叠检查、Replace/Add、
  Alpha、Loop、骨骼档案键和完整视觉验收流程。
- Debug 全量自动化测试 `274/274` 通过；Release 和实际配置迁移待当前运行
  实例释放文件锁后完成。
- 本次实际影响状态支持，维护计数：`4/5 -> 5/5`。

## 2026-08-23：修复跨状态纹理回收与 Normal 恢复

- 定位到纹理清理只统计当前资源，遗漏 standing/aim/cover 非活动资源槽；
  其他角色隐藏或移除后会误删槽内 GPU 纹理，返回 Normal 时触发持续帧失败。
- 纹理存活集合现覆盖当前资源与全部状态槽；若 GPU 缓存仍因设备或清理流程
  丢失，`NativeTextureSource` 会从原 PNG 重新解码并按相同 alpha 规则上传。
- 资源状态切换显式报告成败；失败不再污染运行时状态。成功返回 Normal 后
  清除临时/附加轨并重新循环 configured-or-idle standing 动画。
- 新增资源槽保留、像素二次上传和切换失败回滚测试；全量测试 `251/251`，
  Release Build 0 警告、0 错误，Publish/Run 通过并出现 `startup-complete`。
- 本次实际影响状态支持，维护计数：`3/5 -> 4/5`。

## 2026-08-23：接入 Aim 射击附加时间轴

- 配置升级到 1.7，Battle 动画新增可空 `AimFireEffects`；扫描 Spine
  骨骼时只写入至少包含一个时间轴的 `aim_fire_*`，跳过同名空占位。
- `to_aim` 完成后，从附加轨并行循环 hair/hip 效果；切 Aim idle、Cover
  或 Normal 时清除附加轨并恢复 setup pose，避免附件和静态形变残留。
- 全量资源审计确认 6 套有效 `aim_fire_hair`，其中 Sugar 同时具有唯一有效
  `aim_fire_hip`；主 `aim_fire` 内部形变仍由主轨完整播放。
- 指南补充 aim_x/y、受击、眩晕、死亡、技能、分段换弹和切换事件的实际
  资源位置与后续配置方案，本次不擅自绑定额外输入。
- 全量自动化测试 `249/249` 通过；Release Build 0 警告、0 错误，
  Publish 成功，Run 后出现 `startup-complete` 且实例持续响应。
- 实际配置已升级为 1.7：61 个角色、41 个 Battle、6 个效果配置；hair
  为 6 套、hip 为 1 套，没有空效果项或失效 Battle。
- 本次实际影响状态支持，维护计数：`2/5 -> 3/5`。

## 2026-08-23：修复 Cover 待机被错误动画覆盖

- 核验实际 Battle 配置和 40 套当前可加载 Cover 骨骼：
  `CoverIdle` 均为 `cover_idle`，换弹序列为 `cover_reload` 或分段回退，
  两者在骨骼中是独立动画。
- 修复配置面板显隐时把 Normal 的 `ConfiguredAnimation` 应用到 Battle
  骨骼的问题；Aim/Cover 资源现在保持自己的当前轨道和待机。
- 为模式切换、战斗状态切换和右键长按/释放增加运行时代次校验，旧的异步
  回 Cover 任务不能在用户已切回 Normal 或手动选定状态后再次排入换弹。
- 新增真实 Anis Cover 骨骼回归，确认
  `to_cover -> cover_reload -> cover_idle(loop)` 最终恢复正确待机。
- 全量自动化测试 `245/245` 通过；Release Build 0 警告、0 错误，
  Publish 成功，Run 后出现 `startup-complete` 且实例持续响应。
- 本次实际影响状态支持，维护计数：`1/5 -> 2/5`。

## 2026-08-23：Aim/Cover 正式支持

- 新增按 `rename-map.json` 精确资源编号导入，standing 始终导入，Aim/Cover
  仅在双方骨骼、atlas 和全部纹理页完整时成对导入。
- 配置升级到 1.6，新增全局 BattleRules 与可空角色 Battle；扫描可自动补齐
  完整配置、清除失效配置，并保持重复扫描幂等。
- 新增手动 Normal/Battle 与 Cover/Aim 切换；Battle 右键长按进入 Aim 连续
  开火，释放或捕获丢失后回 Cover 并执行换弹回退。
- `Aim_Cover_Proposal.md` 保留文件名并升级为 Active 权威指南。
- 完整审计 `resources\Characters` 后，43 套 Spine 4.1 战斗资源已导入并
  写入实际配置；配置共 63 个角色、43 个 Battle，启动仍为 Normal，
  手动进入 Battle 后默认 Cover。Spine `4.0.47` 的 Dolla 战斗资源未写入。
- 真实 `c017_01` 与全量自动化测试 `240/240` 通过；Release Build
  0 警告、0 错误，Publish 成功，Run 经指南允许的 `WINDIR` 子进程回退后
  出现 `startup-complete` 且实例持续响应。
- 本次实际影响状态支持，维护计数：`0/5 -> 1/5`。

## 2026-08-21：Aim/Cover 方案迁移

- 将零散说明迁入 `Aim_Cover_Proposal.md`。
- 明确标记为 Proposal，避免被 Agent 当作当前能力。
- 纯文档迁移，不增加维护计数。
