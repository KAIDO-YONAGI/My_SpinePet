# Removes atlas regions whose names do not appear in the matching skeleton.
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$Folder,

    [bool]$CreateBackup = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
    throw "Character directory does not exist: $Folder"
}

function Test-IsAtlasEntryHeader {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Line
    )

    if (
        [string]::IsNullOrWhiteSpace($Line) -or
        [char]::IsWhiteSpace($Line[0])
    ) {
        return $false
    }

    $candidate = $Line.Trim()
    return $candidate -notmatch ':'
}

function Test-IsRegionHeader {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Line
    )

    return (
        (Test-IsAtlasEntryHeader -Line $Line) -and
        $Line.Trim() -notmatch '\.(png|jpe?g|webp)$'
    )
}

function Invoke-AtlasCleanup {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$SkeletonFile,

        [Parameter(Mandatory)]
        [System.IO.FileInfo]$AtlasFile
    )

    $skeletonBytes =
        [System.IO.File]::ReadAllBytes($SkeletonFile.FullName)
    $skeletonText =
        [System.Text.Encoding]::UTF8.GetString($skeletonBytes)
    $lines = [System.IO.File]::ReadAllLines($AtlasFile.FullName)
    $newLines = [System.Collections.Generic.List[string]]::new()
    $removed = 0
    $index = 0

    while ($index -lt $lines.Count) {
        $line = $lines[$index]
        $trimmed = $line.Trim()

        if (
            (Test-IsRegionHeader -Line $line) -and
            $skeletonText.IndexOf(
                $trimmed,
                [System.StringComparison]::Ordinal
            ) -lt 0
        ) {
            Write-Host "  DEL: $trimmed"
            $removed++
            $index++

            while (
                $index -lt $lines.Count -and
                -not (Test-IsAtlasEntryHeader -Line $lines[$index])
            ) {
                $index++
            }

            continue
        }

        [void]$newLines.Add($line)
        $index++
    }

    if ($removed -gt 0) {
        if ($PSCmdlet.ShouldProcess(
            $AtlasFile.FullName,
            "Remove $removed unused atlas region(s)"
        )) {
            if ($CreateBackup) {
                $backupPath = "$($AtlasFile.FullName).bak"
                if (-not (Test-Path -LiteralPath $backupPath)) {
                    Copy-Item `
                        -LiteralPath $AtlasFile.FullName `
                        -Destination $backupPath
                }
            }

            [System.IO.File]::WriteAllLines(
                $AtlasFile.FullName,
                $newLines
            )
        }
    }

    Write-Host "  $($SkeletonFile.BaseName): removed=$removed"
}

$processed = 0
foreach (
    $skeletonFile in Get-ChildItem `
        -LiteralPath $Folder `
        -File `
        -Filter '*.skel' |
        Sort-Object Name
) {
    $atlasPath =
        Join-Path $Folder "$($skeletonFile.BaseName).atlas"
    $textureFiles = @(
        Get-ChildItem `
            -LiteralPath $Folder `
            -File `
            -Filter "$($skeletonFile.BaseName)*.png" |
            Where-Object {
                -not $_.BaseName.EndsWith(
                    '_icon',
                    [System.StringComparison]::OrdinalIgnoreCase
                )
            }
    )
    if (
        -not (Test-Path -LiteralPath $atlasPath -PathType Leaf) -or
        $textureFiles.Count -eq 0
    ) {
        Write-Host "  $($skeletonFile.BaseName): SKIP missing files"
        continue
    }

    Invoke-AtlasCleanup `
        -SkeletonFile $skeletonFile `
        -AtlasFile (Get-Item -LiteralPath $atlasPath)
    $processed++
}

if ($processed -eq 0) {
    Write-Host '  SKIP: no complete resource sets'
}
