# Character resource layout migration

The application organizes all standing skins and their icons for one official
character under a single directory:

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

The migration reads character display names from
`src/SpinePet/Data/CharacterNames.json`. It reorganizes legacy
`res/<character>/{standing,icons}` resource sets according to each skeleton's
`c<character-code>_<skin-code>` prefix. Atlas page names are read from the
`.atlas` file, so textures such as `page-one.png` move with their skeleton even
when their names do not repeat the resource prefix.

Legacy `aim` and `cover` directories are outside the supported resource model.
The migration reports them but deliberately does not move or delete their
contents.

Preview the migration from the repository root:

```powershell
.\tools\resource-layout\Migrate-CharacterResources.ps1 -WhatIf
```

Run the migration only after the preview has no conflicts:

```powershell
.\tools\resource-layout\Migrate-CharacterResources.ps1
```

Use `-ResourceDirectory` and `-CharacterNamesPath` to operate on a non-default
resource tree and name map. The script performs a complete conflict check before
moving anything and refuses to overwrite an existing destination. Files with an
unrecognized prefix or a character code missing from the name map remain in
place with a warning. It also refuses to combine two character IDs in one
display-name and skin directory. After successful moves, it removes only legacy
`standing` or `icons` directories—and old character directories—that are
genuinely empty. A character directory that still contains legacy `aim` or
`cover` data is therefore preserved.
