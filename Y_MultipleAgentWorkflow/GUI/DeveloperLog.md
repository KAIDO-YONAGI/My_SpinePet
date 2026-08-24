# GUI Developer Log

## 2026-08-24：Reset All 后动画下拉标题空白修复

- 用户反馈：Reset All Settings 后下拉标题回到空白状态。根因与此前
  标题修复不同：`ResetAllSettings` 对未加载（隐藏）角色调用
  `GetAnimationNames` 得到空列表，`SelectIdleAnimationName` 返回 null，
  `ConfiguredAnimation` 被重置为**空字符串**——下拉回退项与标题随之
  消失。该行为在重构前即存在，非阶段 1/2 回归。
- 修复：`CharacterCatalog.ResetAllSettings` 在无法解析 idle 时回退到
  `"idle"` 字面值而非空串；渲染侧按 `idle -> idle* -> 第一动画` 链解析，
  下次加载后收敛为真实 idle。`CharacterManagerTests` 对应断言由
  `Empty` 更新为 `"idle"` 并注明理由。
- 兄弟路径核对（新增回归防控要求）：面板打开/卡片选中/Scan/换肤/
  加载完成/Normal-Battle 切换均不依赖该空值回退，行为不变；换肤后
  加载期间的短暂空白属既有加载表现，加载完成后自动回填。
- 验证证据：Debug 全量测试 `318/318`；Release Build 0 警告 0 错误；
  Publish 成功（`SpinePet-Release-2026-08-24-19 16 49`）；新实例 PID
  3848 于 19:16:59 写入 `startup-complete` 并持续运行。
- 本次实际影响 GUI 批量重置行为，维护计数（新周期）：`0/5 -> 1/5`。

## 2026-08-24：移除 DB（NikkeDB 编号导入）功能

- 按用户要求移除配置面板 DB 按钮（XAML/事件处理器/控制器方法/
  `NikkeDbResourceImportService`/`NikkeDbImportResult`/App 组装与
  `AppPaths.NikkeDbDirectory`），删除专项测试 7 项。
- `CharacterBattleConfigFactoryTests` 原用 DB 导入服务做夹具，改为
  `StageNikkeDbResource` 直接复制 `resources\nikkedb` 真实文件到
  standing/aim/cover 布局；两个真实资源测试断言不变、继续通过。
- 文档同步：`GUI_Design.md` §5 删除 DB 行；`RES-LOAD-GUIDE` 移除
  4.2 DB 流程（审计节升为 4.2、图标为 4.3）并修正核心原则、2.3、
  6.3 的交叉引用（纯文档维护，Resources.Load 不计数）。
- 验证证据：Debug 全量测试 `318/318`（原 325 减去 7 项 DB 服务
  测试）；Release Build 0 警告 0 错误；Publish 成功；新实例写入
  `startup-complete` 并持续运行。
- 本次实际影响 GUI 资源入口，维护计数：`4/5 -> 5/5`。按规则复查本
  周期 5 项任务（重构阶段 1、重构阶段 2、面板真实状态同步、下拉
  标题差量更新、DB 移除）：`GUI_Design.md` 各节均已在对应任务中同步，
  §1–§9 与当前实现一致，其余记录 `reviewed-no-change`，计数归零：
  `5/5 -> 0/5`。

## 2026-08-24：动画下拉标题始终显示当前生效值

- 用户反馈：Animation 下拉收起标题空白，应显示默认配置动画（如
  `idle`），更改后也应显示更改值。根因：`RefreshDisplaySelectionOptions`
  每次同步对选项集合 `Clear()` 重灌，WPF ComboBox 在 ItemsSource 清空
  瞬间把选中项重置为空，标题闪失后不再恢复。
- 修复：选项列表改为差量更新（按目标顺序插入缺失项、移除陈旧项），
  不清空仍有效的条目；当前选中值只要仍在列表中，ComboBox 选择与标题
  全程保持，默认态与更改后均稳定显示。
- 新增 `MainViewModelTests` 2 项：差量重建保留当前选择、完整替换陈旧
  条目。
- 验证证据：Debug 全量测试 `325/325`；Release Build 0 警告 0 错误；
  Publish 成功（`SpinePet-Release-2026-08-24-19 08 06`）；新实例 PID
  18916 于 19:08:16 写入 `startup-complete` 并持续运行。
- 本次实际影响 GUI 详情面板选择框行为，维护计数：`3/5 -> 4/5`。

## 2026-08-24：面板打开与加载完成后自动同步真实配置状态

- 用户反馈两个问题：Normal 模式右侧下拉只剩 `idle`；面板默认空白，需要
  手动点选才能填充。根因相同：右侧详情只在用户点选时同步——启动时
  `RefreshCharacterList` 发生在角色渲染加载完成之前（动画名只有
  `ConfiguredAnimation` 回退值），之后加载完成的快照事件只更新卡片
  ViewModel，没有人再刷新选中角色的下拉选项；面板打开时也没有任何
  重新同步。该行为在重构阶段 1/2 之前即存在，非回归。
- 修复：`MainWindowLifecycleController` 新增 `ConfigModeEntered` 事件
  （`SwitchToConfigMode` 显示面板后触发），MainWindow 订阅并执行
  `RefreshCharacterList` 重建真实状态；`CharacterLibraryController.
  UpdateCharacterState` 在选中角色的动画名或配置动画变化时（典型：
  加载完成）自动调用 `SyncSelectedCharacterSettings` 刷新右侧选项。
- 隐含收益：面板打开即显示真实运行时 Mode（Battle 桌面交互后的状态），
  无需先点卡片。
- 验证证据：Debug 全量测试 `323/323`；Release Build 0 警告 0 错误；
  Publish 成功（`SpinePet-Release-2026-08-24-19 01 54`）；新实例 PID
  30604 于 19:02:05 写入 `startup-complete` 并持续运行。
- 本次实际影响 GUI 面板状态同步行为，维护计数：`2/5 -> 3/5`。

## 2026-08-24：提取 MainViewModel、MainWindow 瘦身为纯视图（重构阶段 2）

- 按 `GUI_Refactor_Plan.md` 阶段 2 执行，行为零变化、XAML 零改动
  （49 处 DataContext 绑定全部为直接属性名）。
- 新建 `ViewModels/MainViewModel.cs`（约 700 行）：实现
  `ICharacterSettingsHost`，迁入 MainWindow 全部 29 个 INPC 状态、选项集、
  派生显示与搜索锚定；setter 副作用改四个事件出口：
  `ScaleComponentsChanged`（缩放提交）、`SettingsSyncRequested`（设置
  同步）、`SearchFilterRequested`（过滤刷新）、`ThumbnailScaleApplied`
  （缩略图布局）。`ICharacterSettingsHost` 与 `CharacterSettingsDefaults`
  移至 ViewModels（依赖方向：Views → ViewModels）。
- `MainWindow.xaml.cs` 从 1340 行瘦身至约 700 行：`DataContext =
  MainViewModel`，只留视图职责（命名元素装配、事件路由、WM_NCHITTEST
  边缘命中测试、DragMove、拖动开关动画、UIA 搜索播报、生命周期转发）。
- 控制器重连：`CharacterLibraryController` 构造参数 18→13（选中状态与
  过滤通知改经 VM）；`CharacterPreviewNavigationController` 的视图与
  选中委托换 VM；`CharacterPanelActivationController` 的清空搜索换
  `vm.ClearSearch()`；`MainWindowLifecycleController` 不变。
- 新增 `MainViewModelTests` 18 项（搜索锚定恢复、DisplayMode 联动、
  缩放分量钳制与提交事件、全局设置写透持久化、运行时战斗态应用），
  补上 Views 状态层不可测的缺口。
- 验证证据：Debug 全量测试 `323/323`；Release Build 0 警告 0 错误；
  Publish 成功（`SpinePet-Release-2026-08-24-18 46 20`）；新实例 PID
  31088 于 18:46:30 写入 `startup-complete`，持续运行且日志无错误行。
- 本次实际影响 GUI 视图层结构，维护计数：`1/5 -> 2/5`。

## 2026-08-24：CharacterManager 按职责拆分为组合门面（重构阶段 1）

- 按 `GUI_Refactor_Plan.md` 阶段 1 执行，行为零变化、公共 API 冻结：
  `CharacterManager`（1358 行）收缩为组合门面（约 430 行），公共
  6 属性 + 20 方法 + 6 事件与 internal 4 参构造全部保留为转发语义；
  渲染事件订阅/退订与回写留在门面，Close 仍保证 7 个渲染事件全部退订。
- 新增 internal 组件：`CharacterCatalog`（配置仓储——增删改、换肤回滚、
  资源同步、全局设置、持久化与 `CharactersChanged`）、
  `CharacterShowCoordinator`（显隐 single-flight——同资源复用、异资源
  排队、完成后持久化）、`BattleInteractionController`（Normal/Battle
  运行时状态机与右键长按/短按交互）。
- 新增 `BattleInteractionControllerTests`（7 项）与
  `CharacterShowCoordinatorTests`（7 项）；原 `CharacterManagerTests`
  29 项断言未改动。
- 验证证据：Debug 全量测试 `305/305`；Release Build 0 警告 0 错误；
  Publish 成功（`SpinePet-Release-2026-08-24-18 35 57`）；新实例 PID
  16456 于 18:36:12 写入 `startup-complete` 并持续运行。
- 本次实际影响 GUI 服务层结构，维护计数：`0/5 -> 1/5`。

## 2026-08-24：角色库滚轮逐项选择重做（wheel-picker）

- 根因：滚轮自由滚动一整行（128px=双列 2 项），选择由已实例化容器几何
  （`TranslatePoint`）+ 视口中线推导；边界滚轮 ±1 步进与 pending 队列绕过
  中线计算后，下一次 `ScrollChanged` 把选择吸回视口几何中线，划出边界时
  跳过 4-5 个条目；快速滚动时容器实现滞后使选择只在单列间跳变。
- 重做为选择驱动滚动：每格滚轮按两列阅读顺序严格 ±1 项（delta 按 120
  累积，快速滚动合并为整数步长，钳制 `[0, count-1]`），把选中条目槽位中心
  （`i×H/2 + H/4`）对齐视口中线，头尾钳制到 `0/max` 不跳变；滚轮一律
  `e.Handled`，面板不再自由滚动。
- 外部滚动（滚动条拖动等）的反向跟随改为纯算术槽位公式；删除
  `GetItemGeometries`、`FindCenterItemIndex`、`GetTraversalCenter`、
  `FindBoundaryWheelSelectionIndex`、pending 队列与 `GetCenteredVerticalOffset`
  全部几何/边界旧路径。点击/搜索/键盘 Reveal 居中统一复用同一算术居中；
  自滚动用 Background 优先级清除的抑制标记，防止跟随与居中在钳制区打架。
- 验证证据：`PreviewNavigationCoordinatorTests` 重写后全量 `291/291` 通过；
  Release Build 0 警告 0 错误，Publish 成功；新实例 PID 20116 启动并持续
  运行。
- 本次实际影响 GUI 角色库滚轮交互，维护计数：`4/5 -> 5/5`。按规则复查
  本周期 GUI 任务证据与 `GUI_Design.md`：§2 卡片/缩略图/搜索/键盘与
  §4 点击动画恢复均与当前实现一致，本次已同步「滚动跟随」行，其余记录
  `reviewed-no-change`，计数归零：`5/5 -> 0/5`。

## 2026-08-24（上海机器时间）：预览列表 recycling 回归修复

- 修复 `VirtualizingUniformGrid` 在 recycling 模式下遗漏复用容器的问题：
  WPF 可能返回已脱离视觉树但 `newlyRealized` 为 false 的容器，面板现在会按
  实际挂载状态重新插入，避免往返滚动后预览条目逐批消失。
- 新增复用容器挂载条件测试；Debug 全量测试 `287/287`，Release Build
  0 警告、0 错误，Publish 成功。
- 新实例 PID 33180 持续响应，上海机器时间 `2026-08-24 05:37:29.298`
  写入 `startup-complete`；真实配置栏连续 8 轮滚到底并返回顶部，已实例化
  条目数稳定为底部 11、顶部 14，没有再降为 0。
- 本次实际影响 GUI 角色库虚拟化，维护计数：`3/5 -> 4/5`。

## 2026-08-24：GUI 与原生渲染线程解耦

- 新增独立 STA 原生渲染线程，由该线程创建并独占原生窗口、D3D 设备、场景、
  Surface、帧调度器和纹理生命周期；WPF Dispatcher 不再执行资源安装、首次
  纹理上传、mipmap 生成或持续帧循环。
- `NativeCharacterRenderHost` 收敛为 UI 门面，以不可变
  `CharacterRenderSnapshot` 提供无阻塞状态查询；语义命令保持 FIFO，高频缩放、
  速度、位置和帧率命令按属性合并为 latest-wins。资源状态切换改为等待渲染线程
  确认的异步结果，现有结构化 `BattleEffects` 参数保持不变。
- 加载、显隐和动画状态按角色合并后投递回 WPF Dispatcher；
  `CharacterLibraryController` 只增量更新对应卡片，角色集合或排序变化时才执行
  全量刷新。Show 后立即 Hide/Remove、重复 Show、失败及关闭竞态继续由角色
  generation 和取消状态阻止迟到结果复活角色。
- 缩略图改为最多两个后台解码任务，按规范化路径、修改时间和文件长度缓存，
  缓存最多 256 项；解码结果缩放并 `Freeze()` 后绑定。角色列表改为固定两列、
  recycling 的 `VirtualizingUniformGrid`，只实现可视行和一行缓冲区。
- 验证证据：针对性测试 `34/34`、Debug 全量测试 `284/284`；Release Build
  0 警告、0 错误，Publish 成功，Run 写入新的 `startup-complete`，PID 29088
  持续响应。
- 本次实际影响 GUI 渲染线程边界、角色库刷新和缩略图加载，维护计数：
  `2/5 -> 3/5`。

## 2026-08-24：特殊混合渲染管线与右键过曝修复

- 原生帧提交改为按 Spine draw order 生成有序计划，每个角色每帧统一上传一次
  几何；D3D11 管线、四种混合状态和可增长顶点/索引缓冲区由设备复用，并去重
  实际未变化的纹理与混合状态绑定。
- 混合配置拆分 RGB 与 Alpha 因子。`Additive` 同时累加预乘颜色和资源源 Alpha，
  保证 DirectComposition 表面持续满足 `RGB <= Alpha`，修复发光附件叠加后角色
  过曝并半透明；Normal、Multiply、Screen 及资源作者制作的淡出、透明和发光保留。
- 右键开启或关闭角色配置面板只切换交互模式并取消指针捕获，不再重选或重启
  当前动画轨。点击临时动画结束时重新解析完成瞬间的资源状态与默认动画，通过
  generation 只从第 0 帧恢复一次。
- standing、aim、cover 的已加载资源共同参与纹理存活判断；缓存真正丢失时允许
  从源贴图重新解码上传。本轮未改变 Aim 附加轨的 BattleEffects 播放配置语义。
- 验证证据：定向测试 `25/25`、动画与 Battle 回归 `37/37`、全量测试
  `272/272`；Release Build 0 警告、0 错误，Publish 成功，Run 在机器时间
  `2026-08-24 04:35:20` 写入新的 `startup-complete`，PID 28564 持续响应。
  任务日期仍按 `2026-08-23` 记录。
- 本次实际影响 GUI 原生渲染、动画交互和生命周期，维护计数：
  `1/5 -> 2/5`。

## 2026-08-24：修复 Battle 返回 Normal 的状态分裂

- `ICharacterRenderHost.SetCharacterResourceState` 改为返回实际切换结果；
  管理器仅在资源槽交换成功后提交 Normal/Battle 与 Cover/Aim 运行时状态。
- 视图切换失败或异常时重新同步最后一个已渲染状态，不再出现下拉框已回
  Normal、模型仍在 Battle 的虚假选择。
- 返回 Normal 时清除临时轨并从第 0 帧循环配置动画；无效配置回退 standing
  idle，避免资源交换后停在空轨或一次性动画末帧。
- 全量测试 `251/251`；Release Build 0 警告、0 错误，Publish 成功，
  Run 出现新的 `startup-complete` 且实例持续响应。
- 本次实际影响 GUI 状态协调，维护计数：`0/5 -> 1/5`。

## 2026-08-24：Aim/Cover 控件、输入与周期复核

- 右侧详情新增显式 Normal/Battle 模式选择，并在原动画下拉框右侧平行增加
  Cover/Aim 下拉框；Battle 仅对完整配置启用，默认 Cover。
- Battle 中右键按住 300ms 进入 Aim 并连续开火，释放或捕获丢失后回 Cover
  并换弹；短右键面板操作与左键点击、拖动保持原行为。
- 模式和战斗状态为运行时状态，启动、首次展示与重启仍从 Normal idle 开始。
- 完整资源重查后，实际配置中 63 个角色有 43 个可手动进入 Battle，
  其余角色仅显示 Normal，不生成无效 Battle 入口。
- 验证证据：全量测试 `240/240`；Release Build 0 警告、0 错误，
  Publish 成功，Run 出现 `startup-complete` 且新实例持续响应。
- 本任务使维护计数 `4/5 -> 5/5`。随后对照 GUI Design、视图绑定、管理器
  输入状态机和原生捕获释放路径完成周期复核，文档已同步，无遗留旧口径，
  计数按规则归零为 `0/5`。

## 2026-08-22：原生渲染管线热路径去重

- 帧调度器改为最多保留一个 Dispatcher 待执行帧；渲染繁忙时丢弃过期节拍，
  不再在前一帧完成后立即补跑积压帧，动画仍按真实经过时间推进。
- `NativeSpineGeometry` 在复制顶点时同步累计边界，帧渲染与资源包络计算复用
  该结果；移除热路径中的第二次 Draw Batch 顶点扫描。
- `NativeCompositionSurface` 缓存锚点位置，位置未变时跳过 DirectComposition
  offset 写入；交换链重建后强制失效并重新定位。
- 输入区域改为按可见角色集合、轮廓时间戳、工作区变化和拖拽结束驱动重建；
  静止帧不再重复聚合、裁剪和排序区域，原有 100ms 动画轮廓刷新保持不变。
- 新增 6 项管线效率测试；目标测试 `29/29`、全量测试 `220/220`；Release Build
  0 警告、0 错误，Publish 成功，Run 后出现 `startup-complete`，新实例持续响应。
- 同一角色、120 FPS 稳定遥测中，平均帧耗时由约 `0.60–0.65ms` 降至
  `0.53–0.57ms`，每帧分配由约 `3.5–3.9KB` 降至 `0.39–0.42KB`。
- 本次实际影响 GUI 原生渲染效率与调度边界，维护计数：`2/5 -> 3/5`。

## 2026-08-22：原生渲染引擎职责拆分

- 将 `NativeCharacterRenderHost` 收敛为外观、调度、生命周期和事件出口，
  角色集合、加载、动画、命中、指针、输入区域、帧调度与帧绘制分别下沉到
  `internal` 组件，现有公开接口保持不变。
- 加载器按同一角色与资源建立 single-flight；宿主对重复显示、隐藏、模式
  切换和关闭执行目标状态判断，关闭后回调与重复关闭均为空操作。
- 点击动画结束后排队从第 0 帧循环播放当前配置动画；未配置或无效时按
  `idle -> idle* -> 第一动画` 选择默认待机，不再续播点击前进度。
- 输入区域会在隐藏、移除、资源切换和加载失败后同步清空，避免透明窗口残留
  旧角色的可交互区域。
- 验证证据：原生渲染目标测试 `31/31`、全量测试 `213/213`；Release Build
  0 警告、0 错误，Publish 成功，Run 后出现 `startup-complete`，新实例
  持续运行并正常响应。
- 本次实际影响 GUI 渲染架构、动画交互和生命周期，维护计数：
  `1/5 -> 2/5`。

## 2026-08-22：角色状态幂等与点击动画衔接

- 将资源匹配和同步差异计算提取到内部 `CharacterResourceCoordinator`，
  `CharacterManager` 只应用差异、持久化和通知。
- 显示、隐藏、全部显示/隐藏、配置模式、移除和关闭均按目标状态去重；
  同一角色并发显示复用加载任务，失败或取消后可重试。
- 关闭时先解除渲染事件订阅，再一次性关闭渲染器；加载期间的隐藏、移除和
  关闭不会被迟到的加载完成结果反转。
- 点击动画结束后从头重放当前配置动画；未配置时重放默认待机动画，避免恢复
  到点击前进度造成衔接不完整。
- 配置规范化与原子提交拆分，同步和异步保存共享版本有序的磁盘提交路径。
- 验证证据：全量测试 `210/210`，Release Build 0 警告、0 错误，
  Publish 成功；Run 按验证指南补齐子进程 `WINDIR` 后出现
  `startup-complete`，新实例持续运行。
- 本次实际影响 GUI 状态协调、动画与生命周期，维护计数：`0/5 -> 1/5`。

## 2026-08-22：维护周期提前复核并清零

- 按用户要求提前复核当前 `1/5` 周期，逐项对照动画交互日志、
  `CharacterSettingsController`、`NativeCharacterRenderHost` 和临时动画测试。
- 确认只有用户从 `Animation` 下拉框选择时才修改常驻动画；点击动画保存并
  恢复播放前状态，连续点击和用户中途覆盖的边界均已写入 GUI Design。
- 权威设计无需新增能力说明，仅同步最后核验日期。
- 复核结论：文档与当前实现一致，维护计数：`1/5 -> 0/5`。

## 2026-08-22：临时动画恢复用户常驻状态

- `Animation` 下拉框仍是常驻动画状态的唯一修改入口；临时点击动画不再
  覆盖 `ConfiguredAnimation`。
- 播放点击动画前保存当前动画名、循环设置和播放进度，播放结束后恢复；
  连续点击保留最初的恢复目标，用户中途选择动画则覆盖旧的临时队列。
- 关键实现位于 `NativeTemporaryAnimationPlayback`、
  `NativeCharacterRenderHost`、`NativeCharacterState` 和对应动画测试。
- 验证证据：目标测试 `9/9`、全量测试 `197/197`；Release Build 为
  0 警告、0 错误，Publish/Run 通过且 `SpinePet.exe` 持续运行。
- 本次实际影响 GUI 动画选择和点击交互，维护计数：`0/5 -> 1/5`。

## 2026-08-20：双列预览导航与职责重构

- 双列列表改用真实卡片几何和视口中线，补齐顶部首项与底部末项边界。
- 详情选择、搜索聚焦和桌宠右键定位统一进入预览导航控制器。
- 位置按钮移到卡片操作区；资源库、设置、生命周期和面板激活拆为非
  `partial` 控制器。
- 当日验证：Release 0 警告 0 错误；目标测试 20/20。全量 155/159，
  4 项为符号链接权限、字体缓存和既有 56px 图标问题。

## 2026-08-21：启动入口修复

- 发现移除 `App.xaml` 的 `x:Class` 后生成了裸 WPF Application，进程存活但
  从未进入 `App.OnStartup`。
- 改为显式 `Program.cs [STAThread]` 创建 `App`，增加三级启动日志和入口点测试。
- 验证窗口句柄、`startup-complete`、渲染初始化、单实例激活和 Run.bat。
- 将烟囱标准改为必须观察到 `startup-complete`，仅进程存活不算成功。

## 2026-08-21：渲染性能修复

- 对轮廓栅格化增加 100ms 缓存刷新，并在位移或缩放变化时立即刷新。
- 裁剪路径改为复用列表，移除每帧 LINQ 数组分配。
- 单宠物 60fps 实测：平均帧时约 2.42ms 降至 0.67ms，每帧分配约
  66KB 降至 11KB，进程 CPU 约 1.3% 降至 0.26%。
- Release 构建通过；新增 5 项节流测试通过，右键命中端到端验证通过。

以上为迁移前历史证据，不增加新维护周期计数。
