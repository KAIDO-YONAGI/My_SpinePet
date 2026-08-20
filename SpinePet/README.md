# SpinePet

<img src="src/SpinePet/Assets/Brand/spinepet-avatar.png" width="128" alt="SpinePet white ghost avatar">

SpinePet is a Windows desktop pet application for Spine skeleton assets. It renders
characters through the native Spine 4.1 C# runtime, Direct3D 11, and
DirectComposition, while WPF provides the configuration panel for importing
characters, selecting animations, scaling, and positioning.

## Requirements

- Windows 10 or later
- .NET 9 SDK
- A Spine export containing matching `.skel`, `.atlas`, and `.png` files

## Repository layout

```text
src/SpinePet/
  Assets/Brand/                Application avatar and Windows icon
  Data/                       Embedded application data
  Infrastructure/             Paths and local diagnostics
    Import/Tools/             Bundled UnityFS extraction helper
  Models/                     Persisted application state
  Rendering/                  Renderer contract and implementation
  Rendering/Native/           D3D11/DirectComposition Spine renderer
  Services/                   Application services
  ViewModels/                 UI presentation state
  Views/                      WPF windows
src/SpineRuntime41/            Isolated Spine 4.1 runtime assembly
third_party/spine-csharp-4.1/  Licensed Spine runtime source
tools/atlas-cleaner/          Optional atlas maintenance utility
tools/icons-downloader/       Local character icon downloader
tools/resource-layout/        Legacy resource layout migration
tools/skeleton-inspector/     Spine skeleton attachment inspector
tools/zip-intake/             One-step resource zip intake
```

The local `res/` directory is intentionally ignored by Git because character packs
can be large and may have separate redistribution terms. Put each character in its
own display-name folder, then put every skin in its own skin-code folder. Each skin
has one standing resource directory and one icon directory:

```text
res/
  Anis Star/
    00/
      standing/
        c017_00.skel
        c017_00.atlas
        c017_00_FullNude.png
      icons/
        c017_00_icon.png
```

Resource file names use `c<character-code>_<skin-code>`. Character names are
resolved through `src/SpinePet/Data/CharacterNames.json`. The panel shows one card
per character. Right-click a card, or focus it and press Shift+F10, to choose an
available skin directly. Skin switching always uses that skin's `standing`
resource.

### Panel shortcuts

- `Ctrl+F` focuses character search.
- `Down` or `Enter` moves from search to the selected result; `Esc` clears the
  current query.
- Arrow keys select a character. `Enter` or `Space` shows or hides it, and
  `Shift+F10` opens its skin menu.
- `Alt+F4` hides the configuration panel without exiting. Reopen it from the
  tray icon or launch `SpinePet.exe` again.
- `Ctrl+Alt+Shift+F12` is the emergency exit shortcut if a render window
  interferes with normal input.

The configuration toolbar keeps **Open Folder** beside Add and Scan; it opens
the root `res/` directory. Scan reconciles cards and saved configuration with
complete resources currently present on disk, so a skin removed outside the app
does not leave a stale card. Deleting the selected skin requires confirmation
and sends that skin directory to the Windows Recycle Bin. The card switches to
another available skin, or disappears when its final skin is deleted.

Character cards prefer
`<skin-code>/icons/<resource-name>_icon.png` under the character directory.
After Add imports a standing bundle, SpinePet automatically downloads and
extracts a missing icon for that Skin. Icon download failure does not undo the
standing import; the panel reports the problem and continues using the standing
texture as a fallback. `tools/icons-downloader/Update-CharacterIcons.ps1`
remains available for manual repair or bulk updates.

The Add button accepts an existing `.skel` file or a UnityFS bundle whose file
name starts with:

```text
c<character-id>_<skin-id>_<standing|icons>_
```

UnityFS imports require Python, UnityPy, and Pillow. Install the same dependencies
used by the icon downloader:

```powershell
python -m pip install -r .\tools\requirements.txt
```

For standing bundles, the importer extracts a complete skeleton, atlas, and all
referenced atlas textures directly into the matching skin's `standing`
directory, then attempts to download that Skin's separate official icon bundle.
Manually supplied icon bundles use the icon extractor and write the resulting
PNG into the matching skin's `icons` directory. An icon-only import does not
create a character card. Aim and cover bundles are not imported or used. Imports refuse
to overwrite existing character resources, reject Spine exports other than 4.1,
and stop if two character IDs would share the same display-name and skin
directory. Correct `CharacterNames.json` or export the bundle with Spine 4.1
before retrying.

## Build

```powershell
dotnet restore
dotnet build SpinePet.sln -c Debug
dotnet build SpinePet.sln -c Release
```

Run the Debug build:

```powershell
dotnet run --project src/SpinePet/SpinePet.csproj
```

Runtime configuration and diagnostics are stored under
`%LOCALAPPDATA%\SpinePet`. If the configuration JSON is damaged, SpinePet
preserves the original beside it as a timestamped `.corrupt` file before
creating a clean configuration.

The white ghost avatar in `src/SpinePet/Assets/Brand` is the shared brand source
for the panel, executable, window, and tray icon. The exported PNG and ICO
files in `src/SpinePet/Assets/Brand` are the committed source of truth;
replace them there if the avatar ever changes.

## Third-party components

- Esoteric Software `spine-csharp` 4.1
- Vortice.Windows 3.8.3

Spine runtime usage is subject to the license in
`third_party/spine-csharp-4.1/LICENSE`.
