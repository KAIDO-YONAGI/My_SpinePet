# 根工作流 Developer Log

只记录跨业务结构变更和根索引维护；具体任务证据进入对应业务日志。

## 2026-08-21：登记跨项目配置方法

- 根索引加入 `Workflow_Configuration_Guide.md` 和最终设计基线。
- 明确全局 Skill 管通用方法、项目文档管项目实例，设计记录不参与现行规则
  冲突裁决。
- Codex、Claude、ZCode 共用一个 Skill 实体；项目入口仍需用户选择后接入。

## 2026-08-21：建立统一多 Agent 工作流

- 建立唯一入口、业务路由、维护计数和五层索引限制。
- 将 GUI、资源导入、资源匹配清理与 Aim/Cover 资料迁入独立业务根。
- 引入 WorkingAgent 文件租约、固定注册表锁和并发测试。
- 根 `AGENTS.md`、`CLAUDE.md` 与 `.zcode\skills` 缩减为导航入口。
- 本次属于工作流初始化与纯文档迁移，不计入业务维护周期。
