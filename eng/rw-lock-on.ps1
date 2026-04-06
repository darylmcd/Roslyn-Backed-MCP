#requires -Version 5.1
<#
.SYNOPSIS
    Force the roslyn-mcp lock mode to rw-lock (sets ROSLYNMCP_WORKSPACE_RW_LOCK=true).

.DESCRIPTION
    Explicit-mode helper. Use the auto-flip script flip-rw-lock.ps1 instead
    when you want a toggle. Use this script when you want a known target mode
    regardless of the current state — for example, the very first run when
    no User-scope env var has been set yet, or in a CI pipeline that needs a
    deterministic starting state.

    Sets ROSLYNMCP_WORKSPACE_RW_LOCK=true at User scope, mirrors it into the
    current shell, and stops any running roslyn-mcp server processes so the
    next launch picks up the new flag. Never touches generic 'dotnet' processes.

    Companion scripts:
        eng/flip-rw-lock.ps1  — auto-detect current mode and flip to opposite
        eng/rw-lock-off.ps1   — force mode to legacy-mutex

.PARAMETER WhatIf
    Show what would happen without killing processes or changing env vars.

.EXAMPLE
    ./eng/rw-lock-on.ps1

.NOTES
    Windows-only. See flip-rw-lock.ps1 for design notes and edge cases.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'rw-lock-common.ps1')

$ScriptName = $MyInvocation.MyCommand.Name
Test-IsWindowsHost -ScriptName $ScriptName

Write-Host ''
Write-Host "[$ScriptName] Forcing roslyn-mcp lock mode to: rw-lock" -ForegroundColor Cyan
Write-Host ''

Stop-RwLockServerProcesses -ScriptName $ScriptName -Cmdlet $PSCmdlet
Set-RwLockMode -Mode 'rw-lock' -ScriptName $ScriptName -Cmdlet $PSCmdlet
Test-RwLockReadback -ExpectedMode 'rw-lock' -ScriptName $ScriptName
Write-RwLockNextSteps -NewMode 'rw-lock' -ScriptName $ScriptName
