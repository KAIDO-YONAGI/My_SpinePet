# SpinePet GUI 重构工作计划

> 维护工作记忆：每次继续本任务前先阅读本文件的“现状与约束”和“工作记录”。
> 最后更新：2026-08-20

## 目标

1. 鼠标滚轮驱动预览栏选中项跟随：
   - 只响应预览列表的真实滚动；
   - 按网格的行优先顺序处理条目：从左到右、从上到下；
   - 以预览栏可视区域的垂直中线为基准，选择中线附近的可见卡片，而不是固定取每行左列。
2. 在详情区域选中角色时，左侧预览列表的滚动条跳到对应卡片，并保证卡片可见。
3. 位置重置按钮移动到每张预览卡的 `Show/Hide` 按钮下方；详情标题栏释放出的空间用于更稳定地显示角色名。
4. 重构 GUI 代码的职责边界，减少 `MainWindow` partial 之间的隐式状态耦合。
5. 对滚动跟随、详情定位、列表刷新和重复事件做好幂等保护。
6. 不在工作区留下本次重构不再使用的脚本文件。

## 现状与证据

### 入口和布局

- `src/SpinePet/Views/MainWindow.xaml` 是单一 WPF 配置窗口。
- `src/SpinePet/Views/MainWindow.xaml.cs`、`MainWindow.CharacterLibrary.cs`、
  `MainWindow.SelectionSettings.cs` 共同组成一个约 2,000 行的窗口类。
- 左侧 `CharacterCards` 使用 `ListBox + UniformGrid Columns="2"`，
  `ScrollViewer.CanContentScroll="False"`，因此可以按像素读取滚动位置。
- 右侧详情标题栏目前同时承载角色名、皮肤名和 `Position` 按钮。

### 当前滚轮跟随问题

- `MainWindow.CharacterLibrary.cs` 的 `OnCharacterCardsScrollChanged` 通过
  `row = floor(VerticalOffset / rowPitch)` 后取 `row * 2`。
- 该实现固定选择每一行的左项，没有计算右列，也没有使用预览栏可视区域中线。
- `MainWindow.xaml.cs` 还在 `Loaded` 中重复寻找内部 `ScrollViewer` 并订阅同一个滚动处理器，
  与 XAML 上已有的 `ScrollViewer.ScrollChanged` 事件形成重复入口。
- `_suppressScrollFollow` 只靠延迟清除布尔值保护程序滚动，容易与布局刷新或连续选择事件交错。

### 当前详情定位问题

- `OnCharacterSelectionChanged` 使用 `CharacterCards.ScrollIntoView`，但定位逻辑和
  选择同步、滚轮跟随共用窗口字段，缺少单一协调入口。
- `RefreshCharacterFilter`、列表刷新、详情选中事件都可能改写 `SelectedCharacter` 和
  `CharacterCards.SelectedItem`，重复刷新时可能触发多次定位。

### 当前布局问题

- `OnResetSelectedPosition` 位于右侧详情标题栏。
- 左侧卡片的第三列已有 `Show/Hide` 按钮，适合扩展为竖向操作区，把位置重置放在其下方。
- 右侧标题栏移除按钮后，可以让名字使用完整的标题区域，不再与按钮争抢宽度。

## 目标架构

### 1. 预览导航协调器

新增一个纯 C# 的小型导航组件，负责不依赖 WPF 控件的规则：

- 输入：按当前 `CharacterView` 顺序排列的条目、每项的实际矩形、中线距离和可见性；
- 输出：中线基准下应选中的条目索引；
- 规则固定为双列行优先：`index = row * columnCount + column`；
- 同一物理行的左右卡片使用轻量的虚拟中线偏移，使滚轮在垂直滚动时能依次命中左项、右项；
- 同一中线候选存在当前选中项时优先保留当前项，避免右列被重复抢回左列；
- 空列表、无可见条目、无效尺寸输入均返回“无选择”，重复调用不产生副作用。

WPF 适配层只负责读取 `ListBoxItem` 的布局矩形、取得 `ScrollViewer`、
执行 `ScrollIntoView`，并把结果交给窗口的唯一选择入口。

### 2. 窗口内的选择/定位协调

- 保留 `SelectedCharacter` 作为窗口对外绑定状态，但所有列表选择统一经由一个入口完成。
- 使用一次性的“程序滚动会话”标记，而不是依赖 `BeginInvoke` 清除的脆弱布尔值：
  - 详情选中或搜索结果定位时先标记；
  - `ScrollIntoView` 后在布局/滚动事件完成时结束；
  - 同一目标重复定位直接复用，不重复改写选择。
- 鼠标滚轮触发的 `ScrollChanged` 只在用户滚动且没有程序滚动会话时更新选中项。
- 列表刷新、过滤、详情选中后的定位均可重复执行，最终状态一致。

### 3. 位置重置职责

- 位置重置处理函数不再依赖“右侧标题栏按钮”的位置语义；
- 从预览卡按钮的 `Tag` 读取 `CharacterViewModel`，按卡片自身角色重置；
- 保留已有 `CharacterManager.RenderHost.ResetCharacterPosition` 作为唯一实际执行点；
- 位置提交事件仍由现有管理器/渲染主机负责，GUI 只同步对应 ViewModel。

## 实施步骤

- [x] 完成入口、布局、引用点和当前滚动实现的调研。
- [x] 创建本计划文档。
- [x] 新增并测试纯 C# 的预览中线选择规则。
- [x] 删除旧的 `row * 2`、内部 ScrollViewer 重复订阅和旧抑制字段。
- [x] 重做列表选择与程序滚动协调，保证详情选中能定位到对应卡片。
- [x] 调整 XAML：位置按钮移动到 `Show/Hide` 下方，详情标题栏释放给名字。
- [x] 把窗口行为按“列表导航 / 选择设置 / 窗口生命周期”收敛到明确的辅助职责，
      避免新增跨 partial 隐式状态。
- [x] 增加布局/行为回归测试，覆盖双列行优先、中线选择、空列表和幂等定位。
- [x] 运行 `dotnet test` 与 `dotnet build src/SpinePet/SpinePet.csproj -c Release`。
- [x] 检查工作区脚本/生成物，未产生本次重构专用临时脚本。
- [x] 回填验证结果和遗留风险。

## 验收标准

### 功能

- 预览卡为双列时，滚轮从上到下的选择顺序是 `左上、右上、左下、右下...`，
  不会只在左列跳转。
- 滚动停止后，选中卡片位于预览可视区域的垂直中线附近；中线经过两卡之间时，
  选择规则稳定且可预测。
- 从右侧详情切换角色后，对应左侧卡片立即被滚动到可见区域，且不会被滚轮跟随逻辑抢回其他角色。
- 位置重置按钮显示在每张卡片的 `Show/Hide` 下方，详情标题栏可以显示角色名。

### 代码质量

- 滚动规则可脱离 WPF 通过单元测试验证。
- 事件重复订阅、重复定位、空容器和列表刷新不会造成异常或状态漂移。
- 旧滚动实现和无用的临时脚本不再存在。

## 工作记录

### 2026-08-20：调研完成

- 已确认项目为 `D:\SpineTools\SpinePet`，WPF `.NET 9`。
- 已读取 `AGENTS.md`、`MainWindow.xaml`、三个 `MainWindow` 代码文件、
  `CharacterViewModel`、相关布局测试和渲染主机接口。
- 已确认根仓库存在用户未提交修改：`D:\SpineTools\prompts.txt`；
  本任务不触碰该文件。
- 当前实现的关键缺陷已记录在“现状与证据”。

### 2026-08-20：交互重做与第一次验证

- 新增 `Views/PreviewNavigationCoordinator.cs`：
  - 以所有可见 `ListBoxItem` 的实际矩形参与计算；
  - 使用预览视口垂直中线；
  - 对双列同一行加入左到右的虚拟中线偏移，滚动位置测试覆盖 `0 -> 1 -> 2`；
  - 提供详情定位的居中偏移计算，并将程序滚动会话与滚轮选中会话分开管理；
  - 集中管理程序滚动会话和滚轮选中会话，重复调用可安全收敛。
- 删除 `row * 2`、`GetRowPitch`、`_suppressScrollFollow` 和 `Loaded` 中的重复滚动订阅。
- 详情选择、搜索聚焦、过滤刷新统一经过 `SelectCharacterAndReveal`/`RevealPreviewItem`；
 位置按钮改为每张卡片 `Show/Hide` 下方的按卡片重置操作，详情标题栏释放给角色名。
- 新增规则和 XAML 回归测试；目标测试 7/7 通过。
- 后续补充规则和布局测试后，目标测试 11/11 通过；全量测试为 146 通过、4 个与本次改动无关的失败：
  - Windows 创建符号链接缺少权限（2 个测试）；
  - 品牌图标缺少 56px 尺寸；
  - WPF 无头测试字体缓存 URI 初始化失败。
- Release 构建 0 警告、0 错误；旧滚动实现、重复订阅、旧位置处理器和临时脚本候选已完成静态审计。

### 2026-08-20：完成审计

- 双列滚轮规则不再只靠“遍历所有项但同心左优先”：同一行左右项按虚拟中线偏移依次命中，
  当前选中项作为 tie 候选时保持稳定。
- 详情定位流程为：选择目标 -> `ScrollIntoView` -> 按预览垂直中线校正滚动偏移 ->
  会话完成；滚动事件在程序会话期间不参与抢选中。
- XAML 断言确认滚动事件只有一个入口，位置按钮与 `Show/Hide` 位于同一个操作栈，
  没有残留旧位置按钮处理器。
- 工作区没有新增或修改本次重构专用脚本；现有用户改动 `.gitignore`、`prompts.txt` 未触碰。
- Release 进程烟囱检查在当前机器无法完成到稳定窗口状态：
  WPF 在 `System.Windows.Window` 初始化时触发既有的
  `MS.Internal.FontCache.Util -> UriFormatException`，与全量测试中的无头字体缓存失败相同；
  该异常发生在 `MainWindow` 进入本次交互代码之前。

### 后续记录格式

- 日期：
- 改动：
- 验证：
- 结果/遗留风险：
