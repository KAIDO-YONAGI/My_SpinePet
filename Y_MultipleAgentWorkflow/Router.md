# My_SpinePet 多 Agent 工作流总路由

文档 ID：`ROOT-ROUTER`  
状态：`Active`  
最后更新：`2026-08-24`
最后核验：`2026-08-24`

本目录是 Codex、Claude、ZCode 共用的唯一权威文档入口。Agent 可以先做
轻量只读探索以识别任务，但在详细分析、创建子 Agent 或首次修改前，必须
完成路由并取得 WorkingAgent 租约。

## 1. 权威顺序

发生冲突时按以下顺序判断：

1. 用户在当前任务中的最新明确要求。
2. 实际代码、资源、运行结果与可复现实验。
3. 对应业务的 Guide 或 Design。
4. 对应业务 Router。
5. DeveloperLog 与 Proposal。
6. `AGENTS.md`、`CLAUDE.md` 和 `.zcode\skills` 等模型入口。

`Proposal` 只描述未实施方案，不得作为当前能力或现行规则使用。

## 2. 强制起步流程

1. 阅读本文件，根据任务描述选择一个或多个业务 Router。
2. 阅读 `Workflow\Concurrency_Guide.md`，扫描现有租约。
3. 用 `Workflow\Scripts\WorkingAgent.ps1` 获取精确范围的租约。
4. 若发现重叠的活动/等待租约，且当前客户端支持原生 Agent 通信，先向
   对方任务发送冲突通知，再开始修改。
5. 阅读被路由业务的 Guide/Design、Router 和必要日志。
6. 工作范围扩大前先执行 `UpdateScope`；结束、失败或取消均执行 `Release`，
   并在释放后通知正在等待的任务。

只读子 Agent 无法自行登记时，由父 Agent 创建 `read` 租约并填写
`parentLeaseId`。

## 3. 任务路由

| 任务线索 | 必读 Router | 权威内容 |
|---|---|---|
| 多 Agent、并发、租约、路由、模板、文档维护 | `Workflow\Router.md` | Workflow Guide / Concurrency Guide |
| Codex Agent 通信、重叠任务协调、租约释放通知 | `Workflow\Router.md` | Concurrency Guide |
| 为当前或其他项目配置多 Agent 工作流 | `Workflow_Configuration_Guide.md` | 分类、接入、初始化与验证方法 |
| 安装、分发、升级通用 Skill 或项目托管文件 | `Workflow\Router.md` | 外部分发仓库与 WorkflowInstance |
| My_SpinePet 构建、发布、运行验证 | `Workflow\Project_Validation_Guide.md` | 本项目已确认的验证流程 |
| WPF 界面、交互、窗口、托盘、桌面角色 GUI | `GUI\Router.md` | `GUI_Design.md` |
| 资源领域分类、跨资源业务判断 | `Resources\Router.md` | 资源业务下级导航 |
| zip/文件夹入库、角色 ID、图标、导入 `res` | `Resources\Load\Router.md` | Resources Load Guide |
| 背景/海浪/特效清理、附件匹配、点击与动画排查 | `Resources\MatchClean\Router.md` | Match/Clean Guide |
| standing/aim/cover 状态支持 | `Resources\StateSupport\Router.md` | Active Aim/Cover 配置与交互指南 |
| 跨业务任务 | 所有受影响 Router | 分别读取并分别计数 |
| 未知业务 | `Workflow\Router.md` | 先逐级匹配，再按增殖规则处理 |

## 4. 并发资源

常用共享资源：

```text
runtime:SpinePet
pipeline:BuildPublishRun
config:SpinePetUser
git:index
workflow:root
workflow:GUI
workflow:Resources.Load
workflow:Resources.MatchClean
workflow:Resources.StateSupport
```

路径写入必须声明 `path:<绝对或工作区相对路径>`。写任务未给出足够范围时，
同一业务根内按冲突处理。Router/DeveloperLog 更新仅在实际写入期间短时申请
`workflow:<业务根>`；根结构调整使用 `workflow:root`。

## 5. 增殖与维护

- 先从根 Router 逐级匹配同类业务；存在同类时按其 Router 和先验文档工作。
- 没有同类时，在最近大类下用 `Workflow\Templates` 初始化业务根。
- 新增顶级大类、改变工作区规则或无法确定归属时，先征得用户确认。
- `Y_MultipleAgentWorkflow` 内索引最多五层；出现第六层前必须合并、提升
  或精简分类。
- 每个实际影响业务且成功完成的任务使该业务维护计数 `+1`。失败、取消、
  并发登记和纯文档维护不计数。
- 达到 `5/5` 时复查本周期任务证据与代码，更新权威文档和本索引；无需改动
  则记录 `reviewed-no-change`，然后归零。

详细规则见 `Workflow\Workflow_Guide.md`。

## 6. 文档索引

| 文档 ID | 路径 | 职责摘要 | 触发任务 | 状态 | 更新 | 核验 | 旧路径 |
|---|---|---|---|---|---|---|---|
| `WF-ROUTER` | `Workflow\Router.md` | 工作流业务导航 | 路由、并发、增殖 | Active | 2026-08-21 | 2026-08-21 | 无 |
| `WF-GUIDE` | `Workflow\Workflow_Guide.md` | 路由、增殖、计数、日志规范 | 工作流维护 | Active | 2026-08-21 | 2026-08-21 | 无 |
| `CONCURRENCY-GUIDE` | `Workflow\Concurrency_Guide.md` | 租约与冲突协议 | 并发任务 | Active | 2026-08-21 | 2026-08-21 | 无 |
| `WF-PROJECT-VALIDATION` | `Workflow\Project_Validation_Guide.md` | My_SpinePet 已确认的构建、发布和运行验证 | 代码类改动收尾 | Active | 2026-08-21 | 2026-08-22 | 无 |
| `WF-CONFIG-METHOD` | `Workflow_Configuration_Guide.md` | 可带入真实项目的通用分类、接入、初始化与验证方法；My_SpinePet 仅为案例 | 配置或迁移多 Agent 工作流 | Active | 2026-08-22 | 2026-08-22 | 无 |
| `WF-INSTANCE` | `Workflow\WorkflowInstance.json` | 分发版本、初始化选项和托管文件哈希 | 项目工作流升级 | Active Metadata | 2026-08-21 | 2026-08-21 | 无 |
| `WF-DISTRIBUTION` | `..\..\Y_MultipleAgentWorkflow` | 通用 Skill 源码、三客户端适配与发布工具 | 安装、分发、显式升级 | External | 2026-08-22 | 2026-08-22 | 原 `My_Tools` 位置与全局 `.agents` 实体 |
| `WF-DESIGN-BASELINE` | `..\Y_MAW_DesignPlan.md` | 已落地架构与验证设计记录，不作为现行操作权威 | 审视工作流设计来源 | Reference | 2026-08-22 | 2026-08-22 | `Y_MAW_DesignPlan.txt` |
| `GUI-DESIGN` | `GUI\GUI_Design.md` | 已实现 GUI 权威设计 | GUI | Active | 2026-08-23 | 2026-08-23 | `GUI_Design.md` |
| `GUI-REFACTOR-PROPOSAL` | `GUI\GUI_Refactor_Plan.md` | 未实施的 GUI 结构重构分阶段计划 | GUI 结构重构 | Proposal | 2026-08-24 | 2026-08-24 | 无 |
| `RES-LOAD-GUIDE` | `Resources\Load\SpinePet_Resources_Load_Guide.md` | 资源入库与导入流程 | 资源导入 | Active | 2026-08-23 | 2026-08-23 | `SpinePet-通用资源导入指南.md` |
| `RES-MATCH-CLEAN-GUIDE` | `Resources\MatchClean\SpineResource_Match_Clean_Guide.md` | 资源匹配与清理流程 | 清理、动画排查 | Active | 2026-08-22 | 2026-08-22 | `Spine资源匹配与清理通用指南.md` |
| `STATE-AIM-COVER-GUIDE` | `Resources\StateSupport\Aim_Cover_Proposal.md` | Aim/Cover 导入、逐时间轴射击层、公开物理证据、配置、动画回退与输入规则 | 状态支持 | Active | 2026-08-23 | 2026-08-23 | `SpinePet\AIM_COVER_SUPPORT_NOTES.md` |
| `NIKKEDB-README` | `resources\nikkedb\README.md` | 外部证据库结构入口 | 资源证据查询 | External | 2026-08-20 | 2026-08-21 | 无 |
| `NIKKEDB-COSTUME-INDEX` | `resources\nikkedb\NIKKE服装ID对照表.md` | 现行服装名与上游 ID | 身份反查、图标 | External | 动态维护 | 2026-08-21 | 无 |
| `NIKKEDB-DATE-INDEX` | `resources\nikkedb\NIKKE资源日期索引.md` | 日期与结构化索引入口 | 资源日期、归类 | External | 2026-08-20 | 2026-08-21 | 无 |
| `NIKKEDB-VERIFY-ARCHIVE` | `resources\nikkedb\NIKKE资源核验报告.md` | 冻结历史核验 | 历史取证 | Archived External | 2026-08-20 | 2026-08-21 | 无 |

## 7. 维护计数

| 业务根 | 计数 | Router |
|---|---:|---|
| Workflow | `0/5` | `Workflow\Router.md` |
| GUI | `4/5` | `GUI\Router.md` |
| Resources.Load | `3/5` | `Resources\Load\Router.md` |
| Resources.MatchClean | `1/5` | `Resources\MatchClean\Router.md` |
| Resources.StateSupport | `0/5` | `Resources\StateSupport\Router.md` |
