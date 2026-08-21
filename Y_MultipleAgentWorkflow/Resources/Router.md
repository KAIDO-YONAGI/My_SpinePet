# Resources Router

文档 ID：`RESOURCES-ROUTER`  
状态：`Active`  
最后更新：`2026-08-21`

| 任务 | 下级 Router |
|---|---|
| 入库、ID、图标、导入与验证 | `Load\Router.md` |
| 匹配、背景/特效清理、点击与动画排查 | `MatchClean\Router.md` |
| standing/aim/cover 状态能力 | `StateSupport\Router.md` |

跨入库与清理的任务必须同时读取 Load 与 MatchClean，分别登记受影响范围。

`resources\nikkedb` 是工作区外部证据库，不复制到本工作流。它的子目录资料
优先通过 `resources\nikkedb\README.md`、资源日期索引和业务指南中已有索引
发现，避免在 Router 中镜像整棵证据目录。
