# Favorite Interactive Import Guide

文档 ID：`RES-LOAD-FAVORITE-GUIDE`  
状态：`Active`  
最后更新：`2026-08-24`  
最后核验：`2026-08-24`

## 适用边界

本指南适用于文件名形如 `favorite_cNNN_00`、通常只包含 `idle` 和
`expression_merged` 的单状态互动资源。它们不是常规 standing/aim/cover
三状态资源，不进入 Battle 导入，不生成 Aim/Cover 目录或 Battle 配置。

通用文件校验、冲突处理和 `res` 目录约束仍以
`SpinePet_Resources_Load_Guide.md` 为准。

## 身份与编号

1. 从源文件名提取三位原编号 `NNN`。
2. 使用 nikkedb 的基础角色编号反查姓名，并使用原编号查找小头像。
3. Favorite 本地编号固定为 `9NNN`，即在三位原编号前加 `9`。
4. 导入前同时检查 `CharacterNames.json` 和 `SpinePet\res`，确认新编号未占用。
5. 同一预检阶段按原编号定位头像；头像未命中时停止该项，不先导入本体。
6. 本类资源统一使用皮肤编号 `00`，显示名为 `<Name> Favorite`。

示例：`favorite_c072_00` 对应 Diesel，原编号 `072` 只用于身份和图标查询；
本地编号为 `9072`。

## 目录与文件

源资源移动到归档目录：

```text
resources\Characters\Diesel Favorite\
```

运行时资源写入：

```text
SpinePet\res\Diesel Favorite\00\standing\
  c9072_00.skel
  c9072_00.atlas
  c9072_00.png
SpinePet\res\Diesel Favorite\00\icons\
  c9072_00_icon.png
```

- `.skel` 二进制保持原样，只规范化目标文件名。
- `.atlas` 页引用改为新编号对应的 PNG 文件名。
- 原贴图按新编号复制，不进行背景或特效清理。
- `CharacterNames.json` 新增 `"9072": "Diesel Favorite"`。

## 图标

nikkedb 图标按原编号查询，例如：

```text
si_c072_00_s.png
```

复制到运行时目录后使用新编号命名：

```text
c9072_00_icon.png
```

不得用 Favorite 新编号反查 nikkedb，因为该编号只属于本项目。
该查询只属于离线自动化；应用运行时不得访问 nikkedb。批量任务只查询用户
指定的 Favorite 清单，不枚举其他目录或编号。

## 动画行为

- 显示名以 ` Favorite` 结尾的单交互资源，默认常驻动画固定为精确
  `idle_merged`；用户显式配置且该动画存在时，显式配置仍优先。
- 点击动画先按 `action`、`click`、`touch`、`tap`、`reaction`、
  `interact`、`skillcut` 的既有优先级选择。
- 单交互 Favorite 不含上述常规点击动画时，点击动画固定落到
  `expression_merged`。
- 点击动画结束后恢复当前默认常驻动画。
- 不以 ` Favorite` 结尾的普通角色保持原有精确 `idle` 优先规则。

## 验证清单

1. 每套归档资源包含且只包含匹配的 `.skel`、`.atlas` 和纹理页。
2. 每套运行时 standing 可被当前 Spine 运行时实际解析。
3. `.atlas` 页引用与目标 PNG 文件名完全一致。
4. `idle_merged` 和 `expression_merged` 均存在。
5. 新编号在名称表和 `res` 中唯一，图标存在。
6. 不生成 aim、cover 或 Battle 配置。
7. 点击动画选择测试、全量测试、Build、Publish、Run 均通过。
