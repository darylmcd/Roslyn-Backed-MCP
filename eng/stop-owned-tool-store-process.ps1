<#
.SYNOPSIS
Ownership-scoped shutdown and lock detection for the `roslynmcp` global-tool store.

.DESCRIPTION
Defines two functions used to keep a Windows `dotnet tool update`/`uninstall`/`install`
from failing on a locked tool-store directory:

  - Stop-OwnedToolStoreProcess: stops exactly one process, identified by PID + UTC start
    time (a PID-reuse guard), after confirming its image name is `roslynmcp` AND its image
    path resolves under the given tool-store root. Refuses (throws) rather than terminating
    anything it cannot fully attribute. Never discovers processes by name alone.
  - Assert-ToolStoreUnlocked: fails closed, naming every PID + image path still holding a
    file under the tool-store root, without terminating anything. Used as a final pre-flight
    so a caller gets a clear, attributable error instead of an opaque `dotnet` I/O failure.

This file is meant to be BOTH dot-sourced (by eng/reinstall-local-tool.ps1, to reuse the
functions with its own already-resolved parameters) and invoked directly (by `just
tool-update`, which has no per-invocation parameters of its own). To keep dot-sourcing
side-effect-free, the file declares no top-level `param()` block — only functions — so
dot-sourcing it can never clobber a caller's already-resolved variables of the same name.
Standalone execution (env-var-sourced identity, default tool-store root) is gated behind an
InvocationName check so it never runs when dot-sourced.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Stop-OwnedToolStoreProcess {
    [CmdletBinding()]
    param(
        [ValidateRange(0, [int]::MaxValue)]
        [int]$OwnedProcessId = 0,

        [string]$OwnedProcessStartedAtUtc = '',

        [Parameter(Mandatory)]
        [string]$ToolStoreRoot
    )

    $shutdownTimeout = [TimeSpan]::FromSeconds(10)
    $ownershipTimestampTolerance = [TimeSpan]::FromSeconds(1)

    if ($OwnedProcessId -eq 0) {
        if (-not [string]::IsNullOrWhiteSpace($OwnedProcessStartedAtUtc)) {
            throw 'OwnedProcessStartedAtUtc requires a nonzero OwnedProcessId.'
        }

        Write-Host 'No owned roslynmcp process identity was supplied; no process was stopped.'
        return
    }

    if ([string]::IsNullOrWhiteSpace($OwnedProcessStartedAtUtc)) {
        throw 'OwnedProcessId requires OwnedProcessStartedAtUtc as a PID-reuse guard.'
    }

    $expectedStart = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
            $OwnedProcessStartedAtUtc,
            'O',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$expectedStart)) {
        throw 'OwnedProcessStartedAtUtc must use the round-trip UTC timestamp format.'
    }

    if ($OwnedProcessId -eq $PID) {
        throw 'The reinstall helper refuses to terminate its own PowerShell process.'
    }

    try {
        $process = Get-Process -Id $OwnedProcessId -ErrorAction Stop
    }
    catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
        Write-Host "Owned roslynmcp process $OwnedProcessId already exited; continuing."
        return
    }

    if (-not [string]::Equals($process.ProcessName, 'roslynmcp', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Process $OwnedProcessId is '$($process.ProcessName)', not roslynmcp; refusing termination."
    }

    $resolvedToolStoreRoot = $null
    try {
        $resolvedToolStoreRoot = (Resolve-Path -LiteralPath $ToolStoreRoot -ErrorAction Stop).Path.TrimEnd('\', '/')
    }
    catch {
        throw "Could not resolve tool store root '$ToolStoreRoot'; refusing termination."
    }

    $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $OwnedProcessId" -ErrorAction Stop
    $imagePath = [string]$processInfo.ExecutablePath
    if ([string]::IsNullOrWhiteSpace($imagePath) -or
        -not $imagePath.StartsWith($resolvedToolStoreRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Process $OwnedProcessId image path '$imagePath' is not under the tool store root '$resolvedToolStoreRoot'; refusing termination."
    }

    try {
        $actualStart = [DateTimeOffset]$process.StartTime.ToUniversalTime()
    }
    catch {
        throw "Could not verify the start time for owned roslynmcp process $OwnedProcessId; refusing termination."
    }

    if (($actualStart - $expectedStart.ToUniversalTime()).Duration() -gt $ownershipTimestampTolerance) {
        throw "Process $OwnedProcessId start time does not match the supplied ownership identity; refusing termination."
    }

    try {
        Stop-Process -Id $OwnedProcessId -Force -ErrorAction Stop
    }
    catch {
        $process.Refresh()
        if (-not $process.HasExited) {
            throw "Failed to stop owned roslynmcp process $OwnedProcessId."
        }
    }

    if (-not $process.WaitForExit([int]$shutdownTimeout.TotalMilliseconds)) {
        throw "Owned roslynmcp process $OwnedProcessId did not exit within $($shutdownTimeout.TotalSeconds) seconds."
    }

    Write-Host "Stopped owned roslynmcp process $OwnedProcessId."
}

function Assert-ToolStoreUnlocked {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ToolStoreRoot
    )

    if (-not (Test-Path -LiteralPath $ToolStoreRoot)) {
        return
    }

    $resolvedToolStoreRoot = (Resolve-Path -LiteralPath $ToolStoreRoot -ErrorAction Stop).Path.TrimEnd('\', '/')
    $holders = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
        ([string]$_.ExecutablePath).StartsWith($resolvedToolStoreRoot, [StringComparison]::OrdinalIgnoreCase)
    })

    if ($holders.Count -gt 0) {
        $holderDescriptions = $holders | ForEach-Object { "PID $($_.ProcessId): $($_.ExecutablePath)" }
        throw "The tool store root '$resolvedToolStoreRoot' is still locked by $($holders.Count) process(es): $($holderDescriptions -join '; ')"
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $standaloneOwnedProcessId = 0
    if (-not [string]::IsNullOrWhiteSpace($env:ROSLYNMCP_REINSTALL_PROCESS_ID)) {
        $standaloneOwnedProcessId = [int]$env:ROSLYNMCP_REINSTALL_PROCESS_ID
    }
    $standaloneOwnedProcessStartedAtUtc = ''
    if (-not [string]::IsNullOrWhiteSpace($env:ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC)) {
        $standaloneOwnedProcessStartedAtUtc = $env:ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC
    }
    $standaloneToolStoreRoot = Join-Path $env:USERPROFILE '.dotnet' 'tools'

    Stop-OwnedToolStoreProcess `
        -OwnedProcessId $standaloneOwnedProcessId `
        -OwnedProcessStartedAtUtc $standaloneOwnedProcessStartedAtUtc `
        -ToolStoreRoot $standaloneToolStoreRoot
    Assert-ToolStoreUnlocked -ToolStoreRoot $standaloneToolStoreRoot
}
