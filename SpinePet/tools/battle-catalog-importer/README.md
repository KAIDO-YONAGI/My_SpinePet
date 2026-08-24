# Battle Catalog Importer

Audits and imports only the explicitly named complete Spine 4.1
Standing/Aim/Cover sets into `SpinePet/res`. A source directory list is
required; full-catalog import is intentionally disabled.

Run from the workspace root:

```powershell
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- --audit resources\Characters "Red Hood Variant 02" "c872"
dotnet run --project SpinePet\tools\battle-catalog-importer\BattleCatalogImporter.csproj -c Release -- resources\Characters SpinePet\res "Red Hood Variant 02" "c872"
```

The final arguments are exact direct-directory names under
`resources/Characters`; quote names containing spaces. The audit requires all
three exact states. Each state must contain a readable
Spine 4.1 skeleton, atlas, and every referenced texture page. Named alternatives
such as `Aim (Chinese Censored Version)` are ignored. Complete sets are mapped to
an existing target skin when possible; otherwise a new independent character ID
is allocated and matching resource and atlas page prefixes are rewritten.

The import is idempotent. A repeated run reports complete existing sets as
`AlreadyPresent` and does not create duplicate character cards. Incomplete,
unreadable, or non-4.1 battle resources are reported in `SkippedEntries` and do
not receive a Battle configuration.
