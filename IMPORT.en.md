# SpinePet Asset Import Guide

[中文](IMPORT.md) | **English**

A user-facing guide to importing assets. The **authoritative specification** for
development and automated pipelines lives in
[`Y_MultipleAgentWorkflow/Resources/Load/SpinePet_Resources_Load_Guide.md`](Y_MultipleAgentWorkflow/Resources/Load/SpinePet_Resources_Load_Guide.md)
(favorites additionally in
[`Favorite_Interactive_Import_Guide.md`](Y_MultipleAgentWorkflow/Resources/Load/Favorite_Interactive_Import_Guide.md),
cleanup in [`SpineResource_Match_Clean_Guide.md`](Y_MultipleAgentWorkflow/Resources/MatchClean/SpineResource_Match_Clean_Guide.md),
the battle state model in [`Aim_Cover_Proposal.md`](Y_MultipleAgentWorkflow/Resources/StateSupport/Aim_Cover_Proposal.md)).
This document is a **user-facing summary** of those; where they disagree, the
specification wins.

## Complete worked example: one import, start to finish

> Sections 0–7 are the rules; **this section is a walkthrough you can follow directly**.
> Every resulting path below comes from real resources on this machine and can be
> checked against `SpinePet\res` directly.

### The shared skeleton (identical for all four kinds)

```text
(1) Intake     zip → zip-intake script; folders → move into resources\Characters\<normalised name>
(2) List       write down exactly which resources this run touches (never scan the whole library)
(3) Pre-check  Spine 4.1? all three files present? identity and new ID? icon source?
(4) Write      res\<full resource name>\<skin code>\<state>\
(5) Register   add the new ID to CharacterNames.json → only takes effect after dotnet build
(6) Verify     exit the app → write → start it → Scan → see section 5
```

### Example 1: standing (the easy path, in-app Add)

- **Input**: `c472_00.skel` + `c472_00.atlas` + `c472_00.png` (same Spine 4.1 export, same base name)
- **Action**: pick `c472_00.skel` with **Add**; the app validates skeleton/atlas/textures and
  writes **transactionally**, taking the display name from `CharacterNames.json`
  (`472 → Scarlet Overload`), then tries to fetch that skin's icon
- **Result** (real):

```text
res\Scarlet Overload\00\standing\c472_00.skel
res\Scarlet Overload\00\standing\c472_00.atlas
res\Scarlet Overload\00\standing\c472_00.png
res\Scarlet Overload\00\icons\c472_00_icon.png
```

- **Verify**: press `Scan` → the `Scarlet Overload` card appears, default animation `idle`,
  scale 100% / 1.0×, hidden by default (never auto-shown)

### Example 2: a standing variant (you must renumber: `191_02` → `19102`)

- **Input**: source resource `Alice Variant 02`, skeleton originally `c191_02_00.skel`
  (source character ID `191`, skin `02`)
- **Action**:
  1. New ID = `191` + `02` = `19102`; **only the character digit field changes, the skin
     number keeps its source value `02`**
  2. `c191_02_00.skel` → `c19102_02_00.skel`, and rename the same-named `.atlas` too
  3. Rename the **page PNG names declared in the atlas header** and the texture files
     accordingly — page names follow the atlas declaration and **need not equal the
     skeleton base name**: this set has skeleton `c19102_02_00.skel` but pages
     `c19102_02.png` and `c19102_02_2.png` (multi-page atlases get `_2`, `_3` suffixes)
  4. Add `"19102": "Alice Variant 02"` to `CharacterNames.json`
  5. `dotnet build src\SpinePet\SpinePet.csproj -c Release`
- **Result** (real):

```text
res\Alice Variant 02\02\standing\c19102_02_00.skel
res\Alice Variant 02\02\standing\c19102_02_00.atlas
res\Alice Variant 02\02\standing\c19102_02.png        <- page 1 as declared by the atlas
res\Alice Variant 02\02\standing\c19102_02_2.png      <- page 2 as declared by the atlas
res\Alice Variant 02\02\icons\c19102_02_icon.png
```

### Example 3: battle (pairwise, audit before import)

- **Input**: the `standing` / `aim` / `cover` sets of one skin (example: `Anis Star`, ID `0170`)
- **Commands**:

```powershell
# (1) audit only — writes nothing
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  --audit resources\Characters "Anis Star"

# (2) import once every audited item is confirmed
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  resources\Characters SpinePet\res "Anis Star"
```

- **What the audit output looks like** (fields as observed): `CompleteSets` carries
  `Identity: ResourceName=c0170_aim, CharacterCode=0170, SkinCode=aim, DisplayName=Anis Star`,
  and only entries with `Standing` / `Aim` / `Cover` all present are counted
- **Result** (real):

```text
res\Anis Star\00\standing\c0170_00.skel / .atlas / .png
res\Anis Star\00\aim\c0170_aim_00.skel / .atlas / .png
res\Anis Star\00\cover\c0170_cover_00.skel / .atlas / .png
res\Anis Star\00\icons\c0170_00_icon.png
```

- **Note**: with only one side present (for example aim alone) **do not import it** — it stays
  an ordinary standing character. Single-state resources (favorites) are skipped by the audit
  outright: specifying `Bay Favorite` put it in `SkippedEntries` (aim/cover both false) in testing.

### Example 4: burst (zip intake → take Lobby → renumber)

- **Input zip**: `PC _ Computer - Goddess of Victory_ Nikke - Burst - Helm_ Aquamarine.zip`
- **Command**:

```powershell
pwsh -NoProfile -File 'SpinePet\tools\zip-intake\Import-ResourceZip.ps1' -Zip '<full path to zip>'
```

- **Observed output** (the script run against that real zip in a temporary directory):

```text
=== skeleton set analysis ===
  Battle\Sprite Sheet and Other Assets\c353_00_skillcut.skel  character 353  atlas:OK
  Lobby\Sprite Sheet and Other Assets\c353_00_skillcut.skel   character 353  atlas:OK

archived: resources\Characters\Helm - Aquamarine Burst\Aquamarine
zip moved to: resources\zips
```

- **Then**: Battle and Lobby skillcut are identical → **take Lobby**; the directory name ends
  with ` Burst`; renumbering follows Example 2
- **Result** (the real Cinderella set; source `515_00` → local `5150`):

```text
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.skel
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.atlas
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.png
res\Cinderella Crystal Wave Burst\00\standing\c5150_00_skillcut.attachments.exclude   (only if cleaned)
res\Cinderella Crystal Wave Burst\00\icons\c5150_00_icon.png
CharacterNames.json: "5150": "Cinderella Crystal Wave Burst"
```

- **Note**: a skillcut `idle` may show only the upper body — that is the source design, not an
  import error

### Example 5: favorite (prefix the local ID with 9)

- **Input**: `favorite_c072_00.skel` + `.atlas` + `.png` (original number `072` = Diesel)
- **Action**:
  1. Local ID = `9` + `072` = `9072`, skin always `00`, display name `Diesel Favorite`
  2. Rename atlas page references and the texture to `c9072_00.png`
  3. Look the icon up with the **original** number `si_c072_00_s.png`, copy it as `c9072_00_icon.png`
  4. Add `"9072": "Diesel Favorite"` to `CharacterNames.json`
- **Result** (real):

```text
res\Diesel Favorite\00\standing\c9072_00.skel
res\Diesel Favorite\00\standing\c9072_00.atlas
res\Diesel Favorite\00\standing\c9072_00.png
res\Diesel Favorite\00\icons\c9072_00_icon.png
```

- **Note**: never enters battle and gets no aim/cover directories; its resident animation is
  exactly `idle_merged`, and clicking falls back to `expression_merged`

### Verifying everything once the imports are done

```powershell
# per resource: are all texture pages declared by the atlas present? (spec section 5 script)
$dir = 'SpinePet\res\<full resource name>\<skin code>\standing'
Get-Content "$dir\<resource>.atlas" |
  Where-Object { $_.Trim() -match '\.(png|jpg|jpeg|webp)$' } |
  Select-Object -Unique | ForEach-Object {
    if (-not (Test-Path "$dir\$($_.Trim())")) { Write-Error "Missing atlas page: $($_.Trim())" } }
```

Then start SpinePet → `Scan` → confirm per card: name, icon, default `idle`, click animation and
drag bounds.

## 0. Three things to remember first

1. The application accepts exactly one layout: `res\<full resource name>\<skin code>\<state>\`,
   and skeleton file names must carry the `c<character ID>_<skin ID>` prefix.
2. **Raw game exports are almost never directly usable** — directory names,
   character IDs, atlas page references and icon sources all have to be changed.
   That is the real source of the "importing is inconvenient" complaint (section 2).
3. For a resource to be picked up by Scan it must contain, in one directory:
   `<resource>.skel` + a **same-named** `.atlas` + **every** texture page the atlas
   references, all exported by the same **Spine 4.1.x**. If anything is missing it
   stays in `resources\` and must not enter `res\`.

## 1. The four resource kinds

| Kind | Directory signature | Skeleton / animations | How to import | Key notes |
| --- | --- | --- | --- | --- |
| **Standing** | `<skin>\standing\` | `idle` as the resident animation | In-app **Add** (`.skel` or UnityFS bundle), or place files by hand | The ordinary case |
| **Battle** aim / cover | `<skin>\aim\` + `<skin>\cover\` | both required to build Battle | `battle-catalog-importer` (**explicit list required**) | One missing side ⇒ plain standing character; **Add does not accept aim/cover** |
| **Burst** skillcut | directory name ends with ` Burst` | close-up framing | same as standing | Battle and Lobby skillcut are usually byte-identical, so **prefer Lobby**; `idle` may show only the upper body — that is the source design, not an import error |
| **Favorite** | directory name ends with ` Favorite`; source file `favorite_cNNN_00` | usually only `idle` and `expression_merged` | same as standing | Local ID is always `9NNN`, skin always `00`; **never enters Battle**, produces no aim/cover; resident animation is exactly `idle_merged`, click falls back to `expression_merged` |

### 1.1 Standing

```text
res\<full resource name>\<skin code>\standing\
  c<character ID>_<skin ID>.skel
  c<character ID>_<skin ID>.atlas
  <every texture page referenced by the atlas>.png
  c<character ID>_<skin ID>.attachments.exclude   (optional, only if cleaned)
```

### 1.2 Battle (aim / cover)

Battle is generated only when **both Aim and Cover are complete** for the same
skin; standing-only, or only one side completed, stays an ordinary Normal
character. Audit first, then write:

```powershell
# audit only (writes nothing)
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  --audit resources\Characters "<resource directory name>" "<another directory name>"

# import once every audited item is confirmed
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- `
  resources\Characters SpinePet\res "<resource directory name>" "<another directory name>"
```

The directory list is **mandatory** and must name exact direct children of
`resources\Characters`. The audit recognises only exactly named
`Standing`/`Aim`/`Cover` directories; variants such as
`Aim (Chinese Censored Version)` will not be mixed into the main resource.

### 1.3 Burst (skillcut)

- Skeleton pick order: **Standing > Lobby > Battle**; use Standing when present.
- Battle and Lobby skillcut are usually identical, so prefer Lobby.
- skillcut is close-up framing; `idle` may only cover the upper body — normal.
- The directory name ends with ` Burst`, e.g. `Cinderella Crystal Wave Burst`.

### 1.4 Favorite

- Source file names look like `favorite_cNNN_00`; take the three-digit original
  number `NNN` from the file name.
- **Local ID = `9NNN`** (prefix a `9`), skin is always `00`, display name is
  `<Name> Favorite`. Example: `favorite_c072_00` → Diesel → local `9072` →
  `res\Diesel Favorite\00\standing\c9072_00.skel`.
- Look the icon up with the **original** number (`si_c072_00_s.png`); never query
  by the `9NNN` ID — that number exists only in this project.
- These resources carry only `idle` and `expression_merged`; they are
  single-state interactive resources.

## 2. What has to be changed in raw exports

| Item | Rule |
| --- | --- |
| **Directory name** | Must be the **full resource name** (character + skin/variant), Burst ending with ` Burst`; **never just the character name**; each variant of a character gets its own directory |
| **File name prefix** | `c<character ID>_<skin ID>` — the application identifies and de-duplicates by this prefix |
| **Character ID** | **Always allocate a fresh ID on import**: new ID = `<source character ID><skin number>` concatenated (`191_02` → `19102`, `260_80` → `26080`, `515_00` → `5150`); only the character digit field changes, the **skin number keeps its source value** (`c191_02_00.skel` → `c19102_02_00.skel`) |
| **Atlas** | Rename the page PNG references in the atlas header and the `.attachments.exclude` file name to match |
| **CharacterNames.json** | Add an entry for the new ID with the display name = full resource name (Burst ends with ` Burst`); the change **only takes effect after a rebuild** (`dotnet build src\SpinePet\SpinePet.csproj -c Release`) |
| **Forbidden** | Do not invent codes like `00cut`; never let two skeletons share one prefix or one display name — **duplicate names make a model unopenable** |
| **Icons** | Use the square index image, not part textures, and not the game's tall portrait art (if height > width × 1.25 you picked the wrong source) |

Icon lookup location and order (queried offline from the nikkedb evidence store;
the application never touches it at runtime):

```text
resources\nikkedb\github-repository\images\sprite\
  si_c<original character ID>_<skin ID>_00_s.png  →  _s  →  _00  →  no suffix
```

Copy the hit to `<skin directory>\icons\<resource prefix>_icon.png`; when a skin
has no dedicated icon, fall back to the base icon `si_c<original ID>_00_s.png`.
**If no icon can be located, do not import the body first** — report
`MissingIcon` instead of patching it up indefinitely afterwards.

The legacy layout `res\<character>\standing\<custom name>.skel` is still scannable
but **must not be added to**; migrate it with
`SpinePet\tools\resource-layout\Migrate-CharacterResources.ps1` (preview with
`-WhatIf` first).

## 3. Import tooling shipped with the project

| Tool | Path | Purpose |
| --- | --- | --- |
| In-app **Add** | configuration panel | Imports a standing `.skel` or UnityFS bundle (file name must start with `c<ID>_<skin ID>_<standing\|icons>_`); transactional, conflicts are never overwritten, failures roll back |
| In-app **Scan / Folder** | configuration panel | Re-scans `res\` after manual changes; opens the current resource directory (created automatically if missing) |
| One-step zip intake | `SpinePet\tools\zip-intake\Import-ResourceZip.ps1` | Unzip → normalise directory names → archive into `resources\Characters\` → file the zip into `resources\zips\` → print a skeleton/atlas/missing-ID report |
| Battle audit and import | `SpinePet\tools\battle-catalog-importer` | Audits and pairwise-imports Standing/Aim/Cover for an explicit list (see 1.2) |
| Icon download | `SpinePet\tools\icons-downloader\Update-CharacterIcons.ps1` | Accepts exactly **one** ResourceId and **one** target skin per run; never scans the whole library |
| Atlas cleanup and masking | `SpinePet\tools\atlas-cleaner\Clean-Atlas.ps1` | Removes background/effect layers from atlases and applies texture masking (**opt-in**, off by default) |
| Legacy layout migration | `SpinePet\tools\resource-layout\Migrate-CharacterResources.ps1` | Migrates the legacy layout; supports `-WhatIf` |
| Skeleton attachment inspector | `SpinePet\tools\skeleton-inspector` | Inspects attachments/timelines, useful for click-region and animation problems |

UnityFS import needs the Python packages:

```powershell
python -m pip install -r SpinePet\tools\requirements.txt
```

## 4. Recommended flow (spec: explicit list → intake → pre-check → import → verify)

1. **Decide the list**: exactly which resources this run touches. Write it down.
2. **Intake**: zips via `Import-ResourceZip.ps1`; folders are **moved** (not copied)
   into `resources\Characters\名称` after renaming
   `YYYY-MM-DD__名称 [cNNN_NN]` → `名称`.
3. **Pre-check**: skeleton completeness (`.skel` + `.atlas` + all textures), Spine
   version, identity and target ID, icon source — all four **before** writing to `res`.
4. **Import**: standing / burst / favorite go into `res\` per section 1; battle
   follows the audit-then-import pair in 1.2.
5. **Verify**: see the next section; new cards default to `Visible = false`, scale
   100% / 1.0×.

**Close SpinePet before overwriting existing files in `res`** (Windows file locks
cause `Device or resource busy`).

## 5. Verification and troubleshooting

Run the atlas page completeness check first (spec section 5):

```powershell
$dir = 'SpinePet\res\<full resource name>\<skin code>\standing'
Get-Content "$dir\<resource>.atlas" |
  Where-Object { $_.Trim() -match '\.(png|jpg|jpeg|webp)$' } |
  Select-Object -Unique | ForEach-Object {
    if (-not (Test-Path "$dir\$($_.Trim())")) {
      Write-Error "Missing atlas page: $($_.Trim())" } }
```

Then press **Scan** in the app and confirm the new card appears, with the right
name, the right icon and `idle` as its default animation.

If it does not show up, check in this order:

1. Is it inside `SpinePet\res\` (not `resources\` or an index directory)?
2. Do `.skel` and `.atlas` share the **same name**?
3. Are all texture pages declared by the atlas present?
4. Is the skeleton **Spine 4.1.x**?
5. Does the file name contain the `c<character ID>_<skin ID>` prefix?
6. Is the character ID present in `CharacterNames.json`, and did you **rebuild**
   after changing it?
7. Is it under `<full resource name>\<skin code>\standing`?
8. Is the same character+skin prefix occupied by two skeletons (duplicates get
   de-duplicated and the card is swallowed)?

## 6. AI-assisted import (recommended)

Importing is really "**read the spec → rename and renumber → verify → run commands
→ read reports**" repeated, and every mistake lives in the details (ID
concatenation, atlas page names, icon source, duplicate names) — which makes it a
good fit for an AI assistant, with you confirming the judgement calls.

**Hand these requirements to the AI:**

1. Read
   `Y_MultipleAgentWorkflow/Resources/Load/SpinePet_Resources_Load_Guide.md` in
   full first; for favorites also read `Favorite_Interactive_Import_Guide.md`.
2. Follow **explicit list → intake → body-and-icon pre-check → import record →
   verify** strictly, and **never** scan or import the whole library.
3. Show **actual commands and their output** as evidence for every step — no
   "it should have imported fine" conclusions.
4. **Ask you to confirm** anything judgemental (new character ID, display name,
   icon source); never guess an ID from a name.
5. When done, run the atlas check from section 5 and report per new card: name /
   ID / whether the ID is in `CharacterNames.json` / default animation / icon path.

**Do not let the AI:** enumerate unspecified sibling directories; use `res` as a
cleanup playground (cleanup happens on staging copies under `resources\`);
overwrite files while the application is running; or query nikkedb with a `9NNN` ID.

## 7. Known pitfalls

- **Duplicate names break models**: two skeletons sharing one
  `c<ID>_<skin ID>` prefix or one display name get de-duplicated and the card is
  swallowed.
- **Do not touch the skin number**: a new ID changes only the character digit
  field; the skin number must keep its source value.
- **Spine version**: must be 4.1.x. Known case: one resource's battle skeleton was
  `4.0.47`, incompatible with the 4.1 runtime, so it had to be skipped.
- **Rebuild after editing `CharacterNames.json`**, or the name will not apply.
- **File locks**: overwriting files in `res` while the app runs fails — exit it first.
- **Favorites never enter Battle**: a `Favorite` resource is single-state; do not
  build aim/cover for it.
- **Cleanup is optional**: off by default; if you do it, work on a staging copy,
  prefer `.attachments.exclude` over texture masking, and never delete `*_eyebg`
  (eye whites).
