#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Validates that all five version-string locations in the repo agree.

.DESCRIPTION
  Reads the canonical version from Directory.Build.props <Version> and asserts
  that manifest.json, .claude-plugin/plugin.json, .claude-plugin/marketplace.json
  (plugins[].version), and the top CHANGELOG.md [X.Y.Z] header all carry the
  same value.

  Called by verify-release.ps1 as a merge-gate check. Can also be run standalone.

  Exit codes:
    0  All five files agree.
    1  At least one file disagrees or is missing.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# 1. Directory.Build.props — canonical source
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
[xml]$props = Get-Content $propsPath
$canonical = $props.Project.PropertyGroup.Version
if (-not $canonical) {
    Write-Error "Could not read <Version> from $propsPath"
    exit 1
}
Write-Host "Canonical version (Directory.Build.props): $canonical"

$errors = @()

# 2. manifest.json
$manifestPath = Join-Path $repoRoot 'manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $canonical) {
    $errors += "manifest.json: expected '$canonical', got '$($manifest.version)'"
}

# 3. .claude-plugin/plugin.json
$pluginPath = Join-Path $repoRoot '.claude-plugin' 'plugin.json'
$plugin = Get-Content $pluginPath -Raw | ConvertFrom-Json
if ($plugin.version -ne $canonical) {
    $errors += ".claude-plugin/plugin.json: expected '$canonical', got '$($plugin.version)'"
}

# 4. .claude-plugin/marketplace.json — plugins[0].version (NOT metadata.version)
$marketplacePath = Join-Path $repoRoot '.claude-plugin' 'marketplace.json'
$marketplace = Get-Content $marketplacePath -Raw | ConvertFrom-Json
$pluginEntry = $marketplace.plugins | Select-Object -First 1
if ($pluginEntry.version -ne $canonical) {
    $errors += ".claude-plugin/marketplace.json plugins[0].version: expected '$canonical', got '$($pluginEntry.version)'"
}

# 5. CHANGELOG.md — top ## [X.Y.Z] header
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$changelogLines = Get-Content $changelogPath
$topHeader = $changelogLines | Where-Object { $_ -match '^\#\# \[(\d+\.\d+\.\d+)\]' } | Select-Object -First 1
if ($topHeader -match '^\#\# \[(\d+\.\d+\.\d+)\]') {
    $changelogVersion = $Matches[1]
    if ($changelogVersion -ne $canonical) {
        $errors += "CHANGELOG.md top header: expected '$canonical', got '$changelogVersion'"
    }
} else {
    $errors += "CHANGELOG.md: no ## [X.Y.Z] header found"
}

if ($errors.Count -gt 0) {
    Write-Host ''
    Write-Host 'VERSION DRIFT DETECTED:' -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host "  - $e" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host "All five files must carry version '$canonical'. See docs/release-policy.md § Where To Bump The Version String." -ForegroundColor Yellow
    exit 1
}

Write-Host "All 5 version files agree on $canonical" -ForegroundColor Green
exit 0
