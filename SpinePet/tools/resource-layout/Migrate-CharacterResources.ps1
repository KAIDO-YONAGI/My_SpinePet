[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$ResourceDirectory,
    [string]$CharacterNamesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resourceTypes = @('standing', 'icons')
$ignoredLegacyResourceTypes = @('aim', 'cover')
$ownershipResourceTypes = @($resourceTypes + $ignoredLegacyResourceTypes)
$pathComparer = [System.StringComparer]::OrdinalIgnoreCase

if ([string]::IsNullOrWhiteSpace($ResourceDirectory)) {
    $repositoryRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot '..\..')
    )
    $ResourceDirectory = Join-Path $repositoryRoot 'res'
}

if ([string]::IsNullOrWhiteSpace($CharacterNamesPath)) {
    $repositoryRoot = Split-Path $ResourceDirectory -Parent
    $CharacterNamesPath = Join-Path $repositoryRoot (
        'src\SpinePet\Data\CharacterNames.json'
    )
}

$ResourceDirectory = [System.IO.Path]::GetFullPath($ResourceDirectory)
$CharacterNamesPath = [System.IO.Path]::GetFullPath($CharacterNamesPath)
if (-not (Test-Path -LiteralPath $ResourceDirectory -PathType Container)) {
    throw "Resource directory does not exist: $ResourceDirectory"
}

if (-not (Test-Path -LiteralPath $CharacterNamesPath -PathType Leaf)) {
    throw "Character name map does not exist: $CharacterNamesPath"
}

$characterNames = Get-Content `
    -LiteralPath $CharacterNamesPath `
    -Encoding UTF8 `
    -Raw |
    ConvertFrom-Json
$invalidFileNameCharacters = [System.IO.Path]::GetInvalidFileNameChars()
$plans = [System.Collections.Generic.List[object]]::new()
$skinDirectories = [System.Collections.Generic.HashSet[string]]::new(
    $pathComparer
)
$legacyResourceDirectories = [System.Collections.Generic.HashSet[string]]::new(
    $pathComparer
)
$legacyCharacterDirectories =
    [System.Collections.Generic.HashSet[string]]::new($pathComparer)
$plannedSources = [System.Collections.Generic.HashSet[string]]::new(
    $pathComparer
)
$skinOwners = [System.Collections.Generic.Dictionary[string, string]]::new(
    $pathComparer
)

function Assert-NoReparsePoint {
    param(
        [string]$Path,
        [string]$Operation
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = $ResourceDirectory.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    $rootPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not [string]::Equals(
            $resolvedPath,
            $resolvedRoot,
            [System.StringComparison]::OrdinalIgnoreCase
        ) -and
        -not $resolvedPath.StartsWith(
            $rootPrefix,
            [System.StringComparison]::OrdinalIgnoreCase
        )
    ) {
        throw "Refusing to $Operation outside the resource root: $resolvedPath"
    }

    $current = [System.IO.DirectoryInfo]::new($resolvedPath)
    while ($null -ne $current -and
        -not [string]::Equals(
            $current.FullName.TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar
            ),
            $resolvedRoot,
            [System.StringComparison]::OrdinalIgnoreCase
        )
    ) {
        if ($current.Exists -and
            ($current.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
        ) {
            throw (
                "Refusing to $Operation through a reparse point: " +
                $current.FullName
            )
        }

        $current = $current.Parent
    }

    if ($null -eq $current) {
        throw "Refusing to $Operation outside the resource root: $resolvedPath"
    }
}

function Get-ResourceIdentity {
    param([string]$FileName)

    if ($FileName -notmatch '^c(?<code>\d+)_(?<skin>[^_.]+)(?=(_|\.|$))') {
        return $null
    }

    return [pscustomobject]@{
        CharacterCode = $Matches['code']
        SkinCode = $Matches['skin']
        ResourceName = $Matches[0]
    }
}

function Get-MappedDisplayName {
    param(
        [string]$CharacterCode,
        [string]$SourcePath
    )

    $nameProperty = $characterNames.PSObject.Properties[$CharacterCode]
    if ($null -eq $nameProperty -or
        [string]::IsNullOrWhiteSpace([string]$nameProperty.Value)
    ) {
        Write-Warning (
            "No CharacterNames.json entry for c$CharacterCode; " +
            "resource left in place: $SourcePath"
        )
        return $null
    }

    $rawDisplayName = [string]$nameProperty.Value
    $displayName = $rawDisplayName.Trim()
    $hasInvalidCharacter = $false
    foreach ($invalidCharacter in $invalidFileNameCharacters) {
        if ($displayName.IndexOf($invalidCharacter) -ge 0) {
            $hasInvalidCharacter = $true
            break
        }
    }

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($displayName)
    $isReservedName = $baseName -match (
        '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$'
    )
    if ($hasInvalidCharacter -or
        $isReservedName -or
        $displayName -in @('.', '..') -or
        $rawDisplayName -ne $displayName -or
        $displayName.EndsWith('.')
    ) {
        Write-Warning (
            "Invalid mapped character name '$rawDisplayName'; " +
            "resource left in place: $SourcePath"
        )
        return $null
    }

    return $displayName
}

function Assert-SkinOwnership {
    param(
        [string]$SkinDirectory,
        [string]$ExpectedCharacterCode
    )

    $resolvedSkinDirectory = [System.IO.Path]::GetFullPath($SkinDirectory)
    Assert-NoReparsePoint `
        -Path $resolvedSkinDirectory `
        -Operation 'inspect or create a skin directory'
    if (-not $skinOwners.ContainsKey($resolvedSkinDirectory)) {
        $existingCodes = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase
        )
        if (Test-Path -LiteralPath $resolvedSkinDirectory -PathType Container) {
            $directoriesToInspect = @($resolvedSkinDirectory)
            foreach ($resourceTypeToInspect in $ownershipResourceTypes) {
                $typeDirectory = Join-Path (
                    $resolvedSkinDirectory
                ) $resourceTypeToInspect
                if (Test-Path -LiteralPath $typeDirectory -PathType Container) {
                    $directoriesToInspect += $typeDirectory
                }
            }

            foreach ($directoryToInspect in $directoriesToInspect) {
                foreach (
                    $existingFile in Get-ChildItem `
                        -LiteralPath $directoryToInspect `
                        -Force `
                        -File
                ) {
                    $existingIdentity = Get-ResourceIdentity $existingFile.Name
                    if ($null -ne $existingIdentity) {
                        [void]$existingCodes.Add(
                            $existingIdentity.CharacterCode
                        )
                    }
                }
            }
        }

        if ($existingCodes.Count -gt 1) {
            throw (
                "Skin directory already mixes character IDs: " +
                "$resolvedSkinDirectory ($($existingCodes -join ', '))"
            )
        }

        if ($existingCodes.Count -eq 1) {
            $skinOwners[$resolvedSkinDirectory] = @($existingCodes)[0]
        }
        else {
            $skinOwners[$resolvedSkinDirectory] = $ExpectedCharacterCode
        }
    }

    $ownerCode = $skinOwners[$resolvedSkinDirectory]
    if (-not [string]::Equals(
        $ownerCode,
        $ExpectedCharacterCode,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw (
            "Character IDs c$ownerCode and c$ExpectedCharacterCode map to " +
            "the same skin directory: $resolvedSkinDirectory. Give them " +
            'unique display names in CharacterNames.json before migrating.'
        )
    }
}

function Add-ResourceSetPlan {
    param(
        [System.IO.FileInfo[]]$SourceFiles,
        [object]$Identity,
        [string]$ResourceType
    )

    $displayName = Get-MappedDisplayName `
        -CharacterCode $Identity.CharacterCode `
        -SourcePath $SourceFiles[0].FullName
    if ($null -eq $displayName) {
        return $false
    }

    $characterDirectory = Join-Path $ResourceDirectory $displayName
    $skinDirectory = Join-Path $characterDirectory $Identity.SkinCode
    Assert-SkinOwnership `
        -SkinDirectory $skinDirectory `
        -ExpectedCharacterCode $Identity.CharacterCode
    $destinationDirectory = Join-Path $skinDirectory $ResourceType

    foreach ($sourceFile in $SourceFiles) {
        if (-not $plannedSources.Add($sourceFile.FullName)) {
            throw (
                'A source file belongs to multiple resource sets and cannot ' +
                "be migrated safely: $($sourceFile.FullName)"
            )
        }

        $plans.Add([pscustomobject]@{
            Source = $sourceFile.FullName
            Destination = Join-Path $destinationDirectory $sourceFile.Name
            SkinDirectory = $skinDirectory
            ResourceType = $ResourceType
        })
    }

    [void]$skinDirectories.Add($skinDirectory)
    return $true
}

foreach (
    $legacyCharacterDirectory in Get-ChildItem `
        -LiteralPath $ResourceDirectory `
        -Force `
        -Directory |
        Sort-Object -Property FullName
) {
    Assert-NoReparsePoint `
        -Path $legacyCharacterDirectory.FullName `
        -Operation 'scan a legacy character directory'
    foreach ($ignoredResourceType in $ignoredLegacyResourceTypes) {
        $ignoredDirectory = Join-Path (
            $legacyCharacterDirectory.FullName
        ) $ignoredResourceType
        if (Test-Path -LiteralPath $ignoredDirectory -PathType Container) {
            Write-Warning (
                "Legacy '$ignoredResourceType' directory preserved " +
                "without changes: $ignoredDirectory"
            )
        }
    }

    foreach ($resourceType in $resourceTypes) {
        $legacyResourceDirectory = Join-Path (
            $legacyCharacterDirectory.FullName
        ) $resourceType
        if (-not (
            Test-Path -LiteralPath $legacyResourceDirectory -PathType Container
        )) {
            continue
        }

        Assert-NoReparsePoint `
            -Path $legacyResourceDirectory `
            -Operation 'scan a legacy resource directory'

        [void]$legacyResourceDirectories.Add($legacyResourceDirectory)
        [void]$legacyCharacterDirectories.Add(
            $legacyCharacterDirectory.FullName
        )

        foreach (
            $nestedDirectory in Get-ChildItem `
                -LiteralPath $legacyResourceDirectory `
                -Force `
                -Directory
        ) {
            Write-Warning (
                'Nested directory left in place: ' +
                $nestedDirectory.FullName
            )
        }

        $allFiles = @(
            Get-ChildItem `
                -LiteralPath $legacyResourceDirectory `
                -Force `
                -File |
                Sort-Object -Property Name
        )
        $claimedFiles = [System.Collections.Generic.HashSet[string]]::new(
            $pathComparer
        )

        if ($resourceType -eq 'icons') {
            foreach ($sourceFile in $allFiles) {
                $identity = Get-ResourceIdentity $sourceFile.Name
                if ($null -eq $identity) {
                    continue
                }

                if (Add-ResourceSetPlan `
                    -SourceFiles @($sourceFile) `
                    -Identity $identity `
                    -ResourceType $resourceType
                ) {
                    [void]$claimedFiles.Add($sourceFile.FullName)
                }
            }
        }
        else {
            $skeletonFiles = @(
                $allFiles |
                    Where-Object Extension -EQ '.skel'
            )
            foreach ($skeletonFile in $skeletonFiles) {
                $identity = Get-ResourceIdentity $skeletonFile.Name
                if ($null -eq $identity) {
                    continue
                }

                $atlasPath = [System.IO.Path]::ChangeExtension(
                    $skeletonFile.FullName,
                    '.atlas'
                )
                if (-not (Test-Path -LiteralPath $atlasPath -PathType Leaf)) {
                    Write-Warning (
                        'Incomplete resource set left in place; atlas missing: ' +
                        $skeletonFile.FullName
                    )
                    continue
                }

                $atlasFile = Get-Item -LiteralPath $atlasPath
                $setFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
                $setFiles.Add($skeletonFile)
                $setFiles.Add($atlasFile)
                $atlasPageNames = @(
                    Get-Content -LiteralPath $atlasPath -Encoding UTF8 |
                        ForEach-Object { $_.Trim() } |
                        Where-Object {
                            $_.EndsWith(
                                '.png',
                                [System.StringComparison]::OrdinalIgnoreCase
                            ) -and
                            [System.IO.Path]::GetFileName($_) -eq $_
                        } |
                        Select-Object -Unique
                )
                if ($atlasPageNames.Count -eq 0) {
                    $atlasPageFiles = @(
                        $allFiles |
                            Where-Object {
                                $_.Extension -eq '.png' -and
                                $_.BaseName.StartsWith(
                                    $identity.ResourceName,
                                    [System.StringComparison]::OrdinalIgnoreCase
                                ) -and
                                -not $_.BaseName.EndsWith(
                                    '_icon',
                                    [System.StringComparison]::OrdinalIgnoreCase
                                )
                            }
                    )
                }
                else {
                    $atlasPageFiles = @()
                    $missingPage = $false
                    foreach ($atlasPageName in $atlasPageNames) {
                        $pagePath = Join-Path (
                            $legacyResourceDirectory
                        ) $atlasPageName
                        if (-not (Test-Path -LiteralPath $pagePath -PathType Leaf)) {
                            Write-Warning (
                                "Incomplete resource set left in place; atlas " +
                                "page '$atlasPageName' missing for " +
                                $skeletonFile.FullName
                            )
                            $missingPage = $true
                            break
                        }

                        $atlasPageFiles += Get-Item -LiteralPath $pagePath
                    }

                    if ($missingPage) {
                        continue
                    }
                }

                if ($atlasPageFiles.Count -eq 0) {
                    Write-Warning (
                        'Incomplete resource set left in place; texture ' +
                        "missing: $($skeletonFile.FullName)"
                    )
                    continue
                }

                foreach ($atlasPageFile in $atlasPageFiles) {
                    $setFiles.Add($atlasPageFile)
                }

                if (Add-ResourceSetPlan `
                    -SourceFiles @($setFiles) `
                    -Identity $identity `
                    -ResourceType $resourceType
                ) {
                    foreach ($setFile in $setFiles) {
                        [void]$claimedFiles.Add($setFile.FullName)
                    }
                }
            }
        }

        foreach ($unclaimedFile in $allFiles) {
            if (-not $claimedFiles.Contains($unclaimedFile.FullName)) {
                Write-Warning (
                    'Unrecognized or incomplete file left in place: ' +
                    $unclaimedFile.FullName
                )
            }
        }
    }
}

$duplicateDestinations = @(
    $plans |
        Group-Object -Property Destination |
        Where-Object Count -gt 1
)
if ($duplicateDestinations.Count -gt 0) {
    throw (
        'Multiple files map to the same destination: ' +
        ($duplicateDestinations.Name -join ', ')
    )
}

$conflicts = @(
    $plans |
        Where-Object {
            Test-Path -LiteralPath $_.Destination
        }
)
if ($conflicts.Count -gt 0) {
    throw (
        'Migration would overwrite existing files: ' +
        (($conflicts | ForEach-Object Destination) -join ', ')
    )
}

if ($plans.Count -eq 0) {
    Write-Host 'No legacy character resources require migration.'
    return
}

Write-Host "Planned file moves: $($plans.Count)"
foreach ($plan in $plans) {
    Write-Host "  $($plan.Source) -> $($plan.Destination)"
}

foreach ($skinDirectory in $skinDirectories | Sort-Object) {
    Assert-NoReparsePoint `
        -Path $skinDirectory `
        -Operation 'create a skin directory'
    foreach ($resourceType in $resourceTypes) {
        $typeDirectory = Join-Path $skinDirectory $resourceType
        Assert-NoReparsePoint `
            -Path $typeDirectory `
            -Operation 'create a resource directory'
        if ($PSCmdlet.ShouldProcess(
            $typeDirectory,
            'Create skin resource directory'
        )) {
            New-Item `
                -ItemType Directory `
                -Path $typeDirectory `
                -Force |
                Out-Null
        }
    }
}

foreach ($plan in $plans) {
    if ($PSCmdlet.ShouldProcess(
        $plan.Source,
        "Move to $($plan.Destination)"
    )) {
        Move-Item `
            -LiteralPath $plan.Source `
            -Destination $plan.Destination
    }
}

foreach (
    $legacyResourceDirectory in $legacyResourceDirectories |
        Sort-Object -Descending
) {
    if (
        (Test-Path -LiteralPath $legacyResourceDirectory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $legacyResourceDirectory -Force).Count -eq 0 -and
        $PSCmdlet.ShouldProcess(
            $legacyResourceDirectory,
            'Remove empty legacy resource directory'
        )
    ) {
        Remove-Item -LiteralPath $legacyResourceDirectory
    }
}

foreach (
    $legacyCharacterDirectory in $legacyCharacterDirectories |
        Sort-Object -Descending
) {
    if (
        (Test-Path `
            -LiteralPath $legacyCharacterDirectory `
            -PathType Container
        ) -and
        @(
            Get-ChildItem `
                -LiteralPath $legacyCharacterDirectory `
                -Force
        ).Count -eq 0 -and
        $PSCmdlet.ShouldProcess(
            $legacyCharacterDirectory,
            'Remove empty legacy character directory'
        )
    ) {
        Remove-Item -LiteralPath $legacyCharacterDirectory
    }
}

Write-Host "Done. Migrated $($plans.Count) file(s)."
