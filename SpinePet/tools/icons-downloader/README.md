# Character icon downloader


`Update-CharacterIcons.ps1` matches local standing Spine resources such as
`c017_00.skel` to the corresponding `icons-char-mi(hd)` bundle in
`internal_ids.json`. It then:

1. reads the latest `dp` BaseUri from the NIKKE `Player.log`;
2. downloads the icon for one explicitly selected Skin;
3. decrypts each bundle with `NikkeAssetUnpacker`;
4. extracts the matching Unity sprite with UnityPy;
5. writes the PNG into the matching skin's `icons` directory.

Encrypted and decrypted working files are also kept under the selected `res`
directory while the command runs, then removed. The downloader does not use a
system-drive cache for icon assets.

The application looks for `<character-code>_<skin-code>_icon.png` in each skin's
`icons` directory. Add automatically invokes this downloader for the newly
imported standing Skin when its icon is missing. If that best-effort download
fails, the standing import remains available and its texture is used as the
card fallback:

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

Only skeletons inside a `standing` directory are used to build the download
plan. Legacy `aim` and `cover` resources are ignored.

## Requirements

- `internal_ids.json` beside the script
- `NikkeAssetUnpacker.exe` and its `Keys` directory under
  `tools/NikkeAssetUnpacker`
- Python with the packages listed in `../requirements.txt`

Install the Python dependencies:

```powershell
python -m pip install -r ..\requirements.txt
```

Every invocation must bind exactly one resource ID to its exact Skin directory.
The script never discovers or processes the whole `res` tree. It scans only the
selected directory's direct `standing` files and refuses paths outside `res` or
paths containing junctions or symbolic links:

```powershell
.\Update-CharacterIcons.ps1 `
    -ResourceId c010_02 `
    -TargetSkinDirectory E:\SpinePet\res\Rapi\02
```

Add `-ListOnly` to preview that one exact match without downloading. For a
selected batch, invoke the command once per selected Skin; omission of either
selection argument is an error.

Replace existing cached icons:

```powershell
.\Update-CharacterIcons.ps1 `
    -ResourceId c010_02 `
    -TargetSkinDirectory E:\SpinePet\res\Rapi\02 `
    -Force
```

Exit SpinePet before using `-Force` so WPF is not displaying the files being
replaced. After downloading missing icons, restart the app or use the panel's
Scan button to refresh the cards.

The BaseUri normally comes from the latest matching line in `Player.log`. It can
also be supplied explicitly after a game update:

```powershell
.\Update-CharacterIcons.ps1 `
    -ResourceId c010_02 `
    -TargetSkinDirectory E:\SpinePet\res\Rapi\02 `
    -BaseUri 'https://cloud.nikke-kr.com/.../pck/dp/.../'
```

`internal_ids.json`, the unpacker package, downloaded bundles, decrypted bundles,
and the entire `res` directory are local-only and must not be committed.
