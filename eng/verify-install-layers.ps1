#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Validates that both install layers are current for a given release version.

.DESCRIPTION
  The server ships on two independent surfaces, and they drift apart silently
  because nothing compared them:

    Layer 1 — the standalone global .NET tool (`darylmcd.roslynmcp`, command `roslynmcp`).
    Layer 2 — the Claude Code plugin (marketplace cache + release-pinned `dnx` launch).

  History runs both directions. v1.29.0 and v1.34.2 updated Layer 1 and left Layer 2
  stale in the plugin cache; v4.1.2 refreshed Layer 2 and left Layer 1 a release behind
  because the global-tool update was documented as optional. `/release-cut` Step 6 now
  updates both and calls this script to prove it.

  Layer 1 is read from `dotnet tool list --global` unless -Layer1Version is supplied.
  Layer 2 is read from the plugin cache directory, plus the cached plugin manifest when
  it is present.

  Exit codes:
    0  Both layers report ExpectedVersion.
    1  A layer is stale, missing, or could not be determined.
#>
[CmdletBinding()]
param(
    [string] $ExpectedVersion,
    [string] $RepositoryRoot,
    [string] $PluginCacheRoot,
    [string] $Layer1Version
)

$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

if (-not $ExpectedVersion) {
    $propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $propsPath)) {
        Write-Host "INSTALL LAYER GATE: Directory.Build.props not found beneath '$RepositoryRoot'." -ForegroundColor Red
        exit 1
    }
    [xml] $props = Get-Content -LiteralPath $propsPath
    $ExpectedVersion = $props.Project.PropertyGroup.Version
    if (-not $ExpectedVersion) {
        Write-Host "INSTALL LAYER GATE: could not read <Version> from $propsPath." -ForegroundColor Red
        exit 1
    }
}

if (-not $PluginCacheRoot) {
    $PluginCacheRoot = Join-Path $HOME '.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp'
}

$errors = @()

# --- Layer 1: standalone global .NET tool -----------------------------------
if (-not $Layer1Version) {
    try {
        $toolList = & dotnet tool list --global 2>&1 | Out-String
    }
    catch {
        $toolList = ''
    }

    $toolMatch = [regex]::Match(
        $toolList,
        '(?im)^\s*darylmcd\.roslynmcp\s+(?<version>\S+)\s')
    if ($toolMatch.Success) {
        $Layer1Version = $toolMatch.Groups['version'].Value
    }
    else {
        $errors += "Layer 1 (global tool 'darylmcd.roslynmcp') is not installed. Run 'just tool-update'."
    }
}

if ($Layer1Version -and $Layer1Version -ne $ExpectedVersion) {
    $errors += "Layer 1 (global tool) is at $Layer1Version, expected $ExpectedVersion. Run 'just tool-update'."
}

# --- Layer 2: Claude Code plugin cache --------------------------------------
if (-not (Test-Path -LiteralPath $PluginCacheRoot)) {
    $errors += "Layer 2 (plugin cache) not found at '$PluginCacheRoot'. Run 'pwsh -NoProfile -File eng/update-claude-plugin.ps1'."
}
else {
    $cacheDirs = @(
        Get-ChildItem -LiteralPath $PluginCacheRoot -Directory -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Name)

    if ($cacheDirs.Count -eq 0) {
        $errors += "Layer 2 (plugin cache) at '$PluginCacheRoot' holds no version directory."
    }
    elseif ($cacheDirs -notcontains $ExpectedVersion) {
        $errors += ("Layer 2 (plugin cache) has no $ExpectedVersion directory; found: " +
            ($cacheDirs -join ', ') + ". Run 'pwsh -NoProfile -File eng/update-claude-plugin.ps1'.")
    }
    else {
        $stale = @($cacheDirs | Where-Object { $_ -ne $ExpectedVersion })
        if ($stale.Count -gt 0) {
            $errors += ('Layer 2 (plugin cache) still holds stale version directories: ' +
                ($stale -join ', ') + '. The updater prunes these; re-run it.')
        }

        $cachedManifest = Join-Path $PluginCacheRoot $ExpectedVersion '.claude-plugin/plugin.json'
        if (Test-Path -LiteralPath $cachedManifest) {
            $manifestVersion = (Get-Content -LiteralPath $cachedManifest -Raw | ConvertFrom-Json).version
            if ($manifestVersion -ne $ExpectedVersion) {
                $errors += "Layer 2 cached plugin.json reports $manifestVersion, expected $ExpectedVersion."
            }
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host ''
    Write-Host "INSTALL LAYER DRIFT DETECTED (expected $ExpectedVersion):" -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host "  - $e" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host "Both layers must track the release. 'just reinstall' refreshes them together." -ForegroundColor Yellow
    exit 1
}

Write-Host "Install layers current at ${ExpectedVersion}: Layer 1 (global tool) and Layer 2 (plugin cache)." -ForegroundColor Green
exit 0
