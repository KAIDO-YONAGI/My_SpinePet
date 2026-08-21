[CmdletBinding()]
param(
    [string]$WorkingAgentScript = (Join-Path $PSScriptRoot 'WorkingAgent.ps1'),
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:PwshPath = (Get-Process -Id $PID).Path
$script:WorkingAgentFullPath = [IO.Path]::GetFullPath($WorkingAgentScript)
$script:WorkspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$script:TestRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'working-agent-tests-' + [Guid]::NewGuid().ToString('N')
)
$script:PassCount = 0
$script:TestCount = 0

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw "ASSERT FAILED: $Message"
    }
}

function New-TestRegistry {
    param([string]$Name)

    $path = Join-Path $script:TestRoot $Name
    [void][IO.Directory]::CreateDirectory($path)
    $path
}

function New-AgentProcess {
    param(
        [Parameter(Mandatory)][hashtable]$Arguments
    )

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $script:PwshPath
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.ArgumentList.Add('-NoLogo')
    $psi.ArgumentList.Add('-NoProfile')
    $psi.ArgumentList.Add('-File')
    $psi.ArgumentList.Add($script:WorkingAgentFullPath)

    foreach ($entry in $Arguments.GetEnumerator()) {
        if ($entry.Value -is [bool]) {
            if ($entry.Value) {
                $psi.ArgumentList.Add("-$($entry.Key)")
            }
            continue
        }

        if ($null -eq $entry.Value) {
            continue
        }

        $value = if ($entry.Value -is [Array]) {
            (@($entry.Value) -join ',')
        }
        else {
            [string]$entry.Value
        }

        $psi.ArgumentList.Add("-$($entry.Key)")
        $psi.ArgumentList.Add($value)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    if (-not $process.Start()) {
        throw 'Failed to start WorkingAgent child process.'
    }

    $process
}

function Receive-AgentProcess {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [int]$TimeoutMilliseconds = 30000
    )

    if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
        try {
            $Process.Kill($true)
        }
        catch {
        }
        throw "WorkingAgent process timed out after $TimeoutMilliseconds ms."
    }

    $stdout = $Process.StandardOutput.ReadToEnd().Trim()
    $stderr = $Process.StandardError.ReadToEnd().Trim()
    $exitCode = $Process.ExitCode
    $Process.Dispose()

    $json = $null
    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        try {
            $json = $stdout | ConvertFrom-Json
        }
        catch {
            throw "Invalid JSON output. Exit=$exitCode Stdout='$stdout' Stderr='$stderr'"
        }
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Json = $json
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Invoke-Agent {
    param(
        [Parameter(Mandatory)][hashtable]$Arguments,
        [int]$TimeoutMilliseconds = 30000
    )

    Receive-AgentProcess (New-AgentProcess $Arguments) $TimeoutMilliseconds
}

function New-AcquireArguments {
    param(
        [string]$Registry,
        [string]$Session,
        [string]$Mode,
        [string[]]$Resources,
        [string[]]$Categories = @('GUI'),
        [string]$Summary = 'test lease',
        [string]$AgentName = 'TestAgent'
    )

    [ordered]@{
        Action = 'Acquire'
        RegistryPath = $Registry
        WorkspaceRoot = $script:WorkspaceRoot
        AgentName = $AgentName
        Model = 'TestModel'
        SessionId = $Session
        TaskType = $Categories[0]
        Categories = $Categories
        Summary = $Summary
        AccessMode = $Mode
        Resources = $Resources
    }
}

function Release-Lease {
    param(
        [string]$Registry,
        [string]$LeaseId,
        [string]$Session
    )

    Invoke-Agent ([ordered]@{
        Action = 'Release'
        RegistryPath = $Registry
        WorkspaceRoot = $script:WorkspaceRoot
        LeaseId = $LeaseId
        SessionId = $Session
    })
}

function Invoke-Test {
    param(
        [string]$Name,
        [scriptblock]$Body
    )

    $script:TestCount++
    & $Body
    $script:PassCount++
    [Console]::WriteLine("PASS $Name")
}

[void][IO.Directory]::CreateDirectory($script:TestRoot)

try {
    Invoke-Test 'read + read can run concurrently' {
        $registry = New-TestRegistry 'read-read'
        $first = Invoke-Agent (New-AcquireArguments $registry 'rr-1' 'read' @('path:shared'))
        $second = Invoke-Agent (New-AcquireArguments $registry 'rr-2' 'read' @('path:shared'))
        Assert-True ($first.ExitCode -eq 0 -and $second.ExitCode -eq 0) `
            'Both read leases should succeed.'
    }

    Invoke-Test 'disjoint write paths can run concurrently' {
        $registry = New-TestRegistry 'disjoint-write'
        $first = Invoke-Agent (New-AcquireArguments $registry 'dw-1' 'write' @('path:one'))
        $second = Invoke-Agent (New-AcquireArguments $registry 'dw-2' 'write' @('path:two'))
        Assert-True ($first.ExitCode -eq 0 -and $second.ExitCode -eq 0) `
            'Disjoint writes should succeed.'
    }

    Invoke-Test 'same path, parent path, named resource, and implicit scope conflict' {
        $registry = New-TestRegistry 'conflict-rules'
        $base = Invoke-Agent (New-AcquireArguments $registry 'cr-1' 'write' @('path:tree'))
        Assert-True ($base.ExitCode -eq 0) 'Base lease should succeed.'

        $same = Invoke-Agent (New-AcquireArguments $registry 'cr-2' 'write' @('path:tree'))
        $child = Invoke-Agent (New-AcquireArguments $registry 'cr-3' 'write' @('path:tree/child'))
        Assert-True ($same.ExitCode -eq 2) 'Same path should conflict.'
        Assert-True ($child.ExitCode -eq 2) 'Parent and child paths should conflict.'

        $namedRegistry = New-TestRegistry 'named-conflict'
        $namedA = Invoke-Agent (New-AcquireArguments $namedRegistry 'nr-1' 'write' @('runtime:ExampleApp'))
        $namedB = Invoke-Agent (New-AcquireArguments $namedRegistry 'nr-2' 'read' @('runtime:ExampleApp'))
        Assert-True ($namedA.ExitCode -eq 0 -and $namedB.ExitCode -eq 2) `
            'Named shared resource should conflict with a writer.'

        $implicitRegistry = New-TestRegistry 'implicit-conflict'
        $implicitA = Invoke-Agent (New-AcquireArguments $implicitRegistry 'ir-1' 'write' @())
        $implicitB = Invoke-Agent (New-AcquireArguments $implicitRegistry 'ir-2' 'write' @())
        Assert-True ($implicitA.ExitCode -eq 0 -and $implicitB.ExitCode -eq 2) `
            'Unscoped writes in one category should conflict.'
    }

    Invoke-Test 'simultaneous Acquire is serialized' {
        $registry = New-TestRegistry 'atomic-acquire'
        $args1 = New-AcquireArguments $registry 'aa-1' 'write' @('path:race')
        $args2 = New-AcquireArguments $registry 'aa-2' 'write' @('path:race')
        $process1 = New-AgentProcess $args1
        $process2 = New-AgentProcess $args2
        $result1 = Receive-AgentProcess $process1
        $result2 = Receive-AgentProcess $process2
        $codes = @($result1.ExitCode, $result2.ExitCode) | Sort-Object
        Assert-True (($codes -join ',') -eq '0,2') `
            "Exactly one racer should acquire. Codes=$($codes -join ',')"
    }

    Invoke-Test 'heartbeat and suspected stale never auto-delete' {
        $registry = New-TestRegistry 'stale'
        $leaseResult = Invoke-Agent (New-AcquireArguments $registry 'st-1' 'write' @('path:stale'))
        $leaseId = $leaseResult.Json.lease.leaseId
        $heartbeat = Invoke-Agent ([ordered]@{
            Action = 'Heartbeat'
            RegistryPath = $registry
            WorkspaceRoot = $script:WorkspaceRoot
            LeaseId = $leaseId
            SessionId = 'st-1'
        })
        Assert-True ($heartbeat.ExitCode -eq 0) 'Heartbeat should succeed.'
        Assert-True (
            $heartbeat.Stdout -match
                '"startedAtUtc":"\d{4}-\d{2}-\d{2}T[^"]*(?:Z|[+-]\d{2}:\d{2})"'
        ) 'Lease updates should preserve an ISO UTC startedAtUtc value.'
        Assert-True (
            $heartbeat.Stdout -match
                '"heartbeatAtUtc":"\d{4}-\d{2}-\d{2}T[^"]*(?:Z|[+-]\d{2}:\d{2})"'
        ) 'Lease updates should preserve an ISO UTC heartbeatAtUtc value.'

        $marker = Get-ChildItem -LiteralPath $registry -Filter '*.txt' | Select-Object -First 1
        $json = [IO.File]::ReadAllText($marker.FullName) | ConvertFrom-Json
        $json.heartbeatAtUtc = [DateTime]::UtcNow.AddMinutes(-16).ToString('o')
        [IO.File]::WriteAllText(
            $marker.FullName,
            ($json | ConvertTo-Json -Depth 8),
            [Text.UTF8Encoding]::new($false)
        )

        $status = Invoke-Agent ([ordered]@{
            Action = 'Status'
            RegistryPath = $registry
            WorkspaceRoot = $script:WorkspaceRoot
        })
        Assert-True ([bool]$status.Json.leases[0].suspectedStale) `
            'Status should mark a 16-minute heartbeat stale.'

        $blocked = Invoke-Agent (New-AcquireArguments $registry 'st-2' 'write' @('path:stale'))
        Assert-True ($blocked.ExitCode -eq 3) 'A stale conflict should return exit code 3.'
        Assert-True ([IO.File]::Exists($marker.FullName)) 'Stale marker must not be auto-deleted.'

        $confirmedRemoval = Invoke-Agent ([ordered]@{
            Action = 'Release'
            RegistryPath = $registry
            WorkspaceRoot = $script:WorkspaceRoot
            LeaseId = $leaseId
            ConfirmStaleRemoval = $true
        })
        Assert-True ($confirmedRemoval.ExitCode -eq 0) `
            'A user-confirmed stale removal should succeed.'
        Assert-True (-not [IO.File]::Exists($marker.FullName)) `
            'Confirmed stale removal should delete the selected marker.'
    }

    Invoke-Test 'waiting lease atomically becomes active' {
        $registry = New-TestRegistry 'wait'
        $blocker = Invoke-Agent (New-AcquireArguments $registry 'wt-1' 'write' @('path:wait'))
        $waitArgs = New-AcquireArguments $registry 'wt-2' 'write' @('path:wait')
        $waitArgs.Action = 'Wait'
        $waitArgs.PollSeconds = 1
        $waitArgs.WaitTimeoutMinutes = 1
        $waitProcess = New-AgentProcess $waitArgs
        Start-Sleep -Seconds 2
        $released = Release-Lease $registry $blocker.Json.lease.leaseId 'wt-1'
        Assert-True ($released.ExitCode -eq 0) 'Blocker release should succeed.'
        $waitResult = Receive-AgentProcess $waitProcess 15000
        Assert-True ($waitResult.ExitCode -eq 0) 'Waiting lease should become active.'
        Assert-True ($waitResult.Json.lease.status -eq 'active') `
            'Waiting lease should transition to active.'
    }

    Invoke-Test 'user override records conflicts' {
        $registry = New-TestRegistry 'override'
        $blocker = Invoke-Agent (New-AcquireArguments $registry 'ov-1' 'write' @('path:override'))
        $args = New-AcquireArguments $registry 'ov-2' 'write' @('path:override')
        $args.OverrideConflict = $true
        $override = Invoke-Agent $args
        Assert-True ($override.ExitCode -eq 0) 'Confirmed override should succeed.'
        Assert-True ($override.Json.lease.status -eq 'override') 'Status should be override.'
        Assert-True ($override.Json.lease.conflictingLeaseIds -contains $blocker.Json.lease.leaseId) `
            'Override should record the blocker lease ID.'
        Assert-True (-not [string]::IsNullOrWhiteSpace(
            [string]$override.Json.lease.overrideApprovedAtUtc
        )) 'Override should record approval time.'
    }

    Invoke-Test 'UpdateScope detects a new conflict before changing scope' {
        $registry = New-TestRegistry 'update-scope'
        $first = Invoke-Agent (New-AcquireArguments $registry 'us-1' 'write' @('path:first'))
        $second = Invoke-Agent (New-AcquireArguments $registry 'us-2' 'write' @('path:second'))
        $update = Invoke-Agent ([ordered]@{
            Action = 'UpdateScope'
            RegistryPath = $registry
            WorkspaceRoot = $script:WorkspaceRoot
            LeaseId = $second.Json.lease.leaseId
            SessionId = 'us-2'
            Resources = @('path:first')
        })
        Assert-True ($first.ExitCode -eq 0 -and $second.ExitCode -eq 0) `
            'Initial disjoint leases should succeed.'
        Assert-True ($update.ExitCode -eq 2) 'Expanded scope should conflict.'
        Assert-True ($update.Json.lease.resources[0].EndsWith('\second')) `
            'Original scope should remain unchanged after conflict.'
    }

    Invoke-Test 'workflow Router/Log resource is a short exclusive section' {
        $registry = New-TestRegistry 'workflow'
        $first = Invoke-Agent (New-AcquireArguments $registry 'wf-1' 'exclusive' `
            @('workflow:GUI') @('GUI'))
        $blocked = Invoke-Agent (New-AcquireArguments $registry 'wf-2' 'write' `
            @('workflow:GUI') @('GUI'))
        Assert-True ($blocked.ExitCode -eq 2) 'Workflow critical section should conflict.'
        $released = Release-Lease $registry $first.Json.lease.leaseId 'wf-1'
        $after = Invoke-Agent (New-AcquireArguments $registry 'wf-3' 'write' `
            @('workflow:GUI') @('GUI'))
        Assert-True ($released.ExitCode -eq 0 -and $after.ExitCode -eq 0) `
            'Workflow resource should be available immediately after release.'
    }

    Invoke-Test 'Build/Publish/Run is a separately confirmed global barrier' {
        $registry = New-TestRegistry 'build-barrier'
        $writer = Invoke-Agent (New-AcquireArguments $registry 'bb-1' 'write' @('path:code'))
        $barrierArgs = New-AcquireArguments $registry 'bb-2' 'exclusive' `
            @('pipeline:BuildPublishRun') @('Workflow')
        $blocked = Invoke-Agent $barrierArgs
        Assert-True ($writer.ExitCode -eq 0 -and $blocked.ExitCode -eq 2) `
            'Build barrier should conflict with any active writer.'

        $barrierArgs.OverrideConflict = $true
        $stillBlocked = Invoke-Agent $barrierArgs
        Assert-True ($stillBlocked.ExitCode -eq 2) `
            'Ordinary override must not include the build barrier.'

        $barrierArgs.ConfirmBuildBarrierOverride = $true
        $confirmed = Invoke-Agent $barrierArgs
        Assert-True ($confirmed.ExitCode -eq 0) `
            'Separately confirmed build barrier override should succeed.'
    }

    Invoke-Test 'one session cannot release another lease' {
        $registry = New-TestRegistry 'ownership'
        $lease = Invoke-Agent (New-AcquireArguments $registry 'own-1' 'write' @('path:owned'))
        $wrongRelease = Release-Lease $registry $lease.Json.lease.leaseId 'own-2'
        Assert-True ($wrongRelease.ExitCode -eq 4) 'Wrong session release should fail.'
        $status = Invoke-Agent ([ordered]@{
            Action = 'Status'
            RegistryPath = $registry
            WorkspaceRoot = $script:WorkspaceRoot
        })
        Assert-True ($status.Json.leases.Count -eq 1) 'Lease should remain registered.'
    }

    Invoke-Test 'abnormal exit leaves a discoverable marker' {
        $registry = New-TestRegistry 'abnormal-exit'
        $lease = Invoke-Agent (New-AcquireArguments $registry 'crash-1' 'write' @('path:crash'))
        Assert-True ($lease.ExitCode -eq 0) 'Lease should be created.'
        $status = Invoke-Agent ([ordered]@{
            Action = 'Status'
            RegistryPath = $registry
            WorkspaceRoot = $script:WorkspaceRoot
        })
        Assert-True ($status.Json.leases.Count -eq 1) `
            'A process ending without Release should leave its marker.'
        Assert-True ($status.Json.leases[0].status -eq 'active') `
            'Crash marker should remain active until heartbeat makes it suspected stale.'
    }

    [Console]::WriteLine("PASS SUMMARY $($script:PassCount)/$($script:TestCount)")
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    [Console]::WriteLine("FAIL SUMMARY $($script:PassCount)/$($script:TestCount)")
    exit 1
}
finally {
    if ($KeepArtifacts) {
        [Console]::WriteLine("Artifacts: $($script:TestRoot)")
    }
    else {
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $resolvedTestRoot = [IO.Path]::GetFullPath($script:TestRoot)
        $isExpectedRoot = $resolvedTestRoot.StartsWith(
            $tempRoot,
            [StringComparison]::OrdinalIgnoreCase
        ) -and
            ([IO.Path]::GetFileName($resolvedTestRoot)).StartsWith(
                'working-agent-tests-',
                [StringComparison]::OrdinalIgnoreCase
            )
        if ($isExpectedRoot -and [IO.Directory]::Exists($resolvedTestRoot)) {
            [IO.Directory]::Delete($resolvedTestRoot, $true)
        }
    }
}
