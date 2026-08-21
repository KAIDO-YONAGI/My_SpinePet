# 跨项目多 Agent 工作流配置方法

文档 ID：`WF-CONFIG-METHOD`  
状态：`Active`  
最后更新：`2026-08-21`  
适用环境：`Windows + PowerShell 7`

本文说明如何把已经在 SpineTools 验证过的多 Agent 路由、业务文档和并发租约
体系配置到其他项目。通用方法由全局 Skill
`multiple-agent-workflow-config` 提供；本文记录 SpineTools 的项目实例与验证
证据。项目中的现行操作始终以根 `Router.md` 及其指向的 Guide/Design 为准。

## 1. 配置目标

一个可复用的工作流应同时解决：

1. Agent 只读取完成当前任务所需的最小上下文。
2. 不同模型和 Agent 从同一个 Router 获取项目事实。
3. 同类任务复用已有 Guide/Design，而不是反复重新探索。
4. 多 Agent 在共享工作树中声明真实读写范围，并能识别冲突。
5. 文档随着实际任务周期维护，Proposal 不会被误当成已实现能力。

推荐根结构：

```text
Y_MultipleAgentWorkflow/
├─ Router.md
├─ DeveloperLog.md
├─ Workflow_Configuration_Guide.md
├─ WorkingAgent/
│  └─ README.md
├─ Workflow/
│  ├─ Router.md
│  ├─ DeveloperLog.md
│  ├─ Workflow_Guide.md
│  ├─ Concurrency_Guide.md
│  ├─ Templates/
│  └─ Scripts/
└─ <BusinessRoot>/
   ├─ Router.md
   ├─ DeveloperLog.md
   └─ <Guide-or-Design.md>
```

## 2. 分类角度

### 2.1 一级：稳定知识域

一级业务根描述长期稳定的责任边界，不按模型、Agent、编程语言或文件扩展名
分类。适合成为一级根的例子：

| 分类 | 责任 |
|---|---|
| `Workflow` | 路由、并发、模板、日志和维护机制 |
| `GUI` | 界面结构、交互、窗口和视觉行为 |
| `Resources` | 资源识别、处理、部署、证据与状态能力 |

### 2.2 二级：任务阶段或处理目标

同一知识域内部，再按会重复出现的任务阶段或处理目标拆分。例如 SpineTools
的 Resources：

| 子类 | 划分角度 | 独立原因 |
|---|---|---|
| `Load` | 资源进入项目的生命周期 | 有独立导入流程、ID、图标和部署资源 |
| `MatchClean` | 资源可用前的匹配与清理 | 有独立附件规则、动画排查和清理方法 |
| `StateSupport` | 运行状态能力 | 当前主要是未实施 Proposal，不能混入现行导入规则 |

GUI 没有继续拆分，是因为当前 GUI 任务共享同一份设计权威和维护周期。目录
不是越细越好；分类应降低路由成本，而不是复刻源码目录。

### 2.3 新建业务根的判断

候选分类满足下列任一强条件，或同时满足多个弱条件时，才值得独立：

- 有清晰且反复出现的任务触发词。
- 有独立的 Guide/Design，不能由上级文档简洁表达。
- 有不同的共享资源、写入路径或并发冲突范围。
- 有独立的维护周期和任务证据。
- 可以独立演进，不会让多数任务同时读取相邻分类。

以下情况不新建业务根：

- 只有一份辅助索引或外部证据。
- 一次性想法、尚未实施的单点方案。
- 同一流程的一个步骤或一个文件类型。
- 目录只会包含 Router/Log，没有真实业务权威内容。

这些内容应进入现有 Router 索引、DeveloperLog 或标记为 `Proposal`。

## 3. 逐级路由与增殖

1. 从根 Router 按任务名词、目标路径、运行资源和输出结果匹配。
2. 进入最接近的大类 Router，检查同义任务和下级分类。
3. 有同类时读取其 Guide/Design、Router 和必要日志。
4. 无同类时先提出建议归属；新增顶级大类必须征得用户确认。
5. 确认后从模板初始化业务根，至少包含 Router 和 DeveloperLog。
6. 目录深度即将超过五层时，优先提升独立子类、合并重复层级，或用 Router
   标签替代目录。

跨业务任务必须路由到所有受影响业务。例如“导入资源并清理背景”同时进入
`Resources.Load` 和 `Resources.MatchClean`，两边分别读取、记录和计数。

## 4. 权威文档与外部证据

推荐权威顺序：

1. 用户在当前任务中的最新明确要求。
2. 实际代码、资源、运行结果与实验。
3. Guide/Design。
4. 业务 Router。
5. DeveloperLog 和 Proposal。
6. 模型入口与 Skill。

外部数据库、生成索引和已忽略的大体积资料不必复制到工作流中。Router 记录
它们的真实路径、职责、状态和核验时间即可。外部证据不能替代项目内现行
Guide，历史报告应标记为 Archived 或 Reference。

## 5. 并发资源设计

租约资源至少从三个角度声明：

```text
path:<实际读写路径>
workflow:<业务根>
runtime:<共享运行时>
pipeline:<构建、发布或运行屏障>
config:<共享配置>
git:index
```

- 路径不相交的写任务可以并行。
- 父目录与子目录视为相交。
- 写任务无法给出足够路径时，同一业务根默认冲突。
- Router/Log 只在实际写入时短时取得 `workflow:<业务根>`。
- 根分类调整短时取得 `workflow:root`。
- 构建、发布和运行使用统一的 `pipeline:BuildPublishRun` 全局屏障。
- 普通并发覆盖不能自动放行构建屏障。

PowerShell 内调用数组参数时，优先直接调用脚本并传递数组：

```powershell
& $workingAgentScript -Action Acquire `
  -Categories @('GUI') `
  -Resources @('path:src\Views', 'workflow:GUI')
```

不要把带引号的数组值拼成外层 `pwsh -File` 字符串；引号可能成为资源名的
一部分。需要启动子进程时使用 `ProcessStartInfo.ArgumentList`。

## 6. 模型入口接入指南

初始化默认使用 `EntryMode=None`，不修改任何模型入口。先检查：

- 根 `AGENTS.md`、`CLAUDE.md` 等已有规则。
- 项目级 `.zcode\skills` 或其他客户端入口。
- 用户级 Skill 目录是否为实体、Junction 或 SymbolicLink。
- 多个客户端是否已经共享同一个 Skill 实体。

然后向用户提供两种接入方式：

1. **保留并加入导航**：保留现有规则，加入可识别、可重复更新的 Router
   导航区块。适合已有成熟规范的项目。
2. **替换为精简入口**：入口只要求读取根 Router。适合规则已经完成迁移、
   且用户明确接受移除旧入口内容的项目。

未得到明确选择前不得修改入口。不得在 Codex、Claude、ZCode 目录分别复制
同一 Skill；应先确定唯一实体，再为其他客户端建立目录 Junction。

## 7. 初始化步骤

使用全局 Skill 时：

1. 索引项目现有文档、入口、构建命令、共享运行时和忽略规则。
2. 输出建议业务树、拆分理由、资源名和迁移清单。
3. 单独询问用户是否配置项目验证流程：
   - `None`：不创建项目验证指南；
   - `Guide`：创建并索引独立的
     `Workflow\Project_Validation_Guide.md`，再由用户确认具体命令。
4. 等待用户确认顶级分类、文档迁移和入口接入方式。
5. 调用初始化脚本生成工作流根、业务 Router/Log、租约脚本和测试。
6. 选择 `Guide` 时，把确认后的项目验证流程写入独立指南；Router 只索引。
7. 运行配置校验和 WorkingAgent 回归测试。
8. 迁移完成后删除双重权威；必要的旧路径只保留在索引元数据中。

示例：

```powershell
$skill = 'C:\Users\12248\.agents\skills\multiple-agent-workflow-config'

& "$skill\scripts\Initialize-Workflow.ps1" `
  -ProjectRoot 'D:\ExampleProject' `
  -Categories @('Workflow', 'GUI', 'Resources.Load', 'Resources.MatchClean') `
  -ProjectValidationMode None

& "$skill\scripts\Test-WorkflowConfiguration.ps1" `
  -ProjectRoot 'D:\ExampleProject' `
  -RunWorkingAgentTests
```

初始化脚本不会修改模型入口。目标工作流已存在时默认拒绝覆盖；`-Merge`
只补充缺失文件，不重写现有内容。先用 `-WhatIf` 查看计划。

## 8. 文档维护

每个成功且实际影响业务的任务使该业务维护计数 `+1`。跨业务分别计数。
失败、取消、并发登记、只读调查和纯文档维护不计数。

达到 `5/5` 时：

1. 复查本周期 DeveloperLog 和实际代码/资源。
2. 更新 Guide/Design、Router 与根路径索引。
3. 无需修改时记录 `reviewed-no-change`。
4. 完成复查后将计数归零。

路径迁移与文档更新必须在同一次变更中同步索引。

## 9. SpineTools 验证实例

SpineTools 使用：

- `Workflow` 管理工作流自身。
- `GUI` 管理 WPF 界面和交互设计。
- `Resources.Load` 管理资源导入。
- `Resources.MatchClean` 管理匹配、清理和动画排查。
- `Resources.StateSupport` 隔离未实施的 aim/cover Proposal。

已验证场景包括 GUI、导入、清理、Aim/Cover、跨业务、未知业务、第五次维护
和五层限制。WorkingAgent 回归覆盖 12 类并发行为，结果为 `12/12`。首次
SpineTools 初始化时已选择独立项目验证指南。本次完整收尾中，Build 为
0 警告 0 错误，Publish 成功，Run 后进程持续运行；这些是 SpineTools
实例证据，不是其他项目的默认要求。

Agent 启动环境曾缺少 `WINDIR`，导致 WPF FontCache URI 异常。验证时仅给
Run 子进程设置 `$env:WINDIR=$env:SystemRoot` 后成功；这是调用进程环境
问题，不应写成应用代码或通用项目规则。

## 10. 验收清单

- 根 Router 能把已知、跨业务和未知任务路由到正确位置。
- 每个业务根都有 Router、DeveloperLog 和明确能力边界。
- 目录深度不超过五层。
- Proposal、Reference、External 和 Active 状态没有混用。
- WorkingAgent 脚本通过语法检查和回归测试。
- `.gitignore` 只忽略运行时租约，不忽略权威文档和脚本。
- 模型入口修改经过用户选择，Skill 只有一个实体。
- 初始化时已明确选择 `ProjectValidationMode`。
- 项目验证流程位于独立指南并由 Router 索引。
- 构建/运行命令来自目标项目，不从 SpineTools 示例硬编码推断。
