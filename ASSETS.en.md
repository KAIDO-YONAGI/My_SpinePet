# Asset Sourcing and Licensing Boundaries

[中文](ASSETS.md) | **English**

In one sentence: **this project provides the player, not the content.**

## 1. The project distributes no game assets

SpinePet is a desktop pet shell: it renders Spine skeleton resources, and it
contains no character resources of its own.

- The source repository contains no assets: `SpinePet/res/` is git-ignored.
- Release packages contain no assets: the packaging flow
  (`tools/Package-Release.ps1`) emits only the application, the configuration
  template and the licensing files. It copies no character resources; `res\` is
  left empty for the user to fill.
- This project does not host assets, does not publish asset download links, and
  does not bundle assets for any specific game.

## 2. Asset copyright does not belong to this project

Character resources are owned by their respective rights holders. For example,
Spine skeletons, atlases, textures and icons related to *NIKKE (Goddess of
Victory)* are owned by **Shift Up Corp.** and its affiliates.

Such assets:

- are **not** covered by this project's GPL-3.0-or-later license;
- cannot be licensed to you by this project, and this project has no authority to
  permit you to redistribute or commercially use them;
- should be used only in ways you have determined to be acceptable to the rights
  holder and lawful where you are.

## 3. Your responsibility as a user

- Prepare the resource files you want to render (`.skel` / `.atlas` / `.png`)
  yourself.
- Satisfy yourself that those files come from a lawful source and that your use of
  them is permitted where you live.
- The bundled import/unpack tooling — UnityFS bundle import, icon download, and
  `tools/nikke-extract/extract_skillcut.py` — only processes files **you already
  hold**. It does not find, download or unlock any content for you.

## 4. Credits

- **nikke-db** (https://github.com/Nikke-db/Nikke-db.github.io) — a community
  project that has stayed free for years, with no ads and no donation links.
  Character naming and skin-code verification during development referred to the
  sprite and Live2D material it publishes; those assets are not owned by nikke-db
  either, and this credit implies no licence or endorsement from it. The helper
  script that consumed that local mirror is a local-only tool and is not part of
  this repository.
- **NikkeTools** by FZFalzar/svatyvabin (MIT, 2022) — the NKAB decryption routine
  in `tools/nikke-extract/extract_skillcut.py` is ported from its `Program.cs`.
- See [THIRD_PARTY_NOTICES.en.md](THIRD_PARTY_NOTICES.en.md) for the complete
  component and licence inventory.

## 5. Free-forever commitment

This project's own code is distributed **free forever**: no ads, no donation
entry points, no paywall, no selling of the player, no selling of assets, and no
placing of assets behind any kind of paid gate.

## 6. 中文摘要

SpinePet 只提供播放器，不提供内容。本仓库与发行包均不含任何游戏素材（`res/`
已在 `.gitignore` 中忽略，打包脚本也不再复制任何角色资源）。角色素材版权归各自
权利人（NIKKE 相关素材归 Shift Up Corp.），不在本项目 GPL-3.0-or-later 授权范围内，
本项目无权为你授权。你需要自行准备素材并确认其来源与用途在你所在地区合法；内置的
导入/解包工具只处理你自己持有的文件，不会替你寻找、下载或解锁任何内容。本项目永久
免费：无广告、无赞助、无付费墙。
