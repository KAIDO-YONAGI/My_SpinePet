# SpinePet 桌宠应用（Codex）

WPF (.NET 9) Spine 4.1 桌宠。资源根 `res\`、工具 `tools\`、
应用配置在 `C:\Users\12248\AppData\Local\SpinePet\config.json`（绝对路径引用 res）。

## Skill 索引（按任务读取，先读再做）

资源导入 / 背景清理工作流的硬规则在上级工作区的 skill 里：

| 任务 | 读取 |
|---|---|
| 导入资源（入库、分配 ID、配图标） | `D:\SpineTools\.zcode\skills\nikke-spine-import\SKILL.md` |
| 清理背景/海浪、排查点击与动画 | `D:\SpineTools\.zcode\skills\nikke-spine-clean\SKILL.md` |

改代码注意：

- `src\SpinePet\Data\CharacterNames.json` 是嵌入资源，改后必须
  `dotnet build src\SpinePet\SpinePet.csproj -c Release`（`global.json` 已 `rollForward: Major`）。
- 改 `res\` 或资源文件名前，先 `taskkill /IM SpinePet.exe /F`（文件锁），
  并同步 config.json 里的绝对路径。
- 本仓库由根目录 `备份.bat` 自动提交。
