# 素材来源与授权边界 / Assets Policy

[English](ASSETS.md) | **中文**

一句话：**本项目只提供"播放器"，不提供"内容"。**

## 1. 本项目不分发任何游戏素材

SpinePet 是一个桌宠外壳：它渲染 Spine 骨架资源，本身不含任何角色资源。

- 源码仓库不含素材：`SpinePet/res/` 已在 `.gitignore` 中忽略。
- 发行包不含素材：打包流程（`tools/Package-Release.ps1`）只输出程序、配置与授权文件，
  不再复制任何角色资源；`res/` 目录留空由使用者自己准备。
- 本项目不托管素材、不提供素材下载地址、不针对任何具体游戏打包素材。

## 2. 素材版权不属于本项目

角色资源的版权属于各自的权利人。例如，《NIKKE（胜利女神）》相关的
Spine 骨架、图集、贴图、图标等素材，版权归 **Shift Up Corp.** 及其关联方所有。

这些素材：

- **不在本项目的 GPL-3.0-or-later 授权范围内**；
- 本项目**无权**为你授权、也无权允许你分发或商用它们；
- 使用者需自行判断其使用行为是否符合素材权利人的要求与所在地区的法律。

## 3. 使用者的责任

- 请自行准备你需要渲染的资源文件（`.skel` / `.atlas` / `.png`）。
- 请自行确认这些文件的来源合法、且在你所在地区允许以你的方式使用。
- 本程序自带的导入/解包工具（UnityFS bundle 导入、图标下载）只用于处理
  **你自己合法持有的文件**，它不会替你寻找、下载或解锁任何内容。

## 4. 关于 nikke-db

本项目使用的角色素材来自该项目**公开发布**的资源（L2D、精灵图及其索引），
并非本项目自行解包所得；这些素材同样不随本项目分发，版权仍归各自权利人。
（当时使用的辅助脚本与 `resources/nikkedb/` 本地镜像都是本机工具，未纳入本仓库。）

nikke-db（https://github.com/Nikke-db/Nikke-db.github.io）是一个长期免费、无广告、
无赞助入口的社区项目。**它同样不拥有游戏素材**，本项目在此仅作来源致谢，
不表示它对本项目有任何授权或背书。



## 5. 免费承诺

本项目自身**永久免费**分发：没有广告、没有赞助入口、没有付费墙、
不卖播放器、不卖素材，也不把素材放到任何形式的付费门槛后面。

## 6. English summary

SpinePet is only a player, not content. This repository and its release packages
contain **no game assets**: `res/` is git-ignored and the packaging script no
longer copies any character resources. Character assets remain the property of
their respective rights holders (for NIKKE-related assets, Shift Up Corp.);
they are **not** covered by this project's GPL-3.0-or-later license, and this
project has no authority to license them to you. You must supply your own
assets and satisfy yourself that your use is lawful where you are. The bundled
import/unpack tools process only the files you already have and do not locate,
download, or unlock any content. The project itself is free forever: no ads, no
donations, no paywall. Credits to nikke-db for its public asset mirror and
indexes used in this repository's preview/audit tooling; nikke-db does not own
the game assets either and does not endorse this project.
