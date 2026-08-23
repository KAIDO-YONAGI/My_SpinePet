# Workflow Developer Log

## 2026-08-23：纳入 Codex Agent 通信协作协议

- 在通用工作流源与本地实例的 Router、并发指南和配置方法中明确：
  WorkingAgent 租约是冲突事实，Codex 原生任务消息是可选协调通道。
- 增加重叠租约通知、释放后回传、疑似失活不自动接管、消息事实边界和无原生
  通信时的降级规则。
- 本次为纯协议文档维护，不计入业务维护计数；租约脚本和冲突语义未改变。

## 2026-08-22：通用方法同步、分发去重与第五次维护复查

- 将当前项目验证后的完整通用配置方法同步到独立 Skill 仓库的唯一方法源
  `src\skills\multiple-agent-workflow-config\references\configuration-method.md`。
- 初始化器改为从唯一方法源生成项目自包含的
  `Workflow_Configuration_Guide.md`，删除模板目录中的第二份手工维护副本。
- 删除 Git 中 Codex、Claude、ZCode 三份 `release-layout` 快照；客户端布局
  仅在 `.tmp` 临时生成，发布资产仅输出到 `dist`。
- 新增 `README.cn.md`，说明仓库构成、重复文件来源、Skill 使用、跨机器迁移、
  项目初始化、升级、卸载和发布流程。
- 分发版本提升到 `1.0.1`；分发回归 `36/36`，其中 WorkingAgent 仍为
  `12/12`；Skill 元数据、Skill Creator 快速校验及 Claude 插件/Marketplace
  校验通过。
- 独立仓库提交 `18f52a5` 已推送，私有 Release `v1.0.1` 已发布；重新下载
  7 个发布文件后，`SHA256SUMS` 中登记的 6 个资产全部核验一致。
- 本任务是本维护周期第 5 次实际 Workflow 能力变更。已对照本周期日志、
  初始化器、分发脚本、索引和通用方法完成复查并更新权威文档，维护计数
  `4/5 -> 5/5 -> 0/5`。

## 2026-08-22：通用配置方法与项目案例边界纠正

- 将 `Workflow_Configuration_Guide.md` 重构为可直接带入任意目标项目的现场
  配置方法，补充事实盘点、分类决策表、确认门槛、初始化和验收流程。
- GUI 与资源分类改为“划分角度和成立条件”的验证示例，不再表达为其他项目
  应照抄的固定目录。
- 明确 Skill 是方法的执行载体，目标项目 Router 与 Guide/Design 才拥有
  项目事实；项目验证流程仍为初始化时单独选择的可选配置。
- `Y_MAW_DesignPlan.md` 更新到 2026-08-22，补记项目迁移、相对路径和复验
  结果，并明确旧 `.txt` 路径只作为历史元数据。
- 本次属于纯文档边界纠正，不改变运行时能力，维护计数保持 `4/5`。

## 2026-08-22：项目根目录迁移与路径去硬编码

- 将项目根目录从旧位置迁移到
  `D:\My_Docs\Programmes\My_SpinePet`，保留 Git 历史、远端和未提交修改。
- 模型入口、项目 Skill、工作流指南和资源索引改用项目相对路径。
- PowerShell 与 Python 工具从脚本自身位置推导项目根，不再依赖固定盘符。
- 工作流配置校验 `58 pass / 0 warning / 0 error`，WorkingAgent 回归
  `12/12`；新路径下 Build 为 `0` 警告、`0` 错误，Publish 与 Run 成功。
- 旧根目录的数据与空目录树已全部迁出；当前 Codex 工作区仍持有
  `D:\SpineTools` 根目录句柄，因此只残留一个零文件、零子目录的空壳，
  重新打开新工作区后即可移除。
- 本次实际提升工作流跨路径复用能力，维护计数：`3/5 -> 4/5`。

## 2026-08-22：分发仓库本地路径迁移

- 将通用 Skill 与分发仓库整体迁移到
  `D:\My_Docs\Programmes\Y_MultipleAgentWorkflow`，Git 历史和远端保持不变。
- 重建 Codex、Claude、ZCode 三个 Junction，并同步更新 MAW 安装记录和目录
  快捷方式。
- 更新 SpineTools 路由、配置指南、设计记录和学习文档中的操作路径。
- 本次仅调整本地存放位置和导航，不改变工作流能力，维护计数保持 `3/5`。

## 2026-08-21：独立分发仓库与三客户端发布

- 建立独立源仓库（现位于 `D:\My_Docs\Programmes\Y_MultipleAgentWorkflow`），通用 Skill 源码只在
  `src\skills\multiple-agent-workflow-config` 维护。
- 生成 Codex、Claude、ZCode 插件布局、离线包、SHA-256 清单及显式安装、
  更新、卸载和项目实例升级接口；不提供后台联网检查。
- 新增 `Workflow\WorkflowInstance.json`，仅允许自动更新未漂移的 WorkingAgent
  脚本、回归测试、业务模板和租约忽略规则。Router、日志、Guide、Design 与
  Proposal 保持项目所有。
- 分发回归覆盖初始化、五层限制、WorkingAgent `12/12`、Copy/Junction、
  拒绝覆盖、替换、卸载、离线安装和托管文件漂移保护。
- Skill frontmatter 与 OpenAI 元数据通过 `quick_validate.py`。
- 本次实际扩展 Workflow 能力，维护计数：`2/5 -> 3/5`。

## 2026-08-21：跨项目配置方法与全局 Skill

- 将已验证的分类方法整理为跨项目配置指南，并以 SpineTools 作为实例。
- 新增全局 `multiple-agent-workflow-config` Skill：`.agents` 保存唯一实体，
  Claude 与 ZCode 通过 Junction 共用。
- Skill 支持确认后初始化业务树、复制自包含租约运行时并执行结构校验；
  默认不修改模型入口。
- 设计基线吸收等待原子激活、UTC、租约所有权、失活处理和构建屏障等实际
  验证修正。
- 验证证据：Skill 校验、临时项目初始化矩阵、WorkingAgent `12/12`、
  Junction 一致性、仓库检查和 Build/Publish/Run。
- 用户纠正：项目构建/发布/运行流程从根 Router 拆入
  `Project_Validation_Guide.md`；通用初始化必须先询问是否配置，不再强制。
- 本次实际扩展 Workflow 能力，维护计数：`1/5 -> 2/5`。

## 2026-08-21：协议初始化

- 定义逐级路由、业务根增殖、五层上限和 `5/5` 维护周期。
- 实现 JSON 文件租约、独占注册表锁、心跳、等待、覆盖和疑似失活识别。
- Build/Publish/Run 使用独立全局屏障，普通覆盖不自动放行该屏障。
- 增加并发回归脚本，真实子进程测试 `12/12` 通过：覆盖只读并行、
  不相交写入、路径和共享资源冲突、原子竞争、心跳、疑似失活、等待、
  用户覆盖、范围扩大、工作流临界区、构建屏障、租约所有权和异常遗留。
- 路由演练覆盖 GUI、导入、清理、Aim/Cover、跨业务、未知业务、第五次维护
  与五层上限，全部命中预期规则。
- 收尾验证：Build 0 警告 0 错误，Publish 成功生成 release 与 zip，Run 后
  `SpinePet.exe` 持续运行并记录 `startup-complete`。当前 Agent shell 缺失
  `WINDIR`，首次启动触发已知 WPF FontCache URI 异常；仅为 Run 子进程设置
  `WINDIR=$SystemRoot` 后验证通过，未修改项目或系统永久环境。
- 本次脚本实现实际影响 Workflow，维护计数：`0/5 -> 1/5`。
