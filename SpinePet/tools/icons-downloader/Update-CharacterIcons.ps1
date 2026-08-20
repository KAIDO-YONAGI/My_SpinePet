[CmdletBinding()]
param(
    [string]$PlayerLogPath,
    [string]$CatalogPath,
    [string]$ResourceDirectory,
    [string]$NauPath,
    [string]$PythonCommand = 'python',
    [string]$BaseUri,
    [string[]]$ResourceId = @(),
    [string]$TargetSkinDirectory,
    [switch]$Force,
    [switch]$ListOnly
)

$ErrorActionPreference = 'Stop'

$repositoryDirectory = Split-Path (
    Split-Path $PSScriptRoot -Parent
) -Parent
if ([string]::IsNullOrWhiteSpace($PlayerLogPath)) {
    $PlayerLogPath = Join-Path $env:USERPROFILE `
        'AppData\LocalLow\com.proximabeta\NIKKE\Player.log'
}
if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $PSScriptRoot 'internal_ids.json'
}
if ([string]::IsNullOrWhiteSpace($ResourceDirectory)) {
    $ResourceDirectory = Join-Path $repositoryDirectory 'res'
}
if ([string]::IsNullOrWhiteSpace($NauPath)) {
    $NauPath = Join-Path (
        Split-Path $PSScriptRoot -Parent
    ) 'NikkeAssetUnpacker\NikkeAssetUnpacker.exe'
}

function Get-DatapackBaseUri {
    param(
        [string]$LogPath
    )

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        throw "Player log was not found: $LogPath"
    }

    $pattern =
        'CatalogGroup\(\)\s+BaseUri:\s+' +
        '(?<uri>https://[^,\s]+/pck/dp/[^,\s]+/?)'
    $latestUri = $null
    foreach ($match in Select-String -LiteralPath $LogPath -Pattern $pattern) {
        $latestUri = $match.Matches[0].Groups['uri'].Value
    }

    if ([string]::IsNullOrWhiteSpace($latestUri)) {
        throw "No dp CatalogGroup BaseUri was found in: $LogPath"
    }

    return $latestUri.TrimEnd('/') + '/'
}

function Get-CharacterResources {
    param(
        [string]$Root
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Character resource directory was not found: $Root"
    }

    $resources = [System.Collections.Generic.List[object]]::new()
    $pendingDirectories = [System.Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push([System.IO.Path]::GetFullPath($Root))

    while ($pendingDirectories.Count -gt 0) {
        $currentPath = $pendingDirectories.Pop()
        Assert-NotReparsePoint -Path $currentPath
        $currentDirectory = Get-Item -LiteralPath $currentPath -Force

        if ([string]::Equals(
            $currentDirectory.Name,
            'standing',
            [System.StringComparison]::OrdinalIgnoreCase
        )) {
            $skinDirectory = $currentDirectory.Parent
            if ($null -eq $skinDirectory) {
                Write-Warning (
                    'Unable to resolve the Skin directory for: ' +
                    $currentDirectory.FullName
                )
                continue
            }

            foreach (
                $skeleton in Get-ChildItem -LiteralPath $currentPath -File `
                    -Filter '*.skel' -Force
            ) {
                Assert-NotReparsePoint -Path $skeleton.FullName
                if ($skeleton.BaseName -match '^(?<id>c\d+_[^_]+)') {
                    $resources.Add([pscustomobject]@{
                        ResourceId = $Matches['id'].ToLowerInvariant()
                        CharacterDirectory = $skinDirectory.FullName
                    })
                }
            }

            # Resource files must be directly inside standing. Do not descend
            # into arbitrary directories below a resource set.
            continue
        }

        foreach (
            $childDirectory in Get-ChildItem -LiteralPath $currentPath `
                -Directory -Force
        ) {
            Assert-NotReparsePoint -Path $childDirectory.FullName
            $pendingDirectories.Push($childDirectory.FullName)
        }
    }

    return @(
        $resources |
            Sort-Object -Property ResourceId, CharacterDirectory -Unique
    )
}

function Get-NormalizedDirectoryPath {
    param(
        [string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals(
        $fullPath,
        $pathRoot,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        return $fullPath
    }

    return $fullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
}

function Assert-NotReparsePoint {
    param(
        [string]$Path
    )

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Resource path contains a reparse point: $($item.FullName)"
    }
}

function Assert-SafePathChain {
    param(
        [string]$Root,
        [string]$Path
    )

    $resolvedRoot = Get-NormalizedDirectoryPath -Path $Root
    $resolvedPath = Get-NormalizedDirectoryPath -Path $Path
    $rootPrefix = if (
        $resolvedRoot.EndsWith(
            [System.IO.Path]::DirectorySeparatorChar.ToString(),
            [System.StringComparison]::Ordinal
        ) -or
        $resolvedRoot.EndsWith(
            [System.IO.Path]::AltDirectorySeparatorChar.ToString(),
            [System.StringComparison]::Ordinal
        )
    ) {
        $resolvedRoot
    }
    else {
        $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    }
    if ([string]::Equals(
        $resolvedPath,
        $resolvedRoot,
        [System.StringComparison]::OrdinalIgnoreCase
    ) -or -not $resolvedPath.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw (
            "Target Skin directory must be strictly inside the resource " +
            "directory: $resolvedPath"
        )
    }

    Assert-NotReparsePoint -Path $resolvedRoot
    $relativePath = $resolvedPath.Substring($rootPrefix.Length)
    $currentPath = $resolvedRoot
    foreach ($segment in ($relativePath -split '[\\/]')) {
        if ([string]::IsNullOrWhiteSpace($segment)) {
            continue
        }

        $currentPath = Join-Path $currentPath $segment
        if (-not (Test-Path -LiteralPath $currentPath)) {
            break
        }

        Assert-NotReparsePoint -Path $currentPath
    }
}

function Get-TargetCharacterResource {
    param(
        [string]$Root,
        [string]$TargetDirectory,
        [string]$RequestedResourceId
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Character resource directory was not found: $Root"
    }

    $resolvedRoot = Get-NormalizedDirectoryPath -Path $Root
    $resolvedTarget = Get-NormalizedDirectoryPath -Path $TargetDirectory
    Assert-SafePathChain -Root $resolvedRoot -Path $resolvedTarget
    if (-not (Test-Path -LiteralPath $resolvedTarget -PathType Container)) {
        throw "Target Skin directory was not found: $resolvedTarget"
    }

    $standingDirectory = Join-Path $resolvedTarget 'standing'
    Assert-SafePathChain -Root $resolvedRoot -Path $standingDirectory
    if (-not (Test-Path -LiteralPath $standingDirectory -PathType Container)) {
        throw (
            'The target Skin does not contain a standing directory: ' +
            $resolvedTarget
        )
    }

    # Validate an existing destination before ListOnly or any write. A missing
    # icons directory is created later after its parent chain has been checked.
    $iconDirectory = Join-Path $resolvedTarget 'icons'
    Assert-SafePathChain -Root $resolvedRoot -Path $iconDirectory

    $matchingSkeletons = @(
        Get-ChildItem -LiteralPath $standingDirectory -File -Filter '*.skel' `
            -Force |
            Where-Object {
                [string]::Equals(
                    $_.BaseName,
                    $RequestedResourceId,
                    [System.StringComparison]::OrdinalIgnoreCase
                )
            }
    )
    if ($matchingSkeletons.Count -ne 1) {
        throw (
            "Target Skin must contain exactly one standing skeleton named " +
            "'$RequestedResourceId.skel'; found $($matchingSkeletons.Count)."
        )
    }

    Assert-NotReparsePoint -Path $matchingSkeletons[0].FullName

    return [pscustomobject]@{
        ResourceId = $RequestedResourceId
        CharacterDirectory = $resolvedTarget
    }
}

function Get-IconBundles {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Internal ID catalog was not found: $Path"
    }

    $records = Get-Content -LiteralPath $Path -Encoding utf8 -Raw |
        ConvertFrom-Json
    $bundlePattern =
        '\\(?<bundle>icons-char-mi\(hd\)_assets_mi_' +
        '(?<id>c\d+_[^_]+)_s_[a-f0-9]+\.bundle)$'
    $bundles = @{}

    foreach ($record in $records) {
        $internalId = [string]$record.internal_id
        if ($internalId -notmatch $bundlePattern) {
            continue
        }

        $resourceId = $Matches['id'].ToLowerInvariant()
        if ($bundles.ContainsKey($resourceId)) {
            throw "Multiple HD icon bundles were found for $resourceId."
        }

        $bundles[$resourceId] = $Matches['bundle']
    }

    return $bundles
}

function Remove-WorkingDirectory {
    param(
        [string]$Path,
        [string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $directorySeparator = [System.IO.Path]::DirectorySeparatorChar
    $alternateSeparator = [System.IO.Path]::AltDirectorySeparatorChar
    $resolvedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd(
        $directorySeparator,
        $alternateSeparator
    )
    $rootPrefix = $resolvedRoot + $directorySeparator
    if (-not $resolvedPath.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing to remove a directory outside ${resolvedRoot}: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

$requestedResourceIds = [System.Collections.Generic.List[string]]::new()
$requestedResourceIdSet = @{}
foreach ($resourceIdValue in $ResourceId) {
    foreach ($candidate in (([string]$resourceIdValue) -split ',')) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            throw (
                'ResourceId cannot be empty. Use IDs such as c010_02.'
            )
        }

        $normalizedResourceId = $candidate.Trim().ToLowerInvariant()
        if ($normalizedResourceId -notmatch '^c\d+_[^_]+$') {
            throw (
                "Invalid ResourceId '$candidate'. Expected a character and " +
                'skin ID such as c010_02.'
            )
        }

        if (-not $requestedResourceIdSet.ContainsKey($normalizedResourceId)) {
            $requestedResourceIdSet[$normalizedResourceId] = $true
            $requestedResourceIds.Add($normalizedResourceId)
        }
    }
}

$ResourceDirectory = Get-NormalizedDirectoryPath -Path $ResourceDirectory
$hasTargetSkinDirectory = -not [string]::IsNullOrWhiteSpace(
    $TargetSkinDirectory
)
if ($hasTargetSkinDirectory -and $requestedResourceIds.Count -ne 1) {
    throw (
        'TargetSkinDirectory requires exactly one ResourceId so the ' +
        'destination Skin can be verified.'
    )
}

if ($hasTargetSkinDirectory) {
    $resolvedTargetSkinDirectory = Get-NormalizedDirectoryPath `
        -Path $TargetSkinDirectory
    $characterResources = @(
        Get-TargetCharacterResource `
            -Root $ResourceDirectory `
            -TargetDirectory $resolvedTargetSkinDirectory `
            -RequestedResourceId $requestedResourceIds[0]
    )
}
else {
    $allCharacterResources = @(
        Get-CharacterResources -Root $ResourceDirectory
    )
    $characterResources = $allCharacterResources
}

if (-not $hasTargetSkinDirectory -and $requestedResourceIds.Count -gt 0) {
    $localResourceIds = @(
        $allCharacterResources |
            Select-Object -ExpandProperty ResourceId -Unique
    )
    $unknownResourceIds = @(
        $requestedResourceIds |
            Where-Object { $_ -notin $localResourceIds }
    )
    if ($unknownResourceIds.Count -gt 0) {
        throw (
            'Requested ResourceId was not found in a local standing ' +
            "directory: $($unknownResourceIds -join ', ')"
        )
    }

    $characterResources = @(
        $allCharacterResources |
            Where-Object {
                $requestedResourceIdSet.ContainsKey($_.ResourceId)
            }
    )
}

$iconBundles = Get-IconBundles -Path $CatalogPath
if ($requestedResourceIds.Count -gt 0) {
    $missingBundleResourceIds = @(
        $requestedResourceIds |
            Where-Object { -not $iconBundles.ContainsKey($_) }
    )
    if ($missingBundleResourceIds.Count -gt 0) {
        throw (
            'No HD icon bundle was found for requested ResourceId: ' +
            ($missingBundleResourceIds -join ', ')
        )
    }
}

$resolvedBaseUri = if ([string]::IsNullOrWhiteSpace($BaseUri)) {
    Get-DatapackBaseUri -LogPath $PlayerLogPath
}
else {
    $BaseUri.TrimEnd('/') + '/'
}

$downloadPlan = @(
    foreach ($resource in $characterResources) {
        $planResourceId = $resource.ResourceId
        if (-not $iconBundles.ContainsKey($planResourceId)) {
            Write-Warning "No HD icon bundle was found for $planResourceId."
            continue
        }

        [pscustomobject]@{
            ResourceId = $planResourceId
            BundleName = $iconBundles[$planResourceId]
            Uri = $resolvedBaseUri + $iconBundles[$planResourceId]
            OutputPath = Join-Path (
                Join-Path $resource.CharacterDirectory 'icons'
            ) "${planResourceId}_icon.png"
        }
    }
)

Write-Output "Datapack BaseUri: $resolvedBaseUri"
if ($hasTargetSkinDirectory) {
    Write-Output "Target Skin directory: $resolvedTargetSkinDirectory"
}
$downloadPlan |
    Select-Object ResourceId, BundleName, OutputPath |
    Format-Table -AutoSize

if ($ListOnly) {
    Write-Output "ListOnly: matched $($downloadPlan.Count) icon(s)."
    return
}

if (-not (Test-Path -LiteralPath $NauPath -PathType Leaf)) {
    throw "NikkeAssetUnpacker was not found: $NauPath"
}

$extractorPath = Join-Path $PSScriptRoot 'extract_icon.py'
if (-not (Test-Path -LiteralPath $extractorPath -PathType Leaf)) {
    throw "UnityPy extractor was not found: $extractorPath"
}

& $PythonCommand -c 'import UnityPy, PIL'
if ($LASTEXITCODE -ne 0) {
    $requirementsPath = Join-Path $PSScriptRoot '..\requirements.txt'
    throw (
        "Python dependencies are missing. Run: " +
        "$PythonCommand -m pip install -r " +
        "`"$requirementsPath`""
    )
}

$workingDirectory = Join-Path $ResourceDirectory (
    'SpinePet-Icons-' + [Guid]::NewGuid().ToString('N')
)
New-Item -ItemType Directory -Path $workingDirectory | Out-Null

$downloaded = 0
$skipped = 0
$failures = [System.Collections.Generic.List[string]]::new()

try {
    foreach ($item in $downloadPlan) {
        if ((Test-Path -LiteralPath $item.OutputPath) -and -not $Force) {
            Write-Output "SKIP $($item.ResourceId): icon already exists."
            $skipped++
            continue
        }

        try {
            $encryptedPath = Join-Path $workingDirectory $item.BundleName
            Write-Output "DOWNLOAD $($item.ResourceId): $($item.Uri)"
            Invoke-WebRequest -Uri $item.Uri -OutFile $encryptedPath `
                -UseBasicParsing

            & $NauPath $encryptedPath
            if ($LASTEXITCODE -ne 0) {
                throw "NAU exited with code $LASTEXITCODE."
            }

            $decryptedName =
                [System.IO.Path]::GetFileNameWithoutExtension(
                    $item.BundleName
                ) + '_dec.bundle'
            $decryptedPath = Join-Path $workingDirectory $decryptedName
            if (-not (Test-Path -LiteralPath $decryptedPath -PathType Leaf)) {
                throw "NAU did not produce a decrypted bundle."
            }

            $temporaryIconPath = Join-Path (
                $workingDirectory
            ) "$($item.ResourceId)_icon.png"
            & $PythonCommand $extractorPath `
                --bundle $decryptedPath `
                --resource-id $item.ResourceId `
                --output $temporaryIconPath
            if ($LASTEXITCODE -ne 0 -or
                -not (Test-Path -LiteralPath $temporaryIconPath -PathType Leaf)
            ) {
                throw "UnityPy icon extraction failed."
            }

            $iconDirectory = Split-Path -Parent $item.OutputPath
            New-Item -ItemType Directory -Path $iconDirectory -Force |
                Out-Null
            Move-Item -LiteralPath $temporaryIconPath `
                -Destination $item.OutputPath -Force
            Write-Output "SAVED $($item.ResourceId): $($item.OutputPath)"
            $downloaded++
        }
        catch {
            $message = "$($item.ResourceId): $($_.Exception.Message)"
            $failures.Add($message)
            Write-Warning $message
        }
    }
}
finally {
    Remove-WorkingDirectory `
        -Path $workingDirectory `
        -AllowedRoot $ResourceDirectory
}

Write-Output (
    "Done. downloaded=$downloaded skipped=$skipped failed=$($failures.Count)"
)
if ($failures.Count -gt 0) {
    throw "Icon update failed: $($failures -join '; ')"
}
