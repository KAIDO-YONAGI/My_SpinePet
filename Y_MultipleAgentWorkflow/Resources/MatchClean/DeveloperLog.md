# Resources Match/Clean Developer Log

## 2026-08-22：立绘自动保留全部 Skin 组件

- 将额外 Skin 的显示从单资源 `.skins.include` 白名单改为运行时自动合并
  `default` 与全部非默认 Skin，立绘中的 `bg`、`acc` 等组件默认保留。
- Arcana Fortune Mate 的桌椅、捕梦网、塔罗牌背景组件和 Ocean's Lament
  的背景、飘带均通过统一逻辑恢复，不修改原始骨骼、图集或贴图。
- `.attachments.exclude` 仍在创建 Skeleton 前应用，因此已有 Burst 显式
  清理规则继续生效。
- 验证证据：Arcana 与 Ocean's Lament 目标槽位均有附件，资源目标测试
  `7/7` 通过；Build 为 0 警告、0 错误，Publish/Run 通过且
  `SpinePet.exe` 持续响应。
- 本次实际影响隐藏组件匹配与资源加载行为，维护计数：`2/5 -> 3/5`。

## 2026-08-22：恢复 Ocean's Lament 额外 Skin 组件

- 确认 `c83502_02` 的 `bg` Skin 包含 `BG1`、`BG2` 和四条 ribbon，
  默认 Skin 未挂载这些组件。
- 运行时支持资源旁的 `.skins.include` 声明，将指定 Skin 与 `default`
  合并；Ocean's Lament 声明包含 `bg`，不修改原始骨骼、图集或贴图。
- 明确非 Burst standing 的背景和装饰组件默认保留，不因 `bg` 关键词自动
  进入清理范围。
- 验证证据：播放 `idle` 后六个目标 Slot 均有附件，资源目标测试 `8/8`
  通过，Build/Publish/Run 通过且 `SpinePet.exe` 持续响应。
- 本次实际影响隐藏组件匹配与资源加载行为，维护计数：`1/5 -> 2/5`。

## 2026-08-22：修正空动画配置误落 action

- 配置模式和设置面板统一验证已配置动画；空值或无效值按
  `idle -> idle* -> 第一动画` 回退。
- SkeletonInspector 增加动画名清单输出，用于批量核验默认状态。
- 验证证据：新增默认动画回退测试，动画目标测试 `6/6`、全量测试
  `191/191`，Build/Publish/Run 通过。
- 本次实际影响动画排查与匹配行为，维护计数：`0/5 -> 1/5`。

## 2026-08-21：匹配清理指南迁移

- 将旧中文文件名迁为 `SpineResource_Match_Clean_Guide.md`。
- 具体硬规则从 Skill 回收到权威指南，Skill 只保留触发和导航。
- 纯文档迁移，不增加维护计数。
