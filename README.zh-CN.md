# My_SpinePet

一个 Windows 桌面宠物程序：用原生 Spine 4.1 C# 运行时 + Direct3D 11 / DirectComposition
渲染 Spine 角色，WPF 提供配置面板（导入角色、选择动画、缩放、定位）。

程序**只负责渲染，不提供任何游戏素材**。本项目永久免费：没有广告、没有赞助入口、没有付费墙。

[English](README.md) | **中文**

## 文档导航

| 文档 | 中文 | English |
| --- | --- | --- |
| 使用说明 | **README.zh-CN.md** | [README.md](README.md) |
| 素材政策 | [ASSETS.zh-CN.md](ASSETS.zh-CN.md) | [ASSETS.md](ASSETS.md) |
| **素材导入指南**（四类资源、格式改造、工具、AI 辅助、排查） | [IMPORT.zh-CN.md](IMPORT.zh-CN.md) | [IMPORT.md](IMPORT.md) |
| 第三方组件与许可证 | [THIRD_PARTY_NOTICES.zh-CN.md](THIRD_PARTY_NOTICES.zh-CN.md) | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |
| 项目许可证 / 版权声明 | [LICENSE](LICENSE)、[NOTICE](NOTICE) | 同文件（英文） |
| 开发者文档（目录结构、构建细节、内部工具） | [SpinePet/README.md](SpinePet/README.md) | 同文件（英文） |
| Spine JSON 版本差异（参考） | [docs/spine-version-differences.md](docs/spine-version-differences.md) | 同文件（中文） |
| 发行包内使用说明 | [UserTips.txt](UserTips.txt) | [UserTips.en.txt](UserTips.en.txt) |

## 目录

- [系统要求](#系统要求)
- [快速开始](#快速开始)
- [第一次使用：准备并导入素材](#第一次使用准备并导入素材)
- [桌面交互](#桌面交互)
- [配置面板](#配置面板)
- [快捷键](#快捷键)
- [资源目录结构](#资源目录结构)
- [便携布局、数据位置与卸载](#便携布局数据位置与卸载)
- [从源码构建与打包](#从源码构建与打包)
- [常见问题](#常见问题)
- [素材与授权](#素材与授权)

## 系统要求

- Windows 10 或更高版本。
- 使用发布包：无需安装 .NET，`win-x64` 自包含发布已内含运行时。
- 从源码构建：.NET 9 SDK。
- 角色资源：Spine 4.1 导出的 `.skel` / `.atlas` / `.png`，或符合命名规则的 UnityFS bundle。
- 用 Add 导入 UnityFS bundle 时需要本机 Python 环境（UnityPy + Pillow）。
- 裸机部署不需要 VC++ 运行库；包内 `Check-Environment.ps1` 可一键自检缺什么、缺了影响哪个功能，
  完整依赖清单（含“哪些能力零依赖、哪些需要 Python”）见 [`IMPORT.zh-CN.md` 第 8 节](IMPORT.zh-CN.md)。

## 快速开始

1. 解压发布包，双击 `Launch.bat`，或直接运行 `app\SpinePet.exe`。
2. 双击托盘图标（或托盘菜单 `Open Panel`）打开配置面板。
3. **首次启动是空角色库**——发布包不含任何素材，请按下一节导入你自己的资源。
4. 关闭窗口 / `Alt+F4` / `Finish Configuration` 只是隐藏面板并保存设置，程序仍在运行；
   退出请用托盘菜单 `Exit` 或 `Ctrl+Alt+Shift+F12`。

## 第一次使用：准备并导入素材

**完整规范见 [`IMPORT.zh-CN.md`](IMPORT.zh-CN.md)**：四类资源各自的导入方式、原始格式要改什么、
工具清单与用法、AI 辅助导入做法、验证与排查顺序。这里只给要点。

支持四类资源：

| 类型 | 目录特征 | 导入方式 |
| --- | --- | --- |
| 待机 standing | `<皮肤>\standing\` | 应用内 **Add**（`.skel` / UnityFS bundle），或手工放入 |
| 射击 aim / cover | `<皮肤>\aim\` + `<皮肤>\cover\` | `SpinePet\tools\battle-catalog-importer`，**指定清单、先审计后导入** |
| 爆裂 skillcut | 目录名以 ` Burst` 结尾 | 同待机；Battle 与 Lobby 的 skillcut 相同则取 Lobby |
| 珍藏品 Favorite | 目录名以 ` Favorite` 结尾 | 同待机；本地编号固定 `9NNN`、皮肤固定 `00`，**不进 Battle** |

要点：

- 只认一种布局：`res\<资源全名>\<皮肤编号>\<状态>\`，骨骼文件名必须带
  `c<角色ID>_<皮肤ID>` 前缀。**游戏导出的原始资源要改名、改号之后才能用**，
  而且每次导入都要分配新的角色 ID（见 IMPORT.zh-CN.md 第 2 节）。
- 一套资源必须同时有同名 `.atlas` 和 atlas 引用的**全部**贴图页，且由
  **Spine 4.1.x** 导出；缺一样就停在外面，不要放进 `res\`。
- 改过 `CharacterNames.json` 后**必须重新构建**才生效；覆盖 `res` 里已有文件前
  先关闭 SpinePet（Windows 文件锁）。`res\` 目录本身请保留，应用按它解析资源位置。
- `Add` 只接受 standing 的 `.skel` 或符合命名规则的 UnityFS bundle（`icons` bundle
  只写图标、不建卡片）；UnityFS 导入依赖
  `python -m pip install -r SpinePet\tools\requirements.txt`。
- 新增卡片默认隐藏、缩放 100% / 1.0 倍；确认无误后在面板点 `Scan`。

## 桌面交互

| 操作 | 行为 |
| --- | --- |
| 左键单击 | 播放角色的点击动画；没有匹配动画时保持当前动画 |
| 按住左键拖动 | 移动角色并保存位置（需开启 `Allow dragging in render mode`） |
| 右键角色（面板已关闭） | 打开配置面板并自动选中、滚动到该角色 |
| 右键角色（面板已打开） | 保存并关闭配置面板 |

## 配置面板

左侧角色库：

- 点击卡片选中角色，右侧显示该角色详情。
- `Show` / `Hide` 切换显隐并把右侧详情切到该角色；`Position` 恢复默认位置。
  白色 `Show/Hide` 按钮表示角色当前已显示；`Loading` 期间不能重复操作。
- 滚动列表时，右侧详情按两列顺序跟随视口中央的角色。
- 搜索同时匹配角色名与皮肤信息（`Ctrl+F` 聚焦）。
- 方向键选择角色，`Enter` / `Space` 切换显隐，右键卡片或 `Shift+F10` 切换皮肤。
- 预览尺寸滑条在 50%–150% 之间调整卡片大小。

右侧设置：

- `Animation`：选择并循环播放该角色的动画。
- `Desktop frame rate`：30 / 60 / 120 FPS，全局生效。
- `Allow dragging in render mode`：控制所有桌面角色能否拖动，全局生效。
- `Scale`：第一条是基础比例 0%–100%（100% 对应基础缩放 0.2）；第二条是独立的
  1.0–5.0 倍乘数，不影响第一条。最终缩放 = 基础比例 ÷ 100 × 0.2 × 倍率，
  并受角色最大尺寸限制。`Reset` 恢复 100% 和 1.0 倍。
- `Animation Speed`：0.10–2.00 倍，`Reset` 恢复 1.00 倍。
- `Delete Current Skin`：确认后把当前皮肤目录移入回收站。

工具栏：`Add`（导入）、`Scan`（重新扫描 `res\`）、`Folder`（打开当前 `res\` 目录，
目录不存在时会自动创建）。

## 快捷键

| 快捷键 | 作用 |
| --- | --- |
| `Ctrl+F` | 聚焦角色搜索框 |
| `↓` / `Enter` | 从搜索框移动到选中的结果 |
| `Esc` | 清空当前搜索内容 |
| `↑` / `↓` | 在角色列表中移动选择 |
| `Enter` / `Space` | 显示或隐藏选中角色 |
| `Shift+F10` | 打开选中角色的皮肤菜单（等同右键卡片） |
| `Alt+F4` | 隐藏配置面板（不退出程序） |
| `Ctrl+Alt+Shift+F12` | 紧急退出（渲染窗口干扰正常输入时使用） |

## 资源目录结构

```text
<发布包目录>\
  Launch.bat                 推荐启动入口
  config.json                便携配置（角色、显隐、位置、缩放、动画、速度、全局设置）
  res\                       角色资源库（内含一份放置说明；素材由你自行准备）
  app\                       程序本体（自包含），请勿改名
  tools\                     运行时工具：UnityFS 解包脚本、图标下载脚本、requirements.txt
  Logs\                      运行日志，排查问题用
  UserTips.txt               使用说明（中文，含本次构建信息）
  UserTips.en.txt            User guide (English)
  IMPORT.md / IMPORT.zh-CN.md  素材导入指南（四类资源、格式改造、工具、AI 辅助、排查）
  Check-Environment.ps1      运行环境自检（Windows 自带 PowerShell 即可运行）
  tools\import\              可选：自包含的射击 aim/cover 导入工具（用 -IncludeImportTools 打包才有）
  LICENSE / NOTICE           本项目许可证（GPL-3.0-or-later）与版权声明
  THIRD_PARTY_NOTICES.md / .zh-CN.md  第三方组件与授权清单
  ASSETS.md / ASSETS.zh-CN.md  素材来源与授权边界
  licenses\                  各第三方许可证原文，请勿删除
```

## 便携布局、数据位置与卸载

- 便携：目录中存在 `config.json` 时，配置与日志就写在该目录（`app\` 子目录布局下写其上一级），
  整个文件夹可以随便搬。
- 若没有便携配置，数据回落到 `%LOCALAPPDATA%\SpinePet`；
  高级用法可用环境变量 `SPINEPET_DATA_DIRECTORY` 指定数据目录。
- 配置 JSON 损坏时，原文件会先被保留为同目录下带时间戳的 `.corrupt` 文件，再重建一份干净配置。
- 多实例：同一目录重复启动会激活已有实例；不同构建目录的 EXE 各自独立，可以同时运行。
- 卸载：便携发布包直接删除整个文件夹即可。

## 从源码构建与打包

解决方案位于 `SpinePet\SpinePet.sln`。在**仓库根目录**执行：

```powershell
# 构建
dotnet restore SpinePet\SpinePet.sln
dotnet build SpinePet\SpinePet.sln -c Debug
dotnet build SpinePet\SpinePet.sln -c Release

# 运行 Debug 构建
dotnet run --project SpinePet\src\SpinePet\SpinePet.csproj

# 打包便携发布版（产出 release\<构建名>\ 与 dist\<构建名>.zip）
.\tools\Publish.bat
```

发行包**只包含程序、配置模板与授权文件，不包含任何角色资源**；`res\` 由使用者自行准备。
仓库内的辅助工具位于 `SpinePet\tools\`：图集清理、角色资源布局迁移、图标下载、
资源 zip 导入、骨架附件检查、Aim/Cover 批量导入等，细节见
[`SpinePet/README.md`](SpinePet/README.md)。

## 常见问题

- **面板打不开或被挡住？** `Alt+F4` 只是隐藏面板；重新双击托盘图标，或再次启动 `SpinePet.exe`。
- **输入被渲染窗口干扰？** 按 `Ctrl+Alt+Shift+F12` 紧急退出。
- **角色不显示？** 检查 `res\` 目录结构与文件是否完整（骨架、图集、贴图三者缺一不可），
  然后点 `Scan`；运行日志在 `Logs\`。
- **导入被拒绝？** 确认 bundle 命名符合 `c<角色ID>_<皮肤ID>_<standing|icons>_` 规则、
  且导出为 Spine 4.1；若两个角色 ID 会映射到相同的显示名与皮肤目录，导入会停止，
  需要先修正 `CharacterNames.json`。
- **界面语言**：自包含发布只保留 `zh-Hans` 语言资源。
- **没有 Python / 装不上 Python？** 仍然可用：手工把 `.skel` + 同名 `.atlas` + 全部贴图页放进
  `res\`，或用 `Add` 直接导入 `.skel`，再点 `Scan`。受影响的只有 UnityFS bundle 导入与图标自动下载。
- **不确定目标机缺什么？** 在包内运行
  `powershell -NoProfile -ExecutionPolicy Bypass -File Check-Environment.ps1`。

## 素材与授权

- 本项目**永久免费**：没有广告、没有赞助入口、没有付费墙，不卖播放器、不卖素材。
- 本项目自身代码按 **GPL-3.0-or-later** 授权（见 `LICENSE` / `NOTICE`）：
  允许自由使用、修改、再分发，但**不允许把衍生版本闭源收费再分发**。
- 本项目**不含任何游戏素材**；角色素材版权归各自权利人（NIKKE 相关素材归
  Shift Up Corp.），不在本项目授权范围内，本项目无权为你授权。详见 [`ASSETS.zh-CN.md`](ASSETS.zh-CN.md)。
- Spine 运行时是独立第三方组件，其许可证要求**每个使用者自行持有 Spine Editor 授权**；
  带非商业条款的第三方工具（例如开发期用过的 SpineSkeletonDataConverter）不属于本仓库、
  也不随任何产物分发；你若自行获取并使用它们，需遵守其各自的条款。
  详见 [`THIRD_PARTY_NOTICES.zh-CN.md`](THIRD_PARTY_NOTICES.zh-CN.md)。

## 源码

- Gitee（主仓库）：https://gitee.com/KAIDOYONAGI/my_-spine-pet
- GitHub（镜像）：https://github.com/KAIDO-YONAGI/My_SpinePet

## English summary

SpinePet is a Windows desktop pet that renders Spine assets through the native
Spine 4.1 C# runtime, Direct3D 11 and DirectComposition, with a WPF panel for
importing characters, choosing animations, scaling and positioning. Usage:
unzip the release, run `Launch.bat`, import your own `.skel` / UnityFS resources
via `Add` or by dropping them into `res\`, then hit `Scan`. The project ships
**no game assets** — you supply your own — and it is free forever: no ads, no
donations, no paywall. The application's own code is licensed
**GPL-3.0-or-later**; third-party components keep their own licenses (see
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)). Character assets belong to
their rights holders and are not covered by this project's license
(see [`ASSETS.md`](ASSETS.md)). Developer documentation: [`SpinePet/README.md`](SpinePet/README.md).
