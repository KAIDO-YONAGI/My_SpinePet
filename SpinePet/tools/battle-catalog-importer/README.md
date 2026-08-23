# Battle Catalog Importer

Audits `resources/Characters` and imports every complete Spine 4.1
Standing/Aim/Cover set into `SpinePet/res`.

Run from the workspace root:

```powershell
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- --audit resources\Characters
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- resources\Characters SpinePet\res
```

The audit requires all three exact states. Each state must contain a readable
Spine 4.1 skeleton, atlas, and every referenced texture page. Named alternatives
such as `Aim (Chinese Censored Version)` are ignored. Complete sets are mapped to
an existing target skin when possible; otherwise a new independent character ID
is allocated and matching resource and atlas page prefixes are rewritten.

The import is idempotent. A repeated run reports complete existing sets as
`AlreadyPresent` and does not create duplicate character cards. Incomplete,
unreadable, or non-4.1 battle resources are reported in `SkippedEntries` and do
not receive a Battle configuration.
