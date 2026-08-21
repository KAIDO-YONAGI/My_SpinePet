# Workflow Developer Log

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
