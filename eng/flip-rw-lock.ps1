#requires -Version 5.1
<#
.SYNOPSIS
    Flip the roslyn-mcp lock mode to the opposite of its current value.

.DESCRIPTION
    Reads the current User-scope value of ROSLYNMCP_WORKSPACE_RW_LOCK,
    decides the inverse, kills any running roslyn-mcp server processes, and
    writes the new value. The operator does not need to know which mode they
    are currently in — the script handles the toggle automatically.

    This is the primary helper for the deep-review-and-refactor.md Phase 8b
    dual-mode lane (Path A). The simplified Path A flow is:

        1. Operator: invoke deep-review prompt (run 1, current mode).
        2. Operator: ./eng/flip-rw-lock.ps1 (auto-flip).
        3. Operator: fully close and reopen the MCP client.
        4. Operator: invoke deep-review prompt (run 2, opposite mode).

    The agent auto-detects the lock mode in Phase 0 via evaluate_csharp and
    auto-pairs the second run with the first run's partial via filename glob,
    so no operator-side bookkeeping is required.

    The script never touches generic 'dotnet' processes — only 'roslynmcp.exe'
    and 'RoslynMcp.Host.Stdio.exe'.

    Companion explicit-mode scripts (rarely needed):
        eng/rw-lock-on.ps1   — force mode to rw-lock
        eng/rw-lock-off.ps1  — force mode to legacy-mutex (default)

.PARAMETER WhatIf
    Show what would happen without killing processes or changing env vars.

.EXAMPLE
    ./eng/flip-rw-lock.ps1
    Detects current mode and flips to the inverse.

.EXAMPLE
    ./eng/flip-rw-lock.ps1 -WhatIf
    Dry-run: prints current mode, target mode, and what would happen.

.NOTES
    Windows-only. The User-scope env var read/write uses
    [Environment]::GetEnvironmentVariable / SetEnvironmentVariable with
    'User' scope, which only takes effect on Windows. For bash/zsh/macOS/Linux
    equivalents, see ai_docs/procedures/deep-review-command-reference.md →
    'Concurrency lock-mode launch'.

    Edge case: if the operator's MCP client launch config sets
    ROSLYNMCP_WORKSPACE_RW_LOCK explicitly (e.g. in .mcp.json env block), the
    User-scope value this script reads may not match what the running server
    actually sees. The deep-review prompt's Phase 0 uses evaluate_csharp to
    read the value from inside the server process, which is the source of
    truth — that always wins over what this script reports.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'rw-lock-common.ps1')

$ScriptName = $MyInvocation.MyCommand.Name
Test-IsWindowsHost -ScriptName $ScriptName

# 1. Detect current mode from User-scope env var.
$current = Get-RwLockCurrentMode
$targetMode = if ($current.Mode -eq 'rw-lock') { 'legacy-mutex' } else { 'rw-lock' }

Write-Host ''
Write-Host "[$ScriptName] Current User-scope $(Get-RwLockEnvVarName) = $($current.Raw)" -ForegroundColor Cyan
Write-Host "[$ScriptName] Detected current mode: $($current.Mode)" -ForegroundColor Cyan
Write-Host "[$ScriptName] Flipping to: $targetMode" -ForegroundColor Cyan
Write-Host ''

# 2. Stop any running roslyn-mcp server processes.
Stop-RwLockServerProcesses -ScriptName $ScriptName -Cmdlet $PSCmdlet

# 3. Set or clear the env var according to the target mode.
Set-RwLockMode -Mode $targetMode -ScriptName $ScriptName -Cmdlet $PSCmdlet

# 4. Verify by reading back from User scope (skipped under -WhatIf).
Test-RwLockReadback -ExpectedMode $targetMode -ScriptName $ScriptName

# 5. Print operator next steps.
Write-RwLockNextSteps -NewMode $targetMode -ScriptName $ScriptName
