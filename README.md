# My_SpinePet

一个 Windows 桌面宠物程序：用原生 Spine 4.1 C# 运行时 + Direct3D 11 / DirectComposition
渲染 Spine 角色，WPF 提供配置面板（导入角色、选择动画、缩放、定位）。

程序**只负责渲染，不提供任何游戏素材**。本项目永久免费：没有广告、没有赞助入口、没有付费墙。

- 面向使用者的说明：本文档
- 面向开发者的说明（目录结构、构建细节、导入工具、面板实现）：[`SpinePet/README.md`](SpinePet/README.md)
- 素材与授权：[`ASSETS.md`](ASSETS.md) · [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)

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

## 快速开始

1. 解压发布包，双击 `Launch.bat`，或直接运行 `app\SpinePet.exe`。
2. 双击托盘图标（或托盘菜单 `Open Panel`）打开配置面板。
3. **首次启动是空角色库**——发布包不含任何素材，请按下一节导入你自己的资源。
4. 关闭窗口 / `Alt+F4` / `Finish Configuration` 只是隐藏面板并保存设置，程序仍在运行；
   退出请用托盘菜单 `Exit` 或 `Ctrl+Alt+Shift+F12`。

## 第一次使用：准备并导入素材

### 方式 A：用 Add 导入（推荐）

- `Add` 接受单个 `.skel` 文件，或文件名以 `c<角色ID>_<皮肤ID>_<standing|icons>_` 开头的
  UnityFS bundle。
- `standing` bundle：自动解出完整骨架、图集与所需贴图，写入对应皮肤目录下的
  `standing\`，随后尝试下载该皮肤的官方图标。
- 单独导入图标 bundle 只会写入图标，不会生成角色卡。
- UnityFS 导入依赖 Python 包：`python -m pip install -r SpinePet\tools\requirements.txt`。

### 方式 B：手动放入文件

把资源按下面的结构放进 `res\`，然后点 `Scan`：

```text
res\
  <角色名>\
    <皮肤编号>\            编号缺省时可使用 00
      standing\
        <资源名>.skel
        <资源名>.atlas
        <资源名>.png
      icons\               可选
        <资源名>_icon.png
```

资源名使用 `c<角色编号>_<皮肤编号>` 形式，角色显示名从
`SpinePet\src\SpinePet\Data\CharacterNames.json` 解析，面板上每个角色显示为一张卡片。

- `Scan` 会把卡片与已保存配置和磁盘上"完整"的资源重新对齐，手动删除或替换资源后点它即可。
- 右键卡片（或选中后按 `Shift+F10`）可直接切换该角色可用的皮肤。
- `Delete Current Skin` 需要确认，确认后把该皮肤目录移入 Windows 回收站。
- 图标是可选资源；下载失败不会撤销 `standing` 导入，面板会提示并回退用立绘贴图当缩略图。
- `res\` 里的说明文件可以删除，但请保留 `res\` 目录本身：应用按它解析资源位置，
  目录整个消失时会回退到 `app\res`。
- `SpinePet\tools\battle-catalog-importer` 可批量审计/导入完整的 Aim/Cover 组合，
  但必须显式给出资源目录；不完整或非 4.1 的组合会被跳过。

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
  app\                       程序本体与内部工具，请勿改名
  Logs\                      运行日志，排查问题用
  LICENSE / NOTICE           本项目许可证（GPL-3.0-or-later）与版权声明
  THIRD_PARTY_NOTICES.md     第三方组件与授权清单
  ASSETS.md                  素材来源与授权边界
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

## 素材与授权

- 本项目**永久免费**：没有广告、没有赞助入口、没有付费墙，不卖播放器、不卖素材。
- 本项目自身代码按 **GPL-3.0-or-later** 授权（见 `LICENSE` / `NOTICE`）：
  允许自由使用、修改、再分发，但**不允许把衍生版本闭源收费再分发**。
- 本项目**不含任何游戏素材**；角色素材版权归各自权利人（NIKKE 相关素材归
  Shift Up Corp.），不在本项目授权范围内，本项目无权为你授权。详见 [`ASSETS.md`](ASSETS.md)。
- Spine 运行时是独立第三方组件，其许可证要求**每个使用者自行持有 Spine Editor 授权**；
  `tools\SpineSkeletonDataConverter` 使用 PolyForm Noncommercial 1.0.0，**禁止商业用途**。
  详见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。

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
