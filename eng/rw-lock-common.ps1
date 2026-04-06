#requires -Version 5.1
<#
.SYNOPSIS
    Shared helper functions for the roslyn-mcp lock-mode flip scripts.

.DESCRIPTION
    Dot-sourced by rw-lock-on.ps1, rw-lock-off.ps1, and flip-rw-lock.ps1.
    Provides:

    - Get-RwLockEnvVarName       : the env var name (single source of truth)
    - Get-RwLockProcessNames     : the process names that may host the server
    - Test-IsWindowsHost         : Windows guard usable from PS 5.1 and PS 7+
    - Get-RwLockCurrentMode      : reads User-scope env var and returns the
                                   current mode label as 'rw-lock' or 'legacy-mutex'
    - Stop-RwLockServerProcesses : kills any running roslyn-mcp server process
                                   (never touches generic dotnet processes),
                                   honors -WhatIf via the caller's $PSCmdlet
    - Set-RwLockMode             : writes/clears the User-scope env var and
                                   mirrors the change into the current shell,
                                   honors -WhatIf via the caller's $PSCmdlet
    - Write-RwLockNextSteps      : prints the standard operator next-steps block

    The helper does not run anything itself when dot-sourced — it only defines
    functions. Each wrapper script calls them in the right order.

.NOTES
    This file is a helper, not a standalone script. Do not run it directly;
    dot-source it from a wrapper.

    Windows-only. Throws on non-Windows from Test-IsWindowsHost.
#>

Set-StrictMode -Version Latest

function Get-RwLockEnvVarName {
    return 'ROSLYNMCP_WORKSPACE_RW_LOCK'
}

function Get-RwLockProcessNames {
    # Process names that may host the roslyn-mcp server. Listed exhaustively so
    # both the installed dotnet tool and a build-from-source exe are caught.
    # We deliberately do NOT touch generic 'dotnet' processes — they almost
    # always belong to other work and killing them would break the operator's
    # environment.
    return @(
        'roslynmcp',            # Installed dotnet tool wrapper (apphost-based)
        'RoslynMcp.Host.Stdio'  # Built-from-source exe in bin/Debug or bin/Release
    )
}

function Test-IsWindowsHost {
    <#
    .SYNOPSIS
        Throw with an actionable message if not running on Windows.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ScriptName
    )

    # PS 7+ exposes $IsWindows; Windows PowerShell 5.1 does not but is always
    # Windows. Either of those is acceptable; anything else is a hard error.
    $isOnWindows = if ($PSVersionTable.PSVersion.Major -le 5) { $true } else { [bool]$IsWindows }
    if (-not $isOnWindows) {
        throw "$ScriptName is Windows-only. For bash/zsh equivalents see ai_docs/procedures/deep-review-command-reference.md → 'Concurrency lock-mode launch'."
    }
}

function Get-RwLockCurrentMode {
    <#
    .SYNOPSIS
        Read the current User-scope env var and return the mode label.
    .OUTPUTS
        PSCustomObject with .Mode ('rw-lock' or 'legacy-mutex') and .Raw (the
        raw env var value, or '(unset)').
    #>
    [CmdletBinding()]
    param()

    $envVarName = Get-RwLockEnvVarName
    $raw = [Environment]::GetEnvironmentVariable($envVarName, 'User')
    $isOn = $false
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $isOn = [bool]::TryParse($raw, [ref]$isOn) -and $isOn
    }

    return [pscustomobject]@{
        Mode = if ($isOn) { 'rw-lock' } else { 'legacy-mutex' }
        Raw  = if ([string]::IsNullOrWhiteSpace($raw)) { '(unset)' } else { $raw }
    }
}

function Stop-RwLockServerProcesses {
    <#
    .SYNOPSIS
        Kill any running roslyn-mcp server processes. Honors -WhatIf via the
        caller's $PSCmdlet.
    .PARAMETER ScriptName
        Caller's script name (used in log lines).
    .PARAMETER Cmdlet
        The caller's $PSCmdlet so ShouldProcess works correctly under -WhatIf.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ScriptName,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $processNames = Get-RwLockProcessNames
    $matched = @()
    foreach ($name in $processNames) {
        $found = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($found) {
            $matched += $found
        }
    }

    if ($matched.Count -eq 0) {
        Write-Host "[$ScriptName] No running roslyn-mcp processes found." -ForegroundColor DarkGray
        return
    }

    Write-Host "[$ScriptName] Found $($matched.Count) roslyn-mcp process(es):" -ForegroundColor Yellow
    $matched | Format-Table -AutoSize Id, ProcessName, Path | Out-Host
    foreach ($proc in $matched) {
        $target = "$($proc.ProcessName) (PID $($proc.Id))"
        if ($Cmdlet.ShouldProcess($target, 'Stop-Process -Force')) {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                Write-Host "[$ScriptName]   killed $target" -ForegroundColor Yellow
            }
            catch {
                Write-Warning "[$ScriptName]   failed to kill $target : $($_.Exception.Message)"
            }
        }
    }
}

function Set-RwLockMode {
    <#
    .SYNOPSIS
        Set or clear the User-scope env var for the lock mode and mirror into
        the current shell. Honors -WhatIf via the caller's $PSCmdlet.
    .PARAMETER Mode
        Target mode: 'rw-lock' (sets to 'true') or 'legacy-mutex' (clears).
    .PARAMETER ScriptName
        Caller's script name (used in log lines).
    .PARAMETER Cmdlet
        The caller's $PSCmdlet so ShouldProcess works correctly under -WhatIf.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('rw-lock', 'legacy-mutex')][string]$Mode,
        [Parameter(Mandatory)][string]$ScriptName,
        [Parameter(Mandatory)][System.Management.Automation.PSCmdlet]$Cmdlet
    )

    $envVarName = Get-RwLockEnvVarName

    if ($Mode -eq 'rw-lock') {
        $value = 'true'
        $envTarget = "$envVarName=$value (User and Process scope)"
        if ($Cmdlet.ShouldProcess($envTarget, 'Set environment variable')) {
            [Environment]::SetEnvironmentVariable($envVarName, $value, 'User')
            Set-Item -Path "Env:\$envVarName" -Value $value
            Write-Host ''
            Write-Host "[$ScriptName] $envVarName set to '$value' (User scope and current shell)." -ForegroundColor Green
        }
    }
    else {
        $envTarget = "$envVarName (User and Process scope)"
        if ($Cmdlet.ShouldProcess($envTarget, 'Clear environment variable')) {
            [Environment]::SetEnvironmentVariable($envVarName, $null, 'User')
            if (Test-Path "Env:\$envVarName") {
                Remove-Item -Path "Env:\$envVarName" -ErrorAction SilentlyContinue
            }
            Write-Host ''
            Write-Host "[$ScriptName] $envVarName cleared (User scope and current shell). Server will fall back to default legacy-mutex mode." -ForegroundColor Green
        }
    }
}

function Test-RwLockReadback {
    <#
    .SYNOPSIS
        Verify the User-scope env var matches the expected mode after a set/clear.
        Skipped under -WhatIf.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('rw-lock', 'legacy-mutex')][string]$ExpectedMode,
        [Parameter(Mandatory)][string]$ScriptName
    )

    if ($WhatIfPreference) { return }

    $envVarName = Get-RwLockEnvVarName
    $readback = [Environment]::GetEnvironmentVariable($envVarName, 'User')

    if ($ExpectedMode -eq 'rw-lock') {
        if ($readback -ne 'true') {
            Write-Warning "[$ScriptName] Verification failed: User-scope $envVarName reads back as '$readback' (expected 'true')."
        }
    }
    else {
        if (-not [string]::IsNullOrEmpty($readback)) {
            Write-Warning "[$ScriptName] Verification failed: User-scope $envVarName still reads back as '$readback' (expected empty)."
        }
    }
}

function Write-RwLockNextSteps {
    <#
    .SYNOPSIS
        Print the standard operator next-steps block after a successful flip.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('rw-lock', 'legacy-mutex')][string]$NewMode,
        [Parameter(Mandatory)][string]$ScriptName
    )

    $envVarName = Get-RwLockEnvVarName

    Write-Host ''
    Write-Host "[$ScriptName] Next steps for the operator:" -ForegroundColor Cyan
    Write-Host '  1. Fully close your MCP client (Claude Code, Cursor, Continue, etc.).'
    Write-Host '     Restarting just the chat is NOT enough — the client process must exit'
    Write-Host '     so the next launch inherits the new env var.'
    if ($NewMode -eq 'rw-lock') {
        Write-Host "  2. Reopen the MCP client. It will spawn a fresh roslyn-mcp server with"
        Write-Host "     $envVarName='true'."
    }
    else {
        Write-Host "  2. Reopen the MCP client. It will spawn a fresh roslyn-mcp server with no"
        Write-Host "     $envVarName set, falling back to the default legacy-mutex mode."
    }
    Write-Host '  3. Run the deep-review prompt (ai_docs/prompts/deep-review-and-refactor.md).'
    Write-Host "     The agent will detect the '$NewMode' lock mode in Phase 0 via evaluate_csharp"
    Write-Host '     and name the audit file accordingly.'
    Write-Host ''
    if ($NewMode -eq 'rw-lock') {
        Write-Host "[$ScriptName] If your MCP client does NOT inherit user-scope env vars (rare)," -ForegroundColor DarkGray
        Write-Host "  set the variable explicitly in the client's launch config (.mcp.json env block)." -ForegroundColor DarkGray
    }
    else {
        Write-Host "[$ScriptName] If your MCP client has $envVarName explicitly set in its launch" -ForegroundColor DarkGray
        Write-Host "  config (.mcp.json env block), this User-scope clear will NOT override that." -ForegroundColor DarkGray
        Write-Host '  Edit the .mcp.json env block manually to remove the override.' -ForegroundColor DarkGray
    }
    Write-Host "  See ai_docs/procedures/deep-review-command-reference.md → 'Concurrency lock-mode launch'." -ForegroundColor DarkGray
    Write-Host ''
}
