# Workflow Router

文档 ID：`WF-ROUTER`  
状态：`Active`  
维护计数：`2/5`  
最后更新：`2026-08-21`

## 路由

| 任务 | 读取 |
|---|---|
| 工作流结构、业务增殖、五层限制、维护计数 | `Workflow_Guide.md` |
| 跨项目配置、分类与模型入口接入 | `..\Workflow_Configuration_Guide.md` |
| SpineTools 构建、发布和运行验证 | `Project_Validation_Guide.md` |
| Agent 登记、冲突、等待、覆盖、心跳、构建屏障 | `Concurrency_Guide.md` |
| 新业务根初始化 | `Templates\BusinessRouter.template.md`、`Templates\DeveloperLog.template.md` |
| 实际租约操作 | `Scripts\WorkingAgent.ps1` |
| 协议回归 | `Scripts\Test-WorkingAgent.ps1` |
| 历史证据 | `DeveloperLog.md` |

修改根目录分类或全局规则前必须先询问用户，并短时申请 `workflow:root`。
