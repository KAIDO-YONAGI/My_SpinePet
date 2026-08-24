# SpinePet GUI 结构重构计划

文档 ID：`GUI-REFACTOR-PROPOSAL`  
状态：`Proposal`（描述未实施方案，不作为现行能力或现行规则使用）  
最后更新：`2026-08-24`

## 1. 背景与目标

当前 GUI 存在两个枢纽类：

- `Services/CharacterManager.cs`（1358 行）：同时承担配置仓储、显隐
  single-flight 协调、战斗交互状态机、全局设置与事件转发四重职责。
- `Views/MainWindow.xaml.cs`（1340 行）：同时是 Window、自身的
  DataContext/ViewModel 和事件路由器。

目标是拆分这两个枢纽类、让状态职责归位，同时满足硬约束：

1. 行为零变化，现有测试（最近记录 291/291）全程绿，不修改既有断言。
2. 公共 API 冻结：`ICharacterRenderHost`、`CharacterManager` 公共面、
   `App.xaml.cs` 与控制器的消费方式在阶段 1–2 中不变。
3. 渲染层（`Rendering/Native/`）与配置管线（`ConfigService`/
   `ConfigNormalizer`/`ConfigFileCommitter`）不动。
4. 每阶段独立验证、独立收尾，任一阶段后可停。

## 2. 阶段划分

### 阶段 0：准备与基线（本轮已执行）

- 取 write 租约；本文档落盘并在 GUI Router 与根 Router 登记。
- 基线：全量测试通过数 + Release 构建干净。

### 阶段 1：拆分 CharacterManager（公共面保真）（2026-08-24 已完成）

新组件全部 `internal`，沿用"门面先行"模式：

| 新组件 | 迁入内容 |
|---|---|
| `Services/BattleInteractionController.cs` | `CharacterBattleRuntimeState`、Mode/Battle 状态机、右键长按/短按处理、`TriggerBattleHoldAsync`/`ReturnToCoverAsync`、`CharacterBattleStateChanged` |
| `Services/CharacterShowCoordinator.cs` | `ShowFlight` 单飞机制、`EnsureCharacterShownAsync`、Show/Hide/ShowAll/HideAll/RestoreAll 核心 |
| `Services/CharacterCatalog.cs` | `_config`/`_configService`/`_resourceCoordinator`/`_identityService` 及其全部使用：增删改、换肤回滚、同步、ResetAllSettings、全局设置 |

`CharacterManager` 收缩为组合门面（保留 6 属性 + 20 方法 + 6 事件、
internal 4 参构造、渲染事件订阅与回写、`SetConfigMode`）。

新增 `BattleInteractionControllerTests`、`CharacterShowCoordinatorTests`；
原 `CharacterManagerTests` 不改断言原样通过。

### 阶段 2：提取 MainViewModel（XAML 零改动）（2026-08-24 已完成）

- `ICharacterSettingsHost` 移至 `ViewModels/`（签名不变）。
- 新建 `ViewModels/MainViewModel.cs`：实现 `ICharacterSettingsHost`，迁入
  MainWindow 全部 INPC 状态、选项集、派生属性、搜索恢复逻辑；setter
  副作用改事件出口（`SearchFilterRequested`、`ThumbnailScaleApplied`、
  `ScaleComponentsChanged`）。
- MainWindow 瘦身至约 700 行，只留视图职责；`DataContext = MainViewModel`。
- 控制器重连：Settings 的 host 直接传 VM；Library 的 3 个选中/通知闭包
  换 VM；PanelActivation 的 clearSearch 换 VM；PreviewNavigation 的
  getSelected 换 VM；ListBox/Window 属主等控件引用保留。
- 新增 `MainViewModelTests`，补上 Views 状态层不可测的缺口。

### 阶段 3：单一状态源（后续轮次）

`CharacterRuntimeStateStore` 合并渲染快照 + 战斗运行时 + 配置投影为每角色
不可变状态，UI 从快照派生；逐步拆除 `_isRefreshingSelection`、
`_isUpdatingDisplaySelection`、`IsUpdatingSelection` 三处重入标志。

### 阶段 4：渲染请求不可变化（后续轮次）

新 record `CharacterRenderRequest`；`ICharacterRenderHost` 三个方法改签名，
门面处一次性投影。唯一公共 API 变更点，需单独编译
`tools\battle-catalog-importer` 验证。

## 3. 每阶段通用收尾

1. `dotnet test SpinePet.sln` 全绿。
2. 终止现有实例 → `pipeline:BuildPublishRun` → `tools\Build.bat`（Release
   0 警告 0 错误）→ `tools\Publish.bat` → `tools\Run.bat` → 日志出现新
   `startup-complete` 且实例持续运行（仅进程存活不算通过）。
3. 更新 `GUI_Design.md` §6、`DeveloperLog.md`、GUI Router 维护计数并同步
   根 Router。
4. git backup commit。

## 4. 风险与回退

- 每阶段独立 backup commit，任一阶段失败整段回退。
- 阶段 1 靠现有测试锚定；阶段 2 靠新增 VM 测试 + 手动冒烟。
- 若阶段 2 某闭包迁移引入回归，允许局部保留原形式并记录，留给阶段 3。

## 5. 回归防控要求（2026-08-24 起对所有 GUI 行为改动生效）

本周连续出现"修 A 坏 B"式回归（下拉标题修复后 Reset All 路径复发空白）。
根因：面板状态由多条路径共享同一套同步机制（面板打开、卡片选中、
Reset All、Scan、换肤、加载完成），单点修复不覆盖兄弟路径。此后每次
GUI 行为改动必须：

1. **测试钉住**：可测层面（Services/ViewModels/规则类）为新行为添加
   回归测试；修改被既有测试钉住的行为时，同步更新断言并说明理由。
2. **兄弟路径清单**：改动涉及"选中状态/动画选项/显隐"任一者时，逐一
   核对以下路径的行为并记录核验结果——面板打开、卡片点击选中、
   Reset All Settings、Scan、换肤、资源加载完成、Normal/Battle 切换。
3. **收尾证据**：DeveloperLog 记录上述清单的核对结论；无法自动化的
   路径标注"待用户冒烟"并告知。
4. 结构性解法是阶段 3 单一状态源（拆除重入标志、所有路径共享同一
   状态推导），优先排期以根除此类回归。
5. **下拉框验收**：不能只断言 ViewModel 字符串。必须验证收起标题、
   `ItemsSource`、`SelectedItem` 三者一致，并覆盖隐藏角色、Show 加载完成、
   Reset All 与连续第二次 Reset。禁止以硬编码选项或 `空 -> 当前值` 的源状态
   抖动作为绑定修复。
