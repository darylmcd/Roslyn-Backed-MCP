#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Validates that the .ai-doc-audit.md Live Surface snapshot matches the live surface.

.DESCRIPTION
  The doc-audit Live Surface table is a hand-maintained snapshot. Nothing gated it,
  so it silently drifted across twelve tagged releases (row
  `surface-snapshot-stale-surface-audit`). This script is the gate: release-cut
  Step 1 refuses the cut while the snapshot disagrees with the live surface.

  Truth sources (both already CI-proven, so this check needs no MCP call):
    - README.md § Live Surface. ReadmeSurfaceCountTests asserts that paragraph
      against ServerSurfaceCatalog on every run, so it tracks the real catalog.
    - skills/*/SKILL.md on disk for the bundled-skill count.

  Exit codes:
    0  Snapshot matches the live surface.
    1  Snapshot drifted, or either document could not be parsed.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

$readmePath = Join-Path $RepositoryRoot 'README.md'
$auditPath = Join-Path $RepositoryRoot '.ai-doc-audit.md'

foreach ($required in @($readmePath, $auditPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "SURFACE SNAPSHOT GATE: required file not found: $required" -ForegroundColor Red
        exit 1
    }
}

# --- Live truth: README § Live Surface (guarded by ReadmeSurfaceCountTests) ----
$readme = Get-Content $readmePath -Raw
$readmePattern = '\*\*(?<total>\d+)\s+(?<kind>tools|resources|prompts)\*\*\s*\(' +
    '(?:(?<stable>\d+)\s+stable\s*/\s*(?<experimental>\d+)\s+experimental|(?<all>all)\s+experimental)\)'
$live = @{}
foreach ($match in [regex]::Matches($readme, $readmePattern, 'IgnoreCase')) {
    $kind = $match.Groups['kind'].Value.ToLowerInvariant()
    $total = [int]$match.Groups['total'].Value
    if ($match.Groups['all'].Success) {
        $live[$kind] = @{ Stable = 0; Experimental = $total; Total = $total }
    } else {
        $live[$kind] = @{
            Stable       = [int]$match.Groups['stable'].Value
            Experimental = [int]$match.Groups['experimental'].Value
            Total        = $total
        }
    }
}

foreach ($kind in @('tools', 'resources', 'prompts')) {
    if (-not $live.ContainsKey($kind)) {
        Write-Host "SURFACE SNAPSHOT GATE: README.md has no parseable '$kind' surface claim." -ForegroundColor Red
        Write-Host "  Expected the README Live Surface paragraph shape, e.g. '**174 tools** (113 stable / 61 experimental)'." -ForegroundColor Yellow
        exit 1
    }
}

$liveSkillCount = @(Get-ChildItem -Path (Join-Path $RepositoryRoot 'skills') -Filter 'SKILL.md' -Recurse -File -ErrorAction SilentlyContinue).Count

# --- Snapshot: .ai-doc-audit.md § Live Surface -------------------------------
$auditLines = Get-Content $auditPath
$headerIndex = -1
for ($i = 0; $i -lt $auditLines.Count; $i++) {
    if ($auditLines[$i] -match '^\#\#\s+Live Surface') { $headerIndex = $i; break }
}
if ($headerIndex -lt 0) {
    Write-Host "SURFACE SNAPSHOT GATE: .ai-doc-audit.md has no '## Live Surface' section." -ForegroundColor Red
    exit 1
}
if ($auditLines[$headerIndex] -notmatch 'snapshot\s+(?<date>\d{4}-\d{2}-\d{2})') {
    Write-Host "SURFACE SNAPSHOT GATE: the Live Surface heading carries no 'snapshot YYYY-MM-DD' date." -ForegroundColor Red
    Write-Host "  Got: $($auditLines[$headerIndex])" -ForegroundColor Yellow
    exit 1
}
$snapshotDate = $Matches['date']

$snapshot = @{}
for ($i = $headerIndex + 1; $i -lt $auditLines.Count; $i++) {
    $line = $auditLines[$i]
    if ($line -match '^\#\#\s') { break }
    if ($line -match '^\|\s*(?<label>Tools|Resources|Prompts)\s*\|\s*(?<stable>\d+)\s*\|\s*(?<experimental>\d+)\s*\|\s*(?<total>\d+)\s*\|') {
        $snapshot[$Matches['label'].ToLowerInvariant()] = @{
            Stable       = [int]$Matches['stable']
            Experimental = [int]$Matches['experimental']
            Total        = [int]$Matches['total']
        }
    } elseif ($line -match '^\|\s*Bundled skills\s*\|[^|]*\|[^|]*\|\s*(?<total>\d+)\s*\|') {
        $snapshot['skills'] = [int]$Matches['total']
    }
}

$errors = @()
foreach ($kind in @('tools', 'resources', 'prompts')) {
    if (-not $snapshot.ContainsKey($kind)) {
        $errors += "Live Surface table has no '$kind' row."
        continue
    }
    foreach ($tier in @('Stable', 'Experimental', 'Total')) {
        $claimed = $snapshot[$kind][$tier]
        $actual = $live[$kind][$tier]
        if ($claimed -ne $actual) {
            $delta = $claimed - $actual
            $errors += ("{0} {1}: snapshot={2}, live={3} (off by {4:+#;-#;0})." -f $kind, $tier.ToLowerInvariant(), $claimed, $actual, $delta)
        }
    }
}
if (-not $snapshot.ContainsKey('skills')) {
    $errors += "Live Surface table has no 'Bundled skills' row."
} elseif ($snapshot['skills'] -ne $liveSkillCount) {
    $delta = $snapshot['skills'] - $liveSkillCount
    $errors += ("bundled skills: snapshot={0}, shipped skills/ directory={1} (off by {2:+#;-#;0})." -f $snapshot['skills'], $liveSkillCount, $delta)
}

if ($errors.Count -gt 0) {
    Write-Host ''
    Write-Host "SURFACE SNAPSHOT DRIFT DETECTED (snapshot $snapshotDate):" -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host "  - $e" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host "Refresh the '## Live Surface' table in .ai-doc-audit.md (run /surface-audit for the live measurement) and re-date the heading." -ForegroundColor Yellow
    exit 1
}

Write-Host "Live Surface snapshot ($snapshotDate) matches the live surface: $($live['tools'].Total) tools, $($live['resources'].Total) resources, $($live['prompts'].Total) prompts, $liveSkillCount skills." -ForegroundColor Green
exit 0
