#requires -Version 5.1
<#
.SYNOPSIS
    Force the roslyn-mcp lock mode to legacy-mutex (clears ROSLYNMCP_WORKSPACE_RW_LOCK).

.DESCRIPTION
    Explicit-mode helper. Use the auto-flip script flip-rw-lock.ps1 instead
    when you want a toggle. Use this script when you want a known target mode
    regardless of the current state — for example, the very first run when
    no User-scope env var has been set yet, or in a CI pipeline that needs a
    deterministic starting state.

    Clears ROSLYNMCP_WORKSPACE_RW_LOCK at User scope, removes it from the
    current shell, and stops any running roslyn-mcp server processes so the
    next launch falls back to the default legacy-mutex mode. Never touches
    generic 'dotnet' processes.

    Companion scripts:
        eng/flip-rw-lock.ps1  — auto-detect current mode and flip to opposite
        eng/rw-lock-on.ps1    — force mode to rw-lock

.PARAMETER WhatIf
    Show what would happen without killing processes or changing env vars.

.EXAMPLE
    ./eng/rw-lock-off.ps1

.NOTES
    Windows-only. See flip-rw-lock.ps1 for design notes and edge cases.

    If the MCP client has ROSLYNMCP_WORKSPACE_RW_LOCK explicitly set in its
    launch config (.mcp.json env block), this User-scope clear will NOT
    override that. Edit the .mcp.json env block manually to remove the override.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'rw-lock-common.ps1')

$ScriptName = $MyInvocation.MyCommand.Name
Test-IsWindowsHost -ScriptName $ScriptName

Write-Host ''
Write-Host "[$ScriptName] Forcing roslyn-mcp lock mode to: legacy-mutex (default)" -ForegroundColor Cyan
Write-Host ''

Stop-RwLockServerProcesses -ScriptName $ScriptName -Cmdlet $PSCmdlet
Set-RwLockMode -Mode 'legacy-mutex' -ScriptName $ScriptName -Cmdlet $PSCmdlet
Test-RwLockReadback -ExpectedMode 'legacy-mutex' -ScriptName $ScriptName
Write-RwLockNextSteps -NewMode 'legacy-mutex' -ScriptName $ScriptName
