# My_SpinePet

[中文](README.md) | **English**

A Windows desktop pet application: it renders Spine characters through the native
Spine 4.1 C# runtime, Direct3D 11 and DirectComposition, with a WPF panel for
importing characters, choosing animations, scaling and positioning.

The application **only renders; it ships no game assets**. The project is free
forever: no ads, no donations, no paywall.

## Documentation

| Document | 中文 | English |
| --- | --- | --- |
| Usage (this file) | [README.md](README.md) | **README.en.md** |
| Asset policy | [ASSETS.md](ASSETS.md) | [ASSETS.en.md](ASSETS.en.md) |
| Third-party components and licenses | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) | [THIRD_PARTY_NOTICES.en.md](THIRD_PARTY_NOTICES.en.md) |
| Project license / copyright | [LICENSE](LICENSE), [NOTICE](NOTICE) | same files (English) |
| Developer documentation | [SpinePet/README.md](SpinePet/README.md) | same file (English) |
| Spine JSON version differences (reference) | [docs/spine-version-differences.md](docs/spine-version-differences.md) | same file (中文) |
| End-user tips shipped in the release | [UserTips.txt](UserTips.txt) | 中文 only for now |

## Contents

- [Requirements](#requirements)
- [Quick start](#quick-start)
- [First run: prepare and import assets](#first-run-prepare-and-import-assets)
- [Desktop interaction](#desktop-interaction)
- [Configuration panel](#configuration-panel)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Resource directory layout](#resource-directory-layout)
- [Portable layout, data location and uninstall](#portable-layout-data-location-and-uninstall)
- [Building and packaging from source](#building-and-packaging-from-source)
- [Troubleshooting](#troubleshooting)
- [Assets and licensing](#assets-and-licensing)

## Requirements

- Windows 10 or later.
- Using a release package: no .NET installation needed — the self-contained
  `win-x64` build already includes the runtime.
- Building from source: .NET 9 SDK.
- Character resources: `.skel` / `.atlas` / `.png` exported from Spine 4.1, or a
  UnityFS bundle that follows the naming rules below.
- Importing UnityFS bundles with `Add` also needs a local Python environment
  (UnityPy + Pillow).

## Quick start

1. Unzip the release package, run `Launch.bat`, or start `app\SpinePet.exe`
   directly.
2. Double-click the tray icon (or use the tray menu `Open Panel`) to open the
   configuration panel.
3. **The first launch shows an empty character library** — release packages
   contain no assets at all, so import your own resources as described in the
   next section.
4. Closing the window / `Alt+F4` / `Finish Configuration` only hides the panel
   and saves settings; the pet keeps running. To exit, use the tray menu `Exit`
   or `Ctrl+Alt+Shift+F12`.

## First run: prepare and import assets

### Option A: import with `Add` (recommended)

- `Add` accepts a single `.skel` file, or a UnityFS bundle whose file name starts
  with `c<character-id>_<skin-id>_<standing|icons>_`.
- A `standing` bundle is fully unpacked — skeleton, atlas and every referenced
  texture — into that skin's `standing\` directory, and the application then
  tries to download that skin's official icon.
- Importing an icon bundle on its own only writes the icon; it does not create a
  character card.
- UnityFS import needs the Python packages:
  `python -m pip install -r SpinePet\tools\requirements.txt`.

### Option B: drop files in manually

Place resources in `res\` using the layout below, then press `Scan`:

```text
res\
  <character name>\
    <skin code>\           00 may be used when a skin code is absent
      standing\
        <resource name>.skel
        <resource name>.atlas
        <resource name>.png
      icons\               optional
        <resource name>_icon.png
```

Resource names use the form `c<character code>_<skin code>`; display names are
resolved from `SpinePet\src\SpinePet\Data\CharacterNames.json`, and every
character shows up as one card in the panel.

- `Scan` reconciles cards and saved configuration with the complete resources
  currently on disk — use it after adding, removing or replacing resources by hand.
- Right-click a card (or select it and press `Shift+F10`) to switch between the
  skins available for that character.
- `Delete Current Skin` asks for confirmation and then sends that skin directory
  to the Windows Recycle Bin.
- Icons are optional; a failed icon download does not undo a `standing` import —
  the panel reports the problem and falls back to the standing texture as the
  thumbnail.
- `SpinePet\tools\battle-catalog-importer` audits and imports complete Aim/Cover
  sets in bulk, but it requires an explicit resource directory list; incomplete
  or non-4.1 pairs are skipped.

### Option C: burst (skill cut-in) animations

`tools\nikke-extract\extract_skillcut.py` is an auxiliary extraction pipeline for
NIKKE burst animations. It scans an `eb` directory, decrypts `NKAB` bundles,
enumerates `skillcut` TextAssets through UnityPy and exports
`.skel` / `.atlas` / referenced textures as PNG into
`<out>\<cXXX[_skin]>\<cXXX[_skin]_skillcut>.{skel,atlas,png}`.

```powershell
py -3.12 tools\nikke-extract\extract_skillcut.py <game eb directory or game root> --out <output directory>
```

It requires Python 3.12 with `UnityPy`, `pycryptodome` and `Pillow`. **You must
supply your own game files** — the script locates, downloads and unlocks nothing.
Its NKAB decryption routine is ported from
[FZFalzar/svatyvabin NikkeTools](https://github.com/FZFalzar/svatyvabin) (MIT,
2022); see [THIRD_PARTY_NOTICES.en.md](THIRD_PARTY_NOTICES.en.md).

## Desktop interaction

| Action | Behaviour |
| --- | --- |
| Left click | Plays the character's click animation; keeps the current animation when there is no matching one |
| Hold left button and drag | Moves the character and saves its position (requires `Allow dragging in render mode`) |
| Right click on character (panel closed) | Opens the panel and selects/scrolls to that character |
| Right click on character (panel open) | Saves and closes the panel |

## Configuration panel

Left side — character library:

- Click a card to select a character; its details appear on the right.
- `Show` / `Hide` toggles visibility and switches the right side to that
  character; `Position` restores its default position. A white `Show/Hide`
  button means the character is currently visible; while `Loading` the action
  cannot be repeated.
- While scrolling, the right side follows the character in the centre of the
  viewport, in two-column order.
- Search matches both character names and skin information (`Ctrl+F` focuses it).
- Arrow keys select a character, `Enter` / `Space` toggles visibility, and
  right-clicking a card or pressing `Shift+F10` switches skins.
- The preview size slider adjusts card size between 50% and 150%.

Right side — settings:

- `Animation`: pick and loop one of the character's animations.
- `Desktop frame rate`: 30 / 60 / 120 FPS, applied globally.
- `Allow dragging in render mode`: whether desktop characters can be dragged,
  applied globally.
- `Scale`: the first value is a base ratio of 0%–100% (100% corresponds to a
  base scale of 0.2); the second is an independent 1.0–5.0 multiplier that does
  not change the first. Final scale = base ratio ÷ 100 × 0.2 × multiplier, still
  bounded by the character's maximum size. `Reset` restores 100% and 1.0.
- `Animation Speed`: 0.10–2.00×; `Reset` restores 1.00×.
- `Delete Current Skin`: sends the current skin directory to the Recycle Bin after
  confirmation.

Toolbar: `Add` (import), `Scan` (re-scan `res\`), `Folder` (open the current
`res\` directory; it is created automatically when missing).

## Keyboard shortcuts

| Shortcut | Effect |
| --- | --- |
| `Ctrl+F` | Focus the character search box |
| `↓` / `Enter` | Move from the search box into the selected result |
| `Esc` | Clear the current search query |
| `↑` / `↓` | Move the selection in the character list |
| `Enter` / `Space` | Show or hide the selected character |
| `Shift+F10` | Open the selected character's skin menu (same as right-clicking the card) |
| `Alt+F4` | Hide the configuration panel (does not exit) |
| `Ctrl+Alt+Shift+F12` | Emergency exit (when a render window interferes with normal input) |

## Resource directory layout

```text
<release package>\
  Launch.bat                 recommended entry point
  config.json                portable configuration (characters, visibility, position,
                             scale, animation, speed, global settings)
  res\                       character resource library (contains only a placeholder note; you supply assets)
  app\                       application payload and internal tools — do not rename
  Logs\                      runtime logs, useful for troubleshooting
  LICENSE / NOTICE           project license (GPL-3.0-or-later) and copyright notice
  THIRD_PARTY_NOTICES.md     third-party components and licenses
  ASSETS.md                  asset sourcing and licensing boundaries
  licenses\                  verbatim third-party license texts — do not delete
```

## Portable layout, data location and uninstall

- Portable: when `config.json` exists in a directory, configuration and logs are
  written there (for the `app\` subfolder layout, one level above it), so the
  whole folder can be moved anywhere.
- Without a portable configuration, data falls back to `%LOCALAPPDATA%\SpinePet`.
  Advanced: the `SPINEPET_DATA_DIRECTORY` environment variable overrides the data
  directory.
- If the configuration JSON is damaged, the original is preserved next to it as a
  timestamped `.corrupt` file before a clean configuration is created.
- Multiple instances: launching the same directory twice activates the existing
  instance; different build directories run independently side by side.
- Uninstall: just delete the whole portable folder.

## Building and packaging from source

The solution lives at `SpinePet\SpinePet.sln`. Run from the **repository root**:

```powershell
# build
dotnet restore SpinePet\SpinePet.sln
dotnet build SpinePet\SpinePet.sln -c Debug
dotnet build SpinePet\SpinePet.sln -c Release

# run the Debug build
dotnet run --project SpinePet\src\SpinePet\SpinePet.csproj

# package a portable release (produces release\<build name>\ and dist\<build name>.zip)
.\tools\Publish.bat
```

A release package contains **only the application, the configuration template and
the licensing files — no character resources**; `res\` is prepared by the user.

Auxiliary tool projects live in `SpinePet\tools\`: atlas cleanup, resource layout
migration, icon download, resource zip intake, skeleton attachment inspection and
bulk Aim/Cover import. See [SpinePet/README.md](SpinePet/README.md) for details.

## Troubleshooting

- **Panel will not open, or is hidden behind something?** `Alt+F4` only hides it;
  double-click the tray icon again, or start `SpinePet.exe` once more.
- **A render window interferes with input?** Press `Ctrl+Alt+Shift+F12` to exit.
- **A character does not show up?** Check the `res\` layout and that the files are
  complete (skeleton, atlas and texture are all required), then press `Scan`.
  Runtime logs are in `Logs\`.
- **An import is rejected?** Make sure the bundle name follows
  `c<character-id>_<skin-id>_<standing|icons>_` and that it was exported for
  Spine 4.1. If two character IDs would map to the same display name and skin
  directory, the import stops and asks you to fix `CharacterNames.json` first.
- **UI language**: the self-contained release keeps only the `zh-Hans` satellite
  resources.

## Assets and licensing

- The project is **free forever**: no ads, no donations, no paywall, and neither
  the player nor assets are for sale.
- The application's own code is licensed **GPL-3.0-or-later** (see `LICENSE` /
  `NOTICE`): anyone may use, modify and redistribute it, but **derivative versions
  may not be redistributed as closed-source paid software**.
- The project **contains no game assets**. Character assets remain the property of
  their rights holders (NIKKE-related assets belong to Shift Up Corp.), are not
  covered by this project's license, and this project has no authority to license
  them to you. See [ASSETS.en.md](ASSETS.en.md).
- The Spine runtime is a separate third-party component whose license requires
  **every user to hold their own Spine Editor license**. See
  [THIRD_PARTY_NOTICES.en.md](THIRD_PARTY_NOTICES.en.md).

---

Source: https://gitee.com/KAIDOYONAGI/my_-spine-pet
