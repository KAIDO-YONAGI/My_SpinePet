# SpinePet GUI 设计说明

> 文档 ID：`GUI-DESIGN`  
> 状态：`Active`  
> 最后核验：`2026-08-22`

本文档记录已实现的 GUI 行为，以及渲染引擎重构必须满足的目标边界。主界面由
WPF 配置面板和独立的原生 Spine 桌面渲染层组成；第 6、7 节中的新组件边界是本次
重构的实施契约，代码验证完成后应与此保持一致。

## 1. 界面结构

| 区域 | 已实现设计 | 主要实现 |
|---|---|---|
| 配置窗口 | 无边框、置顶、可缩放的深色窗口；默认宽 1020，启动时贴齐工作区右上方，高度约为工作区的 60% | `MainWindow.xaml`、`MainWindowLifecycleController.cs` |
| 左侧角色库 | 两列角色卡片，包含自适应缩略图、角色名、显示状态、Show/Hide 和 Position 操作；卡片不显示 Skin 文本 | `MainWindow.xaml`、`CharacterViewModel.cs` |
| 右侧详情栏 | 固定 300 宽，显示当前角色名与 Skin，并提供动画、全局设置、缩放、速度和删除 Skin 等控件 | `MainWindow.xaml`、`CharacterSettingsController.cs` |
| 窗口操作 | 拖动空白区域可移动窗口，边缘可调整大小；Finish Configuration、Alt+F4 或关闭动作会保存状态并隐藏面板 | `MainWindow.xaml.cs`、`MainWindowLifecycleController.cs` |

## 2. 左侧角色库

| 功能 | 具体实现 | 状态 |
|---|---|---|
| 角色卡片 | 按角色名排序并以两列展示；缩略图在 64×64 边界内保持比例，高图或宽图不会撑高卡片 | 已实现 |
| 预览尺寸 | 50%–150% 滑条整体缩放卡片列表，并反向补偿滚动条宽度；数值保存到全局配置 | 已实现 |
| 显示状态 | Hidden、Visible、Loading 三种状态；角色已显示时 Show/Hide 按钮使用白色强调，加载期间禁止重复操作 | 已实现 |
| Show/Hide | 显示或隐藏角色，并把该角色设为右侧详情对象；不会为了按钮操作强制滚动左侧列表 | 已实现 |
| Position | 将角色恢复到默认位置，同时切换右侧详情对象；不会强制滚动左侧列表 | 已实现 |
| 卡片选择 | 点击卡片后更新右侧详情；普通选择会确保条目可见 | 已实现 |
| 滚动跟随 | 用户滚动时，按两列阅读顺序和视口中心更新右侧详情；到达顶部或底部后继续滚轮可逐项切换边界条目 | 已实现 |
| 搜索 | 按角色名、当前 Skin、可用 Skin 编号及资源名进行不区分大小写的多词过滤 | 已实现 |
| 搜索快捷键 | Ctrl+F 聚焦搜索；Down/Enter 进入结果；Esc 清空；Clear 按钮仅在有输入时出现 | 已实现 |
| 键盘操作 | 方向键选择卡片；Enter/Space 切换显示；Shift+F10 打开 Skin 菜单 | 已实现 |
| Skin 切换 | 右键卡片或 Shift+F10 打开可用 Skin 菜单，当前 Skin 带选中状态；切换后复用角色配置并重载资源 | 已实现 |

## 3. 右侧详情与设置

| 功能 | 具体实现 | 状态 |
|---|---|---|
| 当前角色 | 显示所选角色名和当前 Skin；未选择时显示空状态 | 已实现 |
| 动画 | 下拉框列出骨骼动画；选择后立即循环播放并保存为该角色的配置动画 | 已实现 |
| Desktop frame rate | 提供 30、60、120 FPS 三档，标注为 `Global Setting`，立即应用并持久化 | 已实现 |
| Allow dragging | iOS 风格开关，标注为 `Global Setting`；控制渲染模式是否允许拖动全部角色 | 已实现 |
| Scale 基础比例 | 0%–100% 表示基础缩放范围 0–0.2，显示整数百分比 | 已实现 |
| Scale 倍率 | 1.0–5.0 倍纯乘数，独立乘在当前基础比例上，不反向刷新基础比例 | 已实现 |
| Scale 计算 | 最终值为 `基础比例 ÷ 100 × 0.2 × 倍率`，并受角色实际最大缩放限制；两个分量分别持久化 | 已实现 |
| Scale Reset | 恢复默认缩放 0.2，对应 100% 与 1.0 倍 | 已实现 |
| Animation Speed | 0.10–2.00 倍实时调速；Reset 恢复 1.00 倍 | 已实现 |
| Delete Current Skin | 二次确认后把当前 Skin 整个目录移入 Windows 回收站；有其他 Skin 时切换到替代 Skin，最后一个 Skin 删除后移除角色 | 已实现 |

## 4. 桌面角色交互

| 功能 | 具体实现 | 状态 |
|---|---|---|
| 左键点击 | 命中角色且未形成拖动时，按 `action`、`click`、`touch` 等优先名称临时覆盖当前动画；播放结束后从头重放当前配置动画，未配置时重放默认待机动画，保证动作与待机状态完整衔接 | 已实现 |
| 左键拖动 | 全局拖动开关开启后，超过系统拖动阈值即移动角色；释放时提交并保存新位置，动画播放保持连续 | 已实现 |
| 右键角色 | 面板关闭时打开面板、必要时清除搜索，并选中且居中显示对应左侧条目；面板已打开时保存并关闭面板 | 已实现 |
| 配置模式 | 面板开启期间渲染层进入配置模式；完成配置后隐藏面板并恢复桌面交互模式 | 已实现 |

## 5. 资源与应用入口

| 功能 | 具体实现 | 状态 |
|---|---|---|
| Add | 文件选择器接受 `.skel` 或符合命名规则的 UnityFS bundle；导入 standing 资源后尝试自动补齐角色图标 | 已实现 |
| Scan | 扫描 `res`，同步新增、删除和 Skin 变化；可见角色资源改变时尝试即时重载；连续扫描无变化时不保存、不通知也不移除渲染资源 | 已实现 |
| Folder | 创建并打开当前生效的 `res` 资源目录 | 已实现 |
| 托盘菜单 | 双击托盘图标打开面板；菜单提供 Open Panel、Show All、Hide All、Exit | 已实现 |
| 重复启动 | 同一 EXE 目录只保留一个实例并激活已有面板；不同 EXE 目录使用不同互斥锁，可同时运行 | 已实现 |
| 紧急退出 | 全局快捷键 Ctrl+Alt+Shift+F12 请求退出；界面线程失去响应时会强制结束进程 | 已实现 |
| 状态持久化 | 保存角色显隐、位置、缩放分量、用户在 Animation 下拉框选择的常驻动画、速度，以及帧率、拖动和预览尺寸等全局设置；点击等临时动画不覆盖该选择 | 已实现 |

## 6. 实现分层与渲染引擎边界

- `Views/MainWindow.xaml`：布局、控件、绑定、视觉状态和无障碍文本。
- `Views/MainWindow.xaml.cs`：视图装配、事件路由、属性绑定和窗口命中测试。
- `Views/CharacterLibraryController.cs`：搜索、导入、扫描、显隐、Skin 切换和列表同步。
- `Views/CharacterSettingsController.cs`：动画、缩放、速度、位置和 Skin 删除。
- `Views/CharacterPreviewNavigationController.cs`：选择、定位、滚动跟随与边界滚轮规则。
- `Views/CharacterPanelActivationController.cs`：桌面角色右键打开/关闭面板的流程。
- `Services/CharacterResourceCoordinator.cs`：以纯计算方式匹配资源并生成同步差异。
- `Services/CharacterManager.cs`：应用角色状态差异、持久化并发送通知。
- `Services/ConfigNormalizer.cs`、`ConfigFileCommitter.cs`：配置规范化、版本排序和原子磁盘提交。
- `Rendering/Native/NativeCharacterRenderHost.cs`：保持 `ICharacterRenderHost` 的门面，
  负责 Dispatcher 线程边界、生命周期、事件转发和组件装配，不再直接承载全部渲染细节。
- `Rendering/Native/NativeRenderSession.cs`：原生窗口、输入窗口、D3D11/DirectComposition
  资源的初始化、提交和释放。
- `Rendering/Native/NativeCharacterScene.cs`：拥有角色状态集合与 z-order；
  `NativeCharacterLoader.cs` 负责限流、可取消的资源解析和包络计算，安装与释放仍由宿主
  按状态差异执行。
- `Rendering/Native/NativeAnimationController.cs`：常驻动画选择、点击临时动画和模式切换。
- `Rendering/Native/NativeFrameScheduler.cs` 负责帧节拍；
  `NativeFrameRenderer.cs` 负责 Spine 更新、几何/曲面绘制、提交和性能采样，
  二者均不拥有 UI 或配置持久化职责。
- `Rendering/Native/NativePointerController.cs`、`NativeCharacterHitTester.cs`：
  鼠标点击、拖动、右键和几何命中。
- `Rendering/Native/NativeInputRegionCoordinator.cs`、`NativeSilhouetteRasterizer.cs`：
  可点击区域、轮廓缓存、工作区裁剪和屏幕坐标转换。
- `App.xaml.cs`、`TrayIconService.cs`：启动、托盘、单实例激活和退出。

上述新增类型均为 `internal`。依赖方向固定为“门面 → 协调组件 → 原生资源”，
WPF 控件、配置服务和第三方 `SpineRuntime41` 不反向依赖渲染内部组件。公开的
`ICharacterRenderHost`、管理器方法签名、渲染事件和导入冲突语义保持不变。

## 7. 状态协调与生命周期

- 角色显示、隐藏、全部显示/隐藏、配置模式、移除和关闭均先比较目标状态；
  重复请求不再重复调用渲染器、保存配置或发送通知。
- 同一角色与同一资源的并发显示复用同一个加载任务；已经显示且资源未变化时
  直接完成。加载失败或取消不会留下占用状态，后续请求可以正常重试。
- 加载期间发生隐藏、移除或关闭时，以后到达的目标状态为准，加载完成不会
  把角色重新显示出来。
- `CharacterManager.Close()` 只执行一次，先解除全部渲染事件订阅，再关闭
  渲染器；关闭后的渲染回调和重复关闭均为空操作。
- 原生渲染宿主在初始化、显示、隐藏和配置模式切换中执行相同的目标状态判断，
  并保留一次性关闭语义。
- 同一角色同一资源的加载请求使用 single-flight：加载中复用同一任务，已显示且资源
  未变化时直接完成；失败或取消后清理加载占位并允许重试。
- 渲染帧只在渲染线程/Dispatcher 上推进状态。后台线程只负责可取消的资源解析，
  安装前必须再次确认角色版本、目标状态和宿主未关闭。
- 点击临时动画完成后，始终从第 0 帧开始循环当前有效的常驻动画；常驻动画无效时
  从第 0 帧开始循环默认待机动画。不得恢复点击前的旧播放进度。
- 关闭顺序固定为：标记关闭并取消加载/帧调度，解除鼠标和渲染事件订阅，释放角色
  与轮廓缓存，最后释放原生窗口和图形资源。关闭后的回调及重复关闭均为空操作。
- 配置同步与异步保存共用同一原子提交路径；旧版本不能覆盖新版本，相同内容
  不替换磁盘文件，成功提交后统一清除 `RequiresRewrite`。
