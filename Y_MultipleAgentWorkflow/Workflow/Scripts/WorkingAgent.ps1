[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Acquire', 'Heartbeat', 'UpdateScope', 'Wait', 'Release', 'Status')]
    [string]$Action,

    [string]$RegistryPath = (Join-Path $PSScriptRoot '..\..\WorkingAgent'),
    [string]$WorkspaceRoot = (Join-Path $PSScriptRoot '..\..\..'),
    [string]$AgentName,
    [string]$Model,
    [string]$SessionId,
    [string]$ParentLeaseId,
    [string]$TaskType,
    [string[]]$Categories,
    [string]$Summary,
    [string]$AccessMode,
    [string[]]$Resources,
    [string]$LeaseId,
    [switch]$OverrideConflict,
    [switch]$ConfirmBuildBarrierOverride,
    [switch]$ConfirmStaleRemoval,
    [ValidateRange(1, 3600)]
    [int]$PollSeconds = 60,
    [ValidateRange(1, 1440)]
    [int]$WaitTimeoutMinutes = 30,
    [ValidateRange(1, 120)]
    [int]$LockTimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExitSuccess = 0
$script:ExitConflict = 2
$script:ExitStaleConflict = 3
$script:ExitFailure = 4
$script:ExitWaitTimeout = 5
$script:StaleAfter = [TimeSpan]::FromMinutes(15)
$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$script:RegistryFullPath = [IO.Path]::GetFullPath($RegistryPath)
$script:WorkspaceFullPath = [IO.Path]::GetFullPath($WorkspaceRoot)
$script:LockPath = Join-Path $script:RegistryFullPath '_registry.lock'

function Complete-Result {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][int]$Code
    )

    [Console]::Out.WriteLine(($Value | ConvertTo-Json -Depth 12 -Compress))
    exit $Code
}

function New-Result {
    param(
        [bool]$Success,
        [string]$ResultAction,
        [string]$Message,
        $Lease = $null,
        $Conflicts = @(),
        $Leases = @(),
        $Errors = @()
    )

    [ordered]@{
        success = $Success
        action = $ResultAction
        message = $Message
        lease = $Lease
        conflicts = @($Conflicts | Where-Object { $null -ne $_ })
        leases = @($Leases | Where-Object { $null -ne $_ })
        errors = @($Errors | Where-Object { $null -ne $_ })
        timestampUtc = [DateTime]::UtcNow.ToString('o')
    }
}

function Expand-List {
    param([string[]]$Values)

    $expanded = [Collections.Generic.List[string]]::new()
    foreach ($value in @($Values)) {
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }

        foreach ($part in ($value -split ',')) {
            $trimmed = $part.Trim()
            if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                $expanded.Add($trimmed)
            }
        }
    }

    @($expanded | Select-Object -Unique)
}

function ConvertTo-UtcIso {
    param(
        $Value,
        [switch]$AllowNull
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        if ($AllowNull) {
            return $null
        }
        throw 'A required UTC timestamp is missing.'
    }

    if ($Value -is [DateTime]) {
        return ([DateTime]$Value).ToUniversalTime().ToString('o')
    }

    $parsed = [DateTime]::MinValue
    if (-not [DateTime]::TryParse(
        [string]$Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor
            [Globalization.DateTimeStyles]::AdjustToUniversal,
        [ref]$parsed
    )) {
        throw "Invalid UTC timestamp '$Value'."
    }

    $parsed.ToUniversalTime().ToString('o')
}

function Normalize-Categories {
    param([string[]]$Values)

    @(Expand-List $Values | ForEach-Object { $_.Trim() } | Select-Object -Unique)
}

function Normalize-PathValue {
    param([string]$Value)

    $pathValue = $Value.Trim()
    if (-not [IO.Path]::IsPathRooted($pathValue)) {
        $pathValue = Join-Path $script:WorkspaceFullPath $pathValue
    }

    $fullPath = [IO.Path]::GetFullPath($pathValue)
    $root = [IO.Path]::GetPathRoot($fullPath)
    if (-not $fullPath.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
        $fullPath = $fullPath.TrimEnd('\', '/')
    }

    $fullPath
}

function Normalize-Resources {
    param([string[]]$Values)

    $normalized = [Collections.Generic.List[string]]::new()
    foreach ($value in (Expand-List $Values)) {
        if ($value.StartsWith('path:', [StringComparison]::OrdinalIgnoreCase)) {
            $pathValue = $value.Substring(5)
            if ([string]::IsNullOrWhiteSpace($pathValue)) {
                throw 'A path resource cannot be empty.'
            }

            $normalized.Add("path:$(Normalize-PathValue $pathValue)")
        }
        else {
            $normalized.Add($value.Trim())
        }
    }

    @($normalized | Select-Object -Unique)
}

function Test-AccessMode {
    param([string]$Value)

    if ($Value -notin @('read', 'write', 'exclusive')) {
        throw "AccessMode must be read, write, or exclusive. Received '$Value'."
    }
}

function Enter-RegistryLock {
    if (-not [IO.Directory]::Exists($script:RegistryFullPath)) {
        [void][IO.Directory]::CreateDirectory($script:RegistryFullPath)
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($LockTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            return [IO.File]::Open(
                $script:LockPath,
                [IO.FileMode]::OpenOrCreate,
                [IO.FileAccess]::ReadWrite,
                [IO.FileShare]::None
            )
        }
        catch [IO.IOException] {
            Start-Sleep -Milliseconds 50
        }
    }

    throw "Timed out acquiring registry lock '$($script:LockPath)'."
}

function Read-LeasesLocked {
    $leases = [Collections.Generic.List[object]]::new()
    $errors = [Collections.Generic.List[object]]::new()

    foreach ($file in [IO.Directory]::EnumerateFiles($script:RegistryFullPath, '*.txt')) {
        try {
            $json = [IO.File]::ReadAllText($file, $script:Utf8NoBom)
            $lease = $json | ConvertFrom-Json
            if ($null -eq $lease.leaseId -or $null -eq $lease.status) {
                throw 'Missing leaseId or status.'
            }

            $lease | Add-Member -NotePropertyName registryFile -NotePropertyValue $file -Force
            $leases.Add($lease)
        }
        catch {
            $errors.Add([ordered]@{
                file = $file
                error = $_.Exception.Message
            })
        }
    }

    [ordered]@{
        leases = @($leases)
        errors = @($errors)
    }
}

function Write-LeaseLocked {
    param([Parameter(Mandatory)]$Lease)

    $target = [string]$Lease.registryFile
    $persisted = [ordered]@{
        schemaVersion = [int]$Lease.schemaVersion
        leaseId = [string]$Lease.leaseId
        agentName = [string]$Lease.agentName
        model = [string]$Lease.model
        sessionId = [string]$Lease.sessionId
        parentLeaseId = $Lease.parentLeaseId
        taskType = [string]$Lease.taskType
        categories = @($Lease.categories)
        summary = [string]$Lease.summary
        accessMode = [string]$Lease.accessMode
        resources = @($Lease.resources)
        status = [string]$Lease.status
        startedAtUtc = ConvertTo-UtcIso $Lease.startedAtUtc
        heartbeatAtUtc = ConvertTo-UtcIso $Lease.heartbeatAtUtc
        conflictingLeaseIds = @($Lease.conflictingLeaseIds)
        overrideApprovedAtUtc = ConvertTo-UtcIso $Lease.overrideApprovedAtUtc -AllowNull
    }

    $Lease.startedAtUtc = $persisted.startedAtUtc
    $Lease.heartbeatAtUtc = $persisted.heartbeatAtUtc
    $Lease.overrideApprovedAtUtc = $persisted.overrideApprovedAtUtc

    $temp = "$target.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText(
            $temp,
            ($persisted | ConvertTo-Json -Depth 8),
            $script:Utf8NoBom
        )
        [IO.File]::Move($temp, $target, $true)
    }
    finally {
        if ([IO.File]::Exists($temp)) {
            [IO.File]::Delete($temp)
        }
    }
}

function Get-HeartbeatAge {
    param($Lease)

    $heartbeat = [DateTime]::MinValue
    if (-not [DateTime]::TryParse(
        [string]$Lease.heartbeatAtUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor
            [Globalization.DateTimeStyles]::AdjustToUniversal,
        [ref]$heartbeat
    )) {
        return [TimeSpan]::MaxValue
    }

    [DateTime]::UtcNow - $heartbeat
}

function Test-SuspectedStale {
    param($Lease)
    (Get-HeartbeatAge $Lease) -ge $script:StaleAfter
}

function Test-IsWriteLike {
    param([string]$Mode)
    $Mode -in @('write', 'exclusive')
}

function Test-PathOverlap {
    param(
        [string]$Left,
        [string]$Right
    )

    if ($Left.Equals($Right, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $leftPrefix = $Left.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $rightPrefix = $Right.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $Left.StartsWith($rightPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $Right.StartsWith($leftPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Test-CategoryOverlap {
    param(
        [string[]]$Left,
        [string[]]$Right
    )

    foreach ($leftValue in @($Left)) {
        foreach ($rightValue in @($Right)) {
            if ($leftValue.Equals($rightValue, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    $false
}

function Test-HasResource {
    param(
        [string[]]$Values,
        [string]$Expected
    )

    foreach ($value in @($Values)) {
        if ($value.Equals($Expected, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    $false
}

function Test-LeaseConflict {
    param(
        $Candidate,
        $Existing
    )

    if ([string]$Candidate.leaseId -eq [string]$Existing.leaseId) {
        return $false
    }

    if ([string]$Existing.status -notin @('active', 'override')) {
        return $false
    }

    $candidateMode = [string]$Candidate.accessMode
    $existingMode = [string]$Existing.accessMode
    $candidateResources = @($Candidate.resources)
    $existingResources = @($Existing.resources)

    $candidateBuild = Test-HasResource $candidateResources 'pipeline:BuildPublishRun'
    $existingBuild = Test-HasResource $existingResources 'pipeline:BuildPublishRun'
    if (($candidateBuild -and (Test-IsWriteLike $existingMode)) -or
        ($existingBuild -and (Test-IsWriteLike $candidateMode))) {
        return $true
    }

    foreach ($candidateResource in $candidateResources) {
        foreach ($existingResource in $existingResources) {
            $resourceOverlap = $false
            if ($candidateResource.StartsWith('path:', [StringComparison]::OrdinalIgnoreCase) -and
                $existingResource.StartsWith('path:', [StringComparison]::OrdinalIgnoreCase)) {
                $resourceOverlap = Test-PathOverlap `
                    $candidateResource.Substring(5) `
                    $existingResource.Substring(5)
            }
            elseif ($candidateResource.Equals(
                $existingResource,
                [StringComparison]::OrdinalIgnoreCase
            )) {
                $resourceOverlap = $true
            }

            if (-not $resourceOverlap) {
                continue
            }

            if ($candidateMode -eq 'exclusive' -or $existingMode -eq 'exclusive') {
                return $true
            }

            if ((Test-IsWriteLike $candidateMode) -or (Test-IsWriteLike $existingMode)) {
                return $true
            }
        }
    }

    $insufficientScope = $candidateResources.Count -eq 0 -or $existingResources.Count -eq 0
    if ($insufficientScope -and
        ((Test-IsWriteLike $candidateMode) -or (Test-IsWriteLike $existingMode)) -and
        (Test-CategoryOverlap @($Candidate.categories) @($Existing.categories))) {
        return $true
    }

    $false
}

function Get-Conflicts {
    param(
        $Candidate,
        [object[]]$ExistingLeases
    )

    $conflicts = [Collections.Generic.List[object]]::new()
    foreach ($existing in @($ExistingLeases)) {
        if (-not (Test-LeaseConflict $Candidate $existing)) {
            continue
        }

        $age = Get-HeartbeatAge $existing
        $conflicts.Add([ordered]@{
            leaseId = [string]$existing.leaseId
            agentName = [string]$existing.agentName
            model = [string]$existing.model
            taskType = [string]$existing.taskType
            summary = [string]$existing.summary
            accessMode = [string]$existing.accessMode
            resources = @($existing.resources)
            status = [string]$existing.status
            heartbeatAtUtc = [string]$existing.heartbeatAtUtc
            heartbeatAgeSeconds = [Math]::Round($age.TotalSeconds, 1)
            suspectedStale = $age -ge $script:StaleAfter
        })
    }

    @($conflicts)
}

function Get-ConflictExitCode {
    param([object[]]$Conflicts)

    foreach ($conflict in @($Conflicts)) {
        if (-not [bool]$conflict.suspectedStale) {
            return $script:ExitConflict
        }
    }

    $script:ExitStaleConflict
}

function New-Lease {
    $now = [DateTime]::UtcNow
    $nonce = [Guid]::NewGuid().ToString('N')
    $hashInput = "$AgentName|$Model|$SessionId|$($now.ToString('o'))|$nonce"
    $hashBytes = [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($hashInput)
    )
    $hash = ([Convert]::ToHexString($hashBytes)).Substring(0, 12).ToLowerInvariant()
    $safeAgentName = [regex]::Replace($AgentName, '[^\p{L}\p{Nd}_.-]', '_')
    if ([string]::IsNullOrWhiteSpace($safeAgentName)) {
        $safeAgentName = 'Agent'
    }

    $newLeaseId = [Guid]::NewGuid().ToString('D')
    [pscustomobject][ordered]@{
        schemaVersion = 1
        leaseId = $newLeaseId
        agentName = $AgentName
        model = $Model
        sessionId = $SessionId
        parentLeaseId = if ([string]::IsNullOrWhiteSpace($ParentLeaseId)) { $null } else { $ParentLeaseId }
        taskType = $TaskType
        categories = @(Normalize-Categories $Categories)
        summary = $Summary
        accessMode = $AccessMode
        resources = @(Normalize-Resources $Resources)
        status = 'active'
        startedAtUtc = $now.ToString('o')
        heartbeatAtUtc = $now.ToString('o')
        conflictingLeaseIds = @()
        overrideApprovedAtUtc = $null
        registryFile = (Join-Path $script:RegistryFullPath "${safeAgentName}_${hash}.txt")
    }
}

function Assert-AcquireArguments {
    foreach ($required in @(
        @{ Name = 'AgentName'; Value = $AgentName },
        @{ Name = 'Model'; Value = $Model },
        @{ Name = 'SessionId'; Value = $SessionId },
        @{ Name = 'TaskType'; Value = $TaskType },
        @{ Name = 'Summary'; Value = $Summary },
        @{ Name = 'AccessMode'; Value = $AccessMode }
    )) {
        if ([string]::IsNullOrWhiteSpace([string]$required.Value)) {
            throw "$($required.Name) is required for $Action."
        }
    }

    Test-AccessMode $AccessMode
    if (@(Normalize-Categories $Categories).Count -eq 0) {
        throw "At least one category is required for $Action."
    }
}

function Find-Lease {
    param(
        [object[]]$Leases,
        [string]$ExpectedLeaseId
    )

    @($Leases | Where-Object { [string]$_.leaseId -eq $ExpectedLeaseId }) |
        Select-Object -First 1
}

function Assert-OwnedLease {
    param($Lease)

    if ($null -eq $Lease) {
        throw "Lease '$LeaseId' was not found."
    }

    if ([string]::IsNullOrWhiteSpace($SessionId)) {
        throw 'SessionId is required for lease ownership checks.'
    }

    if (-not ([string]$Lease.sessionId).Equals(
        $SessionId,
        [StringComparison]::Ordinal
    )) {
        throw "Lease '$LeaseId' belongs to another session."
    }
}

function Test-BuildOverrideAllowed {
    param(
        $Candidate,
        [object[]]$Conflicts
    )

    if (-not (Test-HasResource @($Candidate.resources) 'pipeline:BuildPublishRun')) {
        return $true
    }

    if (@($Conflicts).Count -eq 0) {
        return $true
    }

    [bool]$ConfirmBuildBarrierOverride
}

try {
    switch ($Action) {
        'Status' {
            $lock = Enter-RegistryLock
            try {
                $snapshot = Read-LeasesLocked
                $statusLeases = foreach ($lease in $snapshot.leases) {
                    $age = Get-HeartbeatAge $lease
                    [ordered]@{
                        leaseId = [string]$lease.leaseId
                        agentName = [string]$lease.agentName
                        model = [string]$lease.model
                        sessionId = [string]$lease.sessionId
                        parentLeaseId = $lease.parentLeaseId
                        taskType = [string]$lease.taskType
                        categories = @($lease.categories)
                        summary = [string]$lease.summary
                        accessMode = [string]$lease.accessMode
                        resources = @($lease.resources)
                        status = [string]$lease.status
                        startedAtUtc = ConvertTo-UtcIso $lease.startedAtUtc
                        heartbeatAtUtc = ConvertTo-UtcIso $lease.heartbeatAtUtc
                        heartbeatAgeSeconds = [Math]::Round($age.TotalSeconds, 1)
                        suspectedStale = $age -ge $script:StaleAfter
                        conflictingLeaseIds = @($lease.conflictingLeaseIds)
                        overrideApprovedAtUtc = ConvertTo-UtcIso `
                            $lease.overrideApprovedAtUtc -AllowNull
                    }
                }
            }
            finally {
                $lock.Dispose()
            }

            Complete-Result (New-Result $true $Action 'Registry status returned.' `
                -Leases $statusLeases -Errors $snapshot.errors) $script:ExitSuccess
        }

        'Acquire' {
            Assert-AcquireArguments
            $candidate = New-Lease
            $lock = Enter-RegistryLock
            try {
                $snapshot = Read-LeasesLocked
                if (-not [string]::IsNullOrWhiteSpace($ParentLeaseId)) {
                    $parent = Find-Lease $snapshot.leases $ParentLeaseId
                    if ($null -eq $parent) {
                        throw "Parent lease '$ParentLeaseId' was not found."
                    }
                }

                $conflicts = @(Get-Conflicts $candidate $snapshot.leases)
                if ($conflicts.Count -gt 0 -and -not $OverrideConflict) {
                    $code = Get-ConflictExitCode $conflicts
                    Complete-Result (New-Result $false $Action 'Lease conflicts with active work.' `
                        -Conflicts $conflicts -Errors $snapshot.errors) $code
                }

                if ($conflicts.Count -gt 0) {
                    if (-not (Test-BuildOverrideAllowed $candidate $conflicts)) {
                        Complete-Result (New-Result $false $Action `
                            'Build/Publish/Run barrier requires separate confirmation.' `
                            -Conflicts $conflicts -Errors $snapshot.errors) $script:ExitConflict
                    }

                    $candidate.status = 'override'
                    $candidate.conflictingLeaseIds = @($conflicts.leaseId)
                    $candidate.overrideApprovedAtUtc = [DateTime]::UtcNow.ToString('o')
                }

                Write-LeaseLocked $candidate
            }
            finally {
                $lock.Dispose()
            }

            Complete-Result (New-Result $true $Action 'Lease acquired.' `
                -Lease $candidate -Conflicts $conflicts -Errors $snapshot.errors) $script:ExitSuccess
        }

        'Heartbeat' {
            if ([string]::IsNullOrWhiteSpace($LeaseId)) {
                throw 'LeaseId is required for Heartbeat.'
            }

            $lock = Enter-RegistryLock
            try {
                $snapshot = Read-LeasesLocked
                $lease = Find-Lease $snapshot.leases $LeaseId
                Assert-OwnedLease $lease
                $lease.heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
                Write-LeaseLocked $lease
            }
            finally {
                $lock.Dispose()
            }

            Complete-Result (New-Result $true $Action 'Heartbeat updated.' `
                -Lease $lease -Errors $snapshot.errors) $script:ExitSuccess
        }

        'UpdateScope' {
            if ([string]::IsNullOrWhiteSpace($LeaseId)) {
                throw 'LeaseId is required for UpdateScope.'
            }

            $lock = Enter-RegistryLock
            try {
                $snapshot = Read-LeasesLocked
                $lease = Find-Lease $snapshot.leases $LeaseId
                Assert-OwnedLease $lease

                $candidate = $lease.PSObject.Copy()
                if ($PSBoundParameters.ContainsKey('Categories')) {
                    $candidate.categories = @(Normalize-Categories $Categories)
                }
                if ($PSBoundParameters.ContainsKey('Resources')) {
                    $candidate.resources = @(Normalize-Resources $Resources)
                }
                if ($PSBoundParameters.ContainsKey('AccessMode')) {
                    Test-AccessMode $AccessMode
                    $candidate.accessMode = $AccessMode
                }
                if ($PSBoundParameters.ContainsKey('TaskType')) {
                    $candidate.taskType = $TaskType
                }
                if ($PSBoundParameters.ContainsKey('Summary')) {
                    $candidate.summary = $Summary
                }

                $candidate.heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
                $conflicts = @(Get-Conflicts $candidate $snapshot.leases)
                if ($conflicts.Count -gt 0 -and -not $OverrideConflict) {
                    $lease.heartbeatAtUtc = $candidate.heartbeatAtUtc
                    Write-LeaseLocked $lease
                    $code = Get-ConflictExitCode $conflicts
                    Complete-Result (New-Result $false $Action `
                        'Expanded scope conflicts; original scope was retained.' `
                        -Lease $lease -Conflicts $conflicts -Errors $snapshot.errors) $code
                }

                if ($conflicts.Count -gt 0) {
                    if (-not (Test-BuildOverrideAllowed $candidate $conflicts)) {
                        Complete-Result (New-Result $false $Action `
                            'Build/Publish/Run barrier requires separate confirmation; original scope was retained.' `
                            -Lease $lease -Conflicts $conflicts -Errors $snapshot.errors) `
                            $script:ExitConflict
                    }

                    $candidate.status = 'override'
                    $candidate.conflictingLeaseIds = @($conflicts.leaseId)
                    $candidate.overrideApprovedAtUtc = [DateTime]::UtcNow.ToString('o')
                }
                elseif ([string]$candidate.status -eq 'override') {
                    $candidate.status = 'active'
                    $candidate.conflictingLeaseIds = @()
                    $candidate.overrideApprovedAtUtc = $null
                }

                Write-LeaseLocked $candidate
            }
            finally {
                $lock.Dispose()
            }

            Complete-Result (New-Result $true $Action 'Lease scope updated.' `
                -Lease $candidate -Conflicts $conflicts -Errors $snapshot.errors) $script:ExitSuccess
        }

        'Wait' {
            $deadline = [DateTime]::UtcNow.AddMinutes($WaitTimeoutMinutes)
            $candidate = $null

            if ([string]::IsNullOrWhiteSpace($LeaseId)) {
                Assert-AcquireArguments
                $candidate = New-Lease
            }

            while ($true) {
                $lock = Enter-RegistryLock
                try {
                    $snapshot = Read-LeasesLocked
                    if ($null -eq $candidate) {
                        $candidate = Find-Lease $snapshot.leases $LeaseId
                        Assert-OwnedLease $candidate
                        if ([string]$candidate.status -ne 'waiting') {
                            throw "Lease '$LeaseId' is not waiting."
                        }
                    }
                    elseif ([IO.File]::Exists([string]$candidate.registryFile)) {
                        $registeredCandidate = Find-Lease $snapshot.leases ([string]$candidate.leaseId)
                        Assert-OwnedLease $registeredCandidate
                        if ([string]$registeredCandidate.status -ne 'waiting') {
                            throw "Lease '$($candidate.leaseId)' is no longer waiting."
                        }
                        $candidate = $registeredCandidate
                    }

                    $candidate.heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
                    $conflicts = @(Get-Conflicts $candidate $snapshot.leases)
                    if ($conflicts.Count -eq 0) {
                        $candidate.status = 'active'
                        $candidate.conflictingLeaseIds = @()
                        $candidate.overrideApprovedAtUtc = $null
                        Write-LeaseLocked $candidate
                        Complete-Result (New-Result $true $Action `
                            'Waiting lease became active.' -Lease $candidate `
                            -Errors $snapshot.errors) $script:ExitSuccess
                    }

                    $candidate.status = 'waiting'
                    $candidate.conflictingLeaseIds = @($conflicts.leaseId)
                    Write-LeaseLocked $candidate
                }
                finally {
                    $lock.Dispose()
                }

                if ([DateTime]::UtcNow -ge $deadline) {
                    Complete-Result (New-Result $false $Action `
                        'Wait timeout reached; lease remains waiting.' `
                        -Lease $candidate -Conflicts $conflicts `
                        -Errors $snapshot.errors) $script:ExitWaitTimeout
                }

                Start-Sleep -Seconds $PollSeconds
            }
        }

        'Release' {
            if ([string]::IsNullOrWhiteSpace($LeaseId)) {
                throw 'LeaseId is required for Release.'
            }

            $lock = Enter-RegistryLock
            try {
                $snapshot = Read-LeasesLocked
                $lease = Find-Lease $snapshot.leases $LeaseId
                if ($null -eq $lease) {
                    throw "Lease '$LeaseId' was not found."
                }

                if ($ConfirmStaleRemoval) {
                    if (-not (Test-SuspectedStale $lease)) {
                        throw "Lease '$LeaseId' is not stale enough for confirmed removal."
                    }
                }
                else {
                    Assert-OwnedLease $lease
                }

                [IO.File]::Delete([string]$lease.registryFile)
            }
            finally {
                $lock.Dispose()
            }

            Complete-Result (New-Result $true $Action 'Lease released.' `
                -Lease $lease -Errors $snapshot.errors) $script:ExitSuccess
        }
    }
}
catch {
    Complete-Result (New-Result $false $Action $_.Exception.Message `
        -Errors @([ordered]@{
            type = $_.Exception.GetType().FullName
            message = $_.Exception.Message
        })) $script:ExitFailure
}
