# GUI Router

文档 ID：`GUI-ROUTER`  
状态：`Active`  
维护计数：`4/5`（2026-08-24 达到 5/5 复查归零后，重构 1、2、真实状态同步与下拉标题修复各计入）  
最后更新：`2026-08-24`

GUI、WPF 配置面板、桌面角色交互、托盘、窗口生命周期和界面状态持久化，
统一读取 `GUI_Design.md`。历史重构证据见 `DeveloperLog.md`。

未实施的结构重构分阶段计划见 `GUI_Refactor_Plan.md`
（`GUI-REFACTOR-PROPOSAL`，Proposal 状态，不作为现行能力）。

并发范围通常声明具体 `path:SpinePet\src\SpinePet\...`；更新本 Router、
GUI Design 或日志时短时使用 `workflow:GUI`。
