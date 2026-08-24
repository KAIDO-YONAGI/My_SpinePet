# SpinePet GUI 设计说明

> 文档 ID：`GUI-DESIGN`  
> 状态：`Active`  
> 最后核验：`2026-08-24`

本文档记录已实现的 GUI 行为和渲染引擎边界。主界面由 WPF 配置面板和独立的
原生 Spine 桌面渲染层组成；第 6 至 9 节记录当前实现必须持续满足的内部契约。

## 1. 界面结构

| 区域 | 已实现设计 | 主要实现 |
|---|---|---|
| 配置窗口 | 无边框、置顶、可缩放的深色窗口；默认宽 1020，启动时贴齐工作区右上方，高度约为工作区的 60% | `MainWindow.xaml`、`MainWindowLifecycleController.cs` |
| 左侧角色库 | 两列角色卡片，包含自适应缩略图、角色名、显示状态、Show/Hide 和 Position 操作；卡片不显示 Skin 文本 | `MainWindow.xaml`、`CharacterViewModel.cs` |
| 右侧详情栏 | 固定 300 宽，显示当前角色名与 Skin，并提供 Mode 联动动画/战斗状态选择、全局设置、缩放、速度和删除 Skin 等控件 | `MainWindow.xaml`、`CharacterSettingsController.cs` |
| 窗口操作 | 拖动空白区域可移动窗口，边缘可调整大小；Finish Configuration、Alt+F4 或关闭动作会保存状态并隐藏面板 | `MainWindow.xaml.cs`、`MainWindowLifecycleController.cs` |

## 2. 左侧角色库

| 功能 | 具体实现 | 状态 |
|---|---|---|
| 角色卡片 | 按角色名排序并以 recycling 虚拟化两列展示，只实例化可视行和一行缓冲；缩略图在后台限流解码、缩放并冻结，在 64×64 边界内保持比例，高图或宽图不会撑高卡片 | 已实现 |
| 预览尺寸 | 50%–150% 滑条整体缩放卡片列表，并反向补偿滚动条宽度；数值保存到全局配置 | 已实现 |
| 显示状态 | Hidden、Visible、Loading 三种状态；角色已显示时 Show/Hide 按钮使用白色强调，加载期间禁止重复操作 | 已实现 |
| Show/Hide | 显示或隐藏角色，并把该角色设为右侧详情对象；不会为了按钮操作强制滚动左侧列表 | 已实现 |
| Position | 将角色恢复到默认位置，同时切换右侧详情对象；不会强制滚动左侧列表 | 已实现 |
| 卡片选择 | 点击卡片后更新右侧详情；普通选择会确保条目可见。选中框以 `IsSelected` 为唯一条目状态，键盘焦点框仅在焦点条目仍为当前选中项时显示；滚动自动选择移开后，旧焦点条目不会残留框线 | 已实现 |
| 滚动跟随 | 滚轮逐项选择：每格滚轮按两列阅读顺序移动一个条目并把选中条目槽位中心对齐视口中线；快速滚轮按格数合并为整数步长，不跳项、不丢列；顶部与底部钳制为首/末行不强行居中，划出边界不跳变。滚动条拖动等外部滚动按同一算术槽位与视口中线更新右侧详情 | 已实现 |
| 搜索 | 按角色名、当前 Skin、可用 Skin 编号及资源名进行不区分大小写的多词过滤 | 已实现 |
| 搜索快捷键 | Ctrl+F 聚焦搜索；Down/Enter 进入结果；Esc 清空；Clear 按钮仅在有输入时出现 | 已实现 |
| 键盘操作 | 方向键选择卡片；Enter/Space 切换显示；Shift+F10 打开 Skin 菜单 | 已实现 |
| Skin 切换 | 右键卡片或 Shift+F10 打开可用 Skin 菜单，当前 Skin 带选中状态；切换后复用角色配置并重载资源 | 已实现 |

## 3. 右侧详情与设置

| 功能 | 具体实现 | 状态 |
|---|---|---|
| 当前角色 | 显示所选角色名和当前 Skin；未选择时显示空状态。面板每次打开都会从当前配置与运行时状态重建角色库与详情；选中角色的资源加载完成、动画列表或配置动画变化时自动重新同步右侧选项，无需手动点选 | 已实现 |
| Normal/Battle | 完整 Aim/Cover 角色可手动切换；启动及首次展示为 Normal，进入 Battle 默认 Cover；模式不持久化 | 已实现 |
| Mode 联动选择 | Mode 与右侧选择框同一行；Normal 显示 standing 资源的真实动画名，选择后立即循环播放并保存；隐藏角色从 `.skel/.json + .atlas` 元数据读取动画名，不制造配置值占位项；Battle 仅显示 `Cover`、`Aim`，继续使用现有 Battle 状态逻辑；无 Battle 资源时 Mode 禁用。视图协调器在同一次提交中把完整选项快照与快照中的真实选中项应用到选择框，Normal/Battle 往返后收起标题与展开选中项保持一致；动画下拉只隐藏自身可见的垂直滚动条，滚轮和键盘滚动仍可用 | 已实现 |
| Reset All | 缩放、速度、位置与动画恢复默认；动画默认名从已加载渲染快照或隐藏角色骨骼元数据按 `idle -> idle* -> 第一动画` 解析并持久化，不能写死不存在的 `idle`。相同资源连续 Reset 得到相同配置与下拉选中项 | 已实现 |
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
| 右键短按 | Normal、无 Battle 的角色，或 Battle 中未满 300ms 时，保持原面板打开/关闭与角色定位行为 | 已实现 |
| Battle 右键长按 | Battle 中按住满 300ms 切 Aim，按 `to_aim -> aim_fire` 连续播放；松开或捕获丢失切 Cover，按 `to_cover -> 换弹 -> cover idle` 回落 | 已实现 |
| 配置模式 | 面板开启期间渲染层进入配置模式；完成配置后隐藏面板并恢复桌面交互模式 | 已实现 |

## 5. 资源与应用入口

| 功能 | 具体实现 | 状态 |
|---|---|---|
| Add | 文件选择器接受 `.skel` 或符合命名规则的 UnityFS bundle；导入 standing 资源后尝试自动补齐角色图标 | 已实现 |
| Scan | 扫描 `res`，按角色、Skin、状态同步；完整 Aim/Cover 自动补写 Battle，残缺或失效组合清除 Battle；连续扫描无变化时不保存、不通知也不移除渲染资源 | 已实现 |
| 新卡默认配置 | Add 或 Scan 新建的角色卡默认保持 Hidden，不自动打开；Scale 基础比例为 100%，倍率为 1.0，对应最终缩放 0.2 | 已实现 |
| Folder | 创建并打开当前生效的 `res` 资源目录 | 已实现 |
| 托盘菜单 | 双击托盘图标打开面板；菜单提供 Open Panel、Show All、Hide All、Exit | 已实现 |
| 重复启动 | 同一 EXE 目录只保留一个实例并激活已有面板；不同 EXE 目录使用不同互斥锁，可同时运行 | 已实现 |
| 紧急退出 | 全局快捷键 Ctrl+Alt+Shift+F12 请求退出；界面线程失去响应时会强制结束进程 | 已实现 |
| 状态持久化 | 保存角色显隐、位置、缩放分量、Normal 常驻动画、速度，以及帧率、拖动、预览尺寸和 Battle 资源规则；活动模式与 Cover/Aim 状态不持久化 | 已实现 |

## 6. 实现分层与渲染引擎边界

- `Views/MainWindow.xaml`：布局、控件、绑定、视觉状态和无障碍文本。
- `Themes/ConfigPanelStyles.xaml`：角色卡片的选中与焦点视觉；焦点框必须同时
  满足键盘焦点和当前选中，避免滚动导航切换选中项后留下旧框线。
- `ViewModels/MainViewModel.cs`：主界面 DataContext，持有全部 INPC 状态、
  选项集、派生显示与搜索锚定；setter 副作用经事件出口（缩放提交、设置
  同步、过滤刷新、缩略图应用），不持有控件。
- `Views/MainWindow.xaml.cs`：视图装配、事件路由、窗口命中测试、拖动
  开关动画和无障碍播报；不持有绑定状态。
- `Views/DisplaySelectionComboCoordinator.cs`：动画选择框的唯一视图写入者；
  把 ViewModel 的完整选项快照和该快照中的匹配选中项作为一次控件提交应用，
  并在提交期间屏蔽选择回写。
- `Views/MainWindow.xaml`：`AnimationCombo` 仅隐藏自身的可见垂直滚动条；
  不修改全局 `ComboBox` 模板，避免改变其他选择框的行为。
- `Views/CharacterLibraryController.cs`：搜索、Add/DB 导入、扫描、显隐、
  Skin 切换和列表同步；角色状态快照只增量更新对应卡片。
- `Views/CharacterThumbnailService.cs`、`VirtualizingUniformGrid.cs`：
  有界后台缩略图解码、冻结结果缓存，以及固定两列的 recycling 虚拟化布局。
- `Views/CharacterSettingsController.cs`：动画、缩放、速度、位置和 Skin 删除。
- `Views/CharacterPreviewNavigationController.cs`：选择、定位、滚动跟随与边界滚轮规则。
- `Views/CharacterPanelActivationController.cs`：桌面角色右键打开/关闭面板的流程。
- `Services/CharacterResourceCoordinator.cs`：以纯计算方式匹配资源并生成同步差异。
- `Services/CharacterManager.cs`：组合门面——编排跨组件操作（显隐与战斗
  运行时复位、移除、隐藏全部、关闭），转发渲染事件并回写配置；公共面为
  纯转发语义。
- `Services/CharacterCatalog.cs`：角色配置仓储——增删改、换肤失败回滚、
  资源同步、全局设置、持久化，并以 `CharactersChanged` 通知结构变化。
- `Services/CharacterAnimationNameReader.cs`：以空纹理加载器读取骨骼与 atlas
  元数据，为未加载角色提供真实动画名；读取失败返回空列表并记录日志。
- `Services/CharacterShowCoordinator.cs`：显隐 single-flight 协调——同一
  角色同一资源复用同一加载任务，不同资源按序排队，完成后再持久化。
- `Services/BattleInteractionController.cs`：Normal/Battle 运行时状态机
  与右键长按/短按交互。
- `Services/NikkeDbResourceImportService.cs`、`CharacterBattleConfigFactory.cs`：
  精确编号导入、三状态归组、Battle 完整性和动画回退配置。
- `Services/ConfigNormalizer.cs`、`ConfigFileCommitter.cs`：配置规范化、版本排序和原子磁盘提交。
- `Rendering/Native/NativeCharacterRenderHost.cs`：保持 `ICharacterRenderHost` 的门面，
  负责线程安全快照、命令入队、事件合并和生命周期，不直接拥有原生资源。
- `Rendering/Native/NativeRenderThread.cs`、`NativeCharacterRenderEngine.cs`：
  独立 STA Dispatcher 线程及线程封闭的原生渲染引擎。原生窗口、D3D、
  DirectComposition、场景、Surface、帧调度和纹理生命周期均由该线程独占。
- `Rendering/Native/NativeRenderSession.cs`：原生窗口、输入窗口、D3D11/DirectComposition
  资源的初始化、提交和释放。
- `Rendering/Native/NativeCharacterScene.cs`：拥有角色状态集合与 z-order；
  `NativeCharacterLoader.cs` 负责限流、可取消的资源解析和包络计算，安装与释放仍由宿主
  按状态差异执行。
- `Rendering/Native/NativeAnimationController.cs`：常驻动画选择、点击临时动画和模式切换。
- `Rendering/Native/NativeFrameScheduler.cs` 负责帧节拍；
  `NativeFrameRenderer.cs` 负责 Spine 更新、几何/曲面绘制、提交和性能采样，
  二者均不拥有 UI 或配置持久化职责。Dispatcher 已有一帧等待执行时，调度器
  丢弃后续过期节拍，不补跑积压帧；动画推进继续使用真实经过时间。
- `Rendering/Native/NativePointerController.cs`、`NativeCharacterHitTester.cs`：
  鼠标点击、拖动、右键和几何命中。
- `Rendering/Native/NativeInputRegionCoordinator.cs`、`NativeSilhouetteRasterizer.cs`：
  可点击区域、轮廓缓存、工作区裁剪和屏幕坐标转换；输入区域只在可见角色集合、
  轮廓缓存版本、显示器工作区或拖拽结束发生变化时重新聚合。
- `App.xaml.cs`、`TrayIconService.cs`：启动、托盘、单实例激活和退出。

除公开不可变 `CharacterRenderSnapshot` 外，上述新增类型均为 `internal`。
依赖方向固定为“门面 → 渲染线程引擎 → 协调组件 → 原生资源”，
WPF 控件、配置服务和第三方 `SpineRuntime41` 不反向依赖渲染内部组件。公开的
角色级状态事件只携带不可变快照；集合级事件只表示结构变化。
`ICharacterRenderHost` 的资源状态切换必须异步返回实际成功结果，管理器和界面
不得在渲染失败时提前提交显示状态。

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
- WPF Dispatcher 只处理控件、输入和轻量状态快照，不执行原生窗口创建、D3D
  初始化、纹理上传、mipmap 生成、首帧渲染或持续帧循环。同步状态查询直接读取
  门面的线程安全快照，不跨线程等待渲染器。
- 动画、资源切换和显隐等语义命令按入队顺序执行；位置、缩放、速度和帧率等
  高频命令按角色与属性合并为最新值，不能通过积压渲染命令反压 WPF Dispatcher。
- 同一角色一轮内的加载、可见性和动画状态通知合并为一次 WPF 增量更新。
  Show、Hide、Remove 和资源切换继续受角色 generation 与取消状态约束，
  过期加载结果不得重新显示已隐藏或删除的角色。
- 点击临时动画完成后，始终从第 0 帧开始循环当前有效的常驻动画；常驻动画无效时
  从第 0 帧开始循环默认待机动画。不得恢复点击前的旧播放进度。
- 配置面板开启或关闭只切换交互模式并取消当前指针捕获，不得选择、清空或重启
  任何动画轨。右键短按已显示角色不得改变当前临时动画、常驻动画或播放进度。
- 关闭顺序固定为：标记关闭并取消加载/帧调度，解除鼠标和渲染事件订阅，释放角色
  与轮廓缓存，最后释放原生窗口和图形资源。关闭后的回调及重复关闭均为空操作。
- 配置同步与异步保存共用同一原子提交路径；旧版本不能覆盖新版本，相同内容
  不替换磁盘文件，成功提交后统一清除 `RequiresRewrite`。
- 动画下拉不得用相互独立的 `ItemsSource`/`SelectedItem` 绑定竞争更新。
  唯一视图协调器必须在同一次提交中应用完整选项快照，并把
  `SelectedItem` 设置为该快照中匹配的真实实例；Normal/Battle 往返及
  重复同步后，收起标题、展开选中项和 ViewModel 值必须一致。禁止用伪选项
  或修改源值为 `空 -> 当前值` 来制造标题显示。
- Reset All 的动画默认值必须从角色真实动画列表解析。连续执行 Reset 时，
  配置值、下拉标题和展开后的选中项必须保持一致。
- Mode 与 Cover/Aim 是角色运行时状态。角色隐藏、移除、卸载或应用
  重启时清除；再次展示仍从 Normal/standing 开始。
- Battle 资源在首次进入时预加载。右键释放和 `WM_CAPTURECHANGED` 共用幂等
  回 Cover 路径，避免 Aim 或连续开火卡住。
- Normal/Battle 或 Cover/Aim 只有在渲染层已取得目标资源槽并完成交换后才
  更新运行时状态。目标槽缺失、宿主关闭或资源不可用时，管理器保留最后一个
  已渲染状态，界面同步回滚，不得显示虚假的 Normal 或 Battle。
- 切回 Normal 时清除临时与附加动画轨，并从第 0 帧循环 standing 的
  `ConfiguredAnimation`；配置无效时按 `idle -> idle* -> 第一动画` 回退。
- 当前资源和 standing/aim/cover 非活动资源槽都属于存活资源。纹理清理必须
  汇总全部槽位；若 GPU 缓存因设备或清理流程丢失，CPU 资源允许从原贴图重新
  解码上传，不得因一次性像素缓存耗尽而让整条渲染帧永久失败。

## 8. 渲染热路径效率约束

- 每个角色每帧只遍历一次生成后的顶点坐标：`NativeSpineGeometry.Build()` 在写入
  顶点缓冲时同步累计本帧边界，帧渲染和资源包络计算直接复用该结果，不再次扫描
  Draw Batch 顶点。
- `NativeCompositionSurface` 缓存上次锚点坐标；角色位置未变化时不重复写入
  DirectComposition visual offset。交换链扩容或缩容会使缓存失效，并在下一次定位时
  重新提交偏移，不能因去重造成角色位置漂移。
- 输入轮廓仍按 100ms 上限刷新，并在位置或缩放改变时立即刷新；聚合后的 Win32
  输入区域不再每帧清空、裁剪、排序和提交。拖拽期间延迟区域重建，释放后必须执行
  一次刷新。
- 帧调度最多保留一个 Dispatcher 待执行帧。UI 或渲染短暂繁忙时不得建立补帧队列，
  恢复后从下一个正常节拍继续，以真实经过时间推进 Spine 状态。
- 缩略图文件不得从绑定 getter 或滚动路径同步读取。解码服务最多并行处理两个
  文件，按规范化路径、修改时间和目标尺寸语义缓存冻结后的 `ImageSource`，
  缓存条目有上限；缺失或损坏图片使用固定 64×64 占位状态。
- 动画速度、曲面位置、输入区域等原生状态只在目标值变化时写入；这些去重不得改变
  公开接口、角色显示状态、点击动画恢复、拖拽命中或关闭顺序。

## 9. 特殊混合与帧执行契约（已实现）

- 原生纹理继续按 atlas 的预乘 Alpha 约定上传。渲染批次必须分别定义颜色混合
  与覆盖 Alpha 混合；窗口覆盖率只由资源作者提供的源 Alpha 参与计算，不得从
  RGB 发光强度反推。
- `Normal` 使用预乘 Alpha source-over；`Additive` 分别累加预乘颜色与源覆盖
  Alpha，并由 UNORM 输出饱和，保证提交给 DirectComposition 的像素持续满足
  `RGB <= Alpha`；`Multiply`、`Screen` 保持 Spine 颜色公式，同时以
  source-over 更新覆盖 Alpha。资源制作的 Slot Alpha、淡出与发光仍按原动画生效。
- 每帧先按 Spine draw order 生成有序绘制计划，再统一上传角色几何并执行命令。
  禁止跨批次重排；只允许合并纹理、混合模式和裁剪状态相同的相邻批次。
  管线、混合状态和可增长 GPU 缓冲区由图形设备复用，重复绑定应按实际状态去重。
- 当前资源和 standing/aim/cover 非活动槽共同组成纹理存活集合。缓存清理不得
  淘汰仍被任一槽引用的纹理；真正丢失后允许从源 PNG 重新解码上传。
- 点击临时动画结束时必须重新解析完成瞬间的资源状态和常驻动画，从第 0 帧恢复
  一次。状态切换、连续点击、取消、隐藏、移除、关闭及过期回调不得重复恢复。
- 关闭顺序固定为停止帧调度和临时播放、解除事件、释放角色与缓存、最后释放
  DirectComposition 和 D3D11 资源；全部阶段允许重复调用且不得产生迟到回调。
