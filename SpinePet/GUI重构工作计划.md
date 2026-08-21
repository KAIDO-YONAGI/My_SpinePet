# SpinePet GUI 重构工作计划

> 维护工作记忆：继续本任务前先阅读本文件的“现状与约束”和“工作记录”。
> 最后更新：2026-08-20

## 目标

1. 预览栏鼠标滚轮跟随必须按双列行优先顺序处理：从左到右、从上到下，并以预览可视区域垂直中线为基准。
2. 滚动到上边界时可以选中首卡，滚动到下边界时可以选中末卡，避免首尾卡片因中线计算而永远切不到。
3. 右侧详情选中角色时，左侧预览列表必须滚动到对应卡片，并在程序滚动结束后保持该选择。
4. 从桌宠右键打开配置面板时，必须同时选中对应角色并滚动左侧列表到对应预览卡。
5. 位置重置按钮位于每张预览卡的 `Show/Hide` 下方，详情标题栏释放给角色名。
6. GUI 按职责拆成非 `partial` 控制器；不使用 `MainWindow` 分部文件隐藏跨职责状态。
7. 事件入口、程序滚动会话、过滤刷新和重复操作具备幂等保护。
8. 不在工作区留下本次重构专用的临时脚本或生成物。

## 现状与约束

- 项目位于 `D:\SpineTools\SpinePet`，使用 WPF、.NET 9 和 WinForms 互操作。
- 左侧 `CharacterCards` 是 `ListBox + UniformGrid Columns="2"`，
  `ScrollViewer.CanContentScroll="False"`，可以读取像素级滚动位置。
- WPF 原本通过 `x:Class` 生成代码隐藏类。为满足禁止 `partial` 的要求，当前 XAML
  不再声明 `x:Class`，由 `MainWindow.LoadView()` 通过 `Application.LoadComponent`
  载入 BAML，所有事件在窗口壳层中集中绑定。
- 根工作区存在用户未提交修改：`.gitignore` 和已删除的 `prompts.txt`；
  本任务不触碰这些文件。
- 当前机器的 WPF 无头环境可能在 `System.Windows.Window` 初始化时触发
  `MS.Internal.FontCache.Util -> UriFormatException`，该环境问题不能作为产品代码
  的功能验证依据。

## 目标架构

### 1. `MainWindow`

只负责：

- WPF 窗口生命周期桥接和运行时加载视图；
- 数据绑定属性、命令声明和薄事件转发；
- 将 UI 控件事件交给控制器；
- 保留 `SelectedCharacter` 等对外绑定状态。

`MainWindow.xaml.cs` 是唯一窗口代码文件，但不使用 `partial`。旧的
`MainWindow.CharacterLibrary.cs`、`MainWindow.SelectionSettings.cs` 已删除。

### 2. `CharacterPreviewNavigationController`

只负责 WPF 预览列表适配：

- 读取 `ListBoxItem` 的布局矩形；
- 找到内部 `ScrollViewer`；
- 调用纯规则决定滚轮跟随项；
- 执行详情选择、搜索结果和右键选择后的 `ScrollIntoView`；
- 用 `PreviewNavigationCoordinator` 管理程序滚动会话，防止滚轮事件抢回选择。

### 3. `PreviewNavigationCoordinator`

只包含可脱离 WPF 测试的规则和状态：

- `FindBoundaryItemIndex`：顶部命中 `0`，底部命中 `itemCount - 1`；
- `FindCenterItemIndex`：仅处理中间滚动位置，使用可视区垂直中线；
- 双列同一行通过虚拟中线偏移按左、右顺序稳定命中；
- `GetCenteredVerticalOffset`：把详情目标卡居中并限制在滚动边界；
- 重复 `BeginReveal`、完成、取消和滚轮选择不会留下悬挂状态。

### 4. `CharacterLibraryController`

负责资源库职责：

- 资源发现、同步、刷新角色列表和过滤；
- 搜索结果聚焦；
- Spine/UnityFS 导入、资源扫描、打开资源目录；
- 皮肤切换；
- 卡片可见性切换和重复操作锁。

### 5. `CharacterSettingsController`

负责选择后的设置写回：

- 缩放、动画速度和动画选择；
- 缩放/速度/位置重置；
- 皮肤删除和替代皮肤恢复；
- 选中角色设置同步；
- 通过 `ICharacterSettingsHost` 访问窗口绑定状态，避免反向依赖窗口实现细节。

### 6. `MainWindowLifecycleController`

负责配置面板生命周期：

- 打开/隐藏配置面板；
- 加载尺寸恢复；
- 关闭拦截、状态保存和幂等释放；
- 应用程序退出时的取消令牌。

### 7. `CharacterPanelActivationController`

负责桌宠右键打开配置面板的流程编排：

- 面板已打开时执行关闭；
- 面板未打开时解析角色；
- 角色被当前搜索过滤隐藏时先清空过滤；
- 按“打开面板 -> 选中并定位预览卡”的固定顺序执行；
- 流程不直接依赖 WPF 控件，可以用纯单元测试验证调用顺序。

## 实施步骤

- [x] 调研项目入口、XAML、事件入口、资源库和渲染主机调用链。
- [x] 创建本计划文档并记录环境约束。
- [x] 重写纯 C# 预览中线规则。
- [x] 增加顶部/底部边界规则，覆盖首卡、末卡和不可滚动列表。
- [x] 将详情定位、搜索结果定位和右键定位统一接入预览导航控制器。
- [x] 将位置按钮移动到卡片 `Show/Hide` 下方。
- [x] 删除旧的 `MainWindow` 功能分部文件和旧滚动实现。
- [x] 建立资源库、设置、生命周期三个非 `partial` 控制器。
- [x] 抽取右键打开面板的激活流程控制器。
- [x] 改为运行时载入 XAML，并把所有 WPF 事件绑定集中到窗口壳层。
- [x] 增加滚动规则、布局和“无 partial 分部文件”回归测试。
- [x] 完成全量测试并记录已知环境/资源失败。
- [x] 完成最终静态审计：无 `partial`、无旧方法、无本次临时脚本。

## 验收标准

### 功能

- 双列滚动选择顺序可覆盖左上、右上、左下、右下等条目，不固定停留在左列。
- 选择基准是预览可视区域的垂直中线。
- 在滚动顶部选择首卡，在滚动底部选择末卡。
- 详情选择、搜索结果聚焦、右键打开配置都能让左侧对应卡片可见并保持选中。
- 位置重置按钮显示在每张卡的 `Show/Hide` 下方，详情标题不再被按钮挤压。

### 架构

- `Views` 目录不包含 `MainWindow.*.cs` 功能分部文件。
- 源码不使用 `partial`。
- `MainWindow` 不直接实现资源导入、资源同步、皮肤删除、预览几何计算等业务。
- 重复事件、重复刷新、空列表和程序滚动期间的滚轮输入不会导致状态漂移。

### 验证

- Release 构建为 0 警告、0 错误。
- 预览规则和布局目标测试通过。
- 全量测试结果如实记录；外部环境或缺失资源导致的失败单独列出。

## 工作记录

### 2026-08-20：调研与第一轮交互重做

- 确认 `CharacterCards` 是双列像素滚动列表，旧实现使用 `row * 2`，
  只能选每行左列。
- 新增 `PreviewNavigationCoordinator`，使用真实卡片几何、中线和双列虚拟偏移。
- 删除旧的行号计算、重复滚动订阅和旧的延迟抑制思路。
- 位置重置按钮移动到卡片操作区。

### 2026-08-20：首尾边界和右键定位

- 新增 `PreviewNavigationRules.FindBoundaryItemIndex`：
  - `VerticalOffset` 位于顶部容差内时返回首项；
  - 位于底部容差内时返回末项；
  - 内容不可滚动时优先保持当前合法选择。
- `CharacterPreviewNavigationController` 先处理边界，再处理中线选择。
- 右键处理顺序改为：清除不匹配过滤 -> 打开配置面板 -> `SelectAndReveal`，
  因此会同时更新 `CharacterCards.SelectedItem` 和左侧滚动位置。
- 目标测试扩展到 16 项并通过。

### 2026-08-20：职责重构

- 新增：
  - `Views/CharacterLibraryController.cs`
  - `Views/CharacterSettingsController.cs`
  - `Views/CharacterSettingsDefaults.cs`
  - `Views/MainWindowLifecycleController.cs`
  - `Views/CharacterPreviewNavigationController.cs`
- 删除：
  - `Views/MainWindow.CharacterLibrary.cs`
  - `Views/MainWindow.SelectionSettings.cs`
- `MainWindow.xaml.cs` 改为无 `partial` 的窗口壳层。
- `MainWindow.xaml` 删除 `x:Class` 和事件属性，改由壳层通过命名控件和路由事件集中绑定。
- `ICharacterSettingsHost` 把设置控制器与窗口绑定状态隔离。
- `CharacterPanelActivationController` 把右键流程从窗口事件中移出，
  并覆盖过滤清除、打开、选中定位、已打开关闭和未知角色分支。
- 资源库、设置和生命周期控制器均不继承窗口、不使用窗口分部状态。

### 2026-08-20：当前验证记录

- Release 构建：通过，0 警告，0 错误。
- 目标测试：
  `PreviewNavigationCoordinatorTests` + `CharacterLibraryLayoutTests` +
  `CharacterPanelActivationControllerTests`，20/20 通过。
- 一次构建曾被旧的 `SpinePet` 进程锁定 DLL；确认进程路径属于本项目后结束，
  后续构建正常。
- 全量测试：155 通过、4 失败、0 跳过、总计 159：
  - `CharacterResourceStorageServiceTests.RecycleSkinDirectoryRejectsNestedReparsePoint`
    和 `UnityBundleImportServiceTests.ImportSkeletonRejectsNestedTargetReparsePoint`
    因当前账户没有创建 Windows 符号链接所需权限而失败；
  - `ConfigServiceTests.NormalizeRepairsNullFieldsDuplicateIdsAndNonFiniteValues`
    因当前机器的 WPF 字体缓存初始化触发 `UriFormatException` 而失败；
  - `BrandAssetTests.WindowsIconContainsEverySupportedSize`
    因现有 `SpinePet.ico` 缺少 56px 图标而失败。
- Release 启动烟囱检查：进程成功保持运行超过 3 秒，随后由验证命令结束；
  未观察到本次运行时 XAML 载入路径的立即退出。
- 静态审计：`src/SpinePet` 中无 `partial`、无旧滚动算法、无旧位置处理器、
  无 `MainWindow.CharacterLibrary.cs` / `MainWindow.SelectionSettings.cs`；
  工作区没有新增本次重构专用脚本。
- 右键流程补充为独立的 `CharacterPanelActivationController`，
  新增 4 个分支测试，目标测试总数提升到 20 项并全部通过。

### 2026-08-21：启动空壳修复（Run.bat 无窗口）

- 现象：双击 `tools\Run.bat` 后无任何窗口，残留多个 SpinePet.exe 且今天日志零新增。
- 根因：职责重构时 `App.xaml` 被删掉了 `x:Class="SpinePet.App"`。没有 `x:Class` 时
  WPF 生成的入口创建的是裸 `System.Windows.Application` 并直接进入消息循环，
  `App.OnStartup`（单实例互斥体、主窗口、托盘、日志）从未执行；
  XAML 里的 `ShutdownMode="OnExplicitShutdown"` 照常生效，空壳进程因此永不退出。
  重构当时的烟囱检查只验证"进程存活 3 秒"，把空壳误判为启动成功。
- 取证：dotnet-stack 显示主线程停在 `GeneratedApplication.Main →
  Application.RunInternal → GetMessageW`；dotnet-dump 确认托管堆上没有
  `SpinePet.App` 实例（MethodTable 未加载）。10:51–10:55 的 5 个残留进程
  均为该空壳，已全部结束。
- 改动：
  - 删除 `App.xaml`（其资源为空，ShutdownMode 迁入代码）。
  - 新增 `Program.cs`：显式 `[STAThread]` 入口创建 `App` 并 `Run()`，维持无 `partial`。
  - `App` 构造函数设置 `ShutdownMode.OnExplicitShutdown`。
  - 启动可观测性：`app-static-constructed` / `startup-begin` / `startup-complete`
    三级日志，用于区分"进程未进入托管启动"与"启动中途卡住"。
  - 新增 `AppEntryPointTests`：断言程序集入口点为 `SpinePet.Program`，
    防止入口再次被 XAML 生成代码劫持成裸 Application。
- 验证：
  - Release 构建：0 警告 0 错误。
  - 全量测试：157 通过、3 失败（既有环境问题：2 个符号链接权限、1 个 56px 图标缺失）；
    上次失败的 `ConfigServiceTests` 字体缓存项本次未复现。
  - 启动验证：窗口句柄非零、`startup-complete` 落盘、渲染管线完成初始化；
    第二实例被 `duplicate-instance-blocked` 拦截并激活首实例窗口后自行退出；
    `tools\Run.bat` 复测同样成功。
- 烟囱验证标准（替代旧标准）：启动成功必须等到日志出现
  `[App] startup-complete`（带超时）；仅凭"进程存活"一律视为未验证。

### 2026-08-21：渲染性能修复（栅格化节流 + 免分配裁剪）

- 背景：空闲 CPU 可达 10%。排查结论：日志非主因（空闲约 1 行/分钟，
  性能遥测默认关闭）；主因是 `UpdateWindowRegions` 每帧全量执行
  `RasterizeSilhouette`（成本随角色屏幕面积线性增长）× 高帧率档位
  （60/120fps），多宠物再线性叠加。
- 改动：
  - `NativeCharacterState` 新增轮廓缓存字段（runs 列表、锚点、缩放、时间戳）。
  - `UpdateWindowRegions` 栅格化节流：`SilhouetteRefreshInterval` 100ms；
    锚点位移 ≥2px（`SilhouetteAnchorEpsilonPixels`）或缩放变化立即刷新
    （拖拽时仍每帧更新）；每帧只做缓存拼装。注意 `RasterizeSilhouette`
    返回共享 scratch 列表，缓存前必须复制（`CopySilhouetteRuns`）。
  - `ClipToWorkingAreas` 从每 run 的 LINQ `ToArray` 改为向预分配列表追加，
    消除每帧 14KB+ 的 GC 分配。
- 实测（同一宠物、60fps、SPINEPET_PERF_LOG=1）：
  - avg-frame-ms：2.42 → 0.67（-72%）；
  - allocated-bytes-per-frame：约 66KB → 约 11KB（-83%，剩余来自 spine 更新）；
  - 进程 CPU（单核口径）：1.3% → 0.26%。
- 验证：Release 构建 0 警告 0 错误；全量测试 162 通过 + 3 个既有环境失败
  （符号链接权限 ×2、56px 图标），新增 `NativeSilhouetteRefreshTests`
  5 项节流决策测试通过；端到端右键命中角色成功弹出配置面板，
  确认节流后输入区域仍工作。
- 遗留：多宠物/大缩放场景成本仍随面积线性增长（栅格化本身未变，
  只是降到 10Hz）；剩余每帧约 11KB 分配来自 spine 动画更新路径。

### 后续记录格式

- 日期：
- 改动：
- 验证：
- 结果/遗留风险：
