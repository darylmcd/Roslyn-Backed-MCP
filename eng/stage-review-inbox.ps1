#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Stage deep-review artifacts (audits, retros, experimental-promotion reports) into review-inbox/ for backlog intake.

.DESCRIPTION
    Discovers the three deep-review artifact shapes across this repo + sibling repos under the
    parent directory and moves them into this repo's review-inbox/ folder. Source filenames
    already carry a repo-id prefix (e.g. 20260424T030145Z_roslyn-backed-mcp_roslyn-mcp-retro.md)
    so no renaming is needed.

    Recognized shapes:
      *_mcp-server-audit.md         Server audit from the deep-review prompt
      *_experimental-promotion.md   Experimental tool/prompt promotion audit
      *_roslyn-mcp-retro.md         Session retro on Roslyn-MCP tool quality

    This script does ONE thing: mechanical discovery + move. All judgment work
    (extract, dedupe, classify, rank, anchor-verify, heroic-split) lives in the
    /backlog-intake skill that operates on the staged review-inbox/ contents.

.PARAMETER SiblingRepoParent
    Parent folder to scan for sibling repos. Defaults to the parent of this repo.

.PARAMETER ExcludeRepoFolders
    Extra folder names to skip under $SiblingRepoParent. This repo's folder is
    always scanned (its canonical ai_docs paths) unless -SkipSelf is passed.

.PARAMETER SearchPaths
    Per-repo relative paths to probe. Defaults cover both the canonical
    ai_docs/audit-reports + ai_docs/reports locations and the older
    audit-reports/ top-level location still used by some siblings.

.PARAMETER DryRun
    Show what would be staged without moving files.

.PARAMETER Copy
    Copy instead of move. Useful for re-runs or when the sibling repo tracks
    the source files and you don't want to dirty it.

.PARAMETER SkipSelf
    Do not scan this repo's own ai_docs/audit-reports or ai_docs/reports
    folders. Useful when the only new artifacts came from siblings.

.EXAMPLE
    ./eng/stage-review-inbox.ps1

    Default: scan parent of this repo for siblings + this repo's own ai_docs,
    move every audit/retro/promotion file into review-inbox/.

.EXAMPLE
    ./eng/stage-review-inbox.ps1 -DryRun

    Show what would be staged without moving anything.

.EXAMPLE
    ./eng/stage-review-inbox.ps1 -Copy -SiblingRepoParent C:\Customer-Repos

    Copy (don't move) from a different sibling parent.
#>
param(
    [string]$SiblingRepoParent = '',
    [string[]]$ExcludeRepoFolders = @(),
    [string[]]$SearchPaths = @(
        'ai_docs/audit-reports',
        'ai_docs/reports',
        'audit-reports'
    ),
    [switch]$DryRun,
    [switch]$Copy,
    [switch]$SkipSelf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$repoName = Split-Path -Leaf $repoRoot
$inbox = Join-Path $repoRoot 'review-inbox'

if (-not $SiblingRepoParent) {
    $SiblingRepoParent = Split-Path -Parent $repoRoot
}

$filePatterns = @('*_mcp-server-audit.md', '*_experimental-promotion.md', '*_roslyn-mcp-retro.md')

$roots = New-Object System.Collections.Generic.List[string]
if (-not $SkipSelf) { $roots.Add($repoRoot) | Out-Null }

if (Test-Path $SiblingRepoParent) {
    $excludeSet = @{ $repoName = $true }
    foreach ($x in $ExcludeRepoFolders) { $excludeSet[$x] = $true }
    Get-ChildItem -Path $SiblingRepoParent -Directory | Where-Object { -not $excludeSet.ContainsKey($_.Name) } | ForEach-Object {
        $roots.Add($_.FullName) | Out-Null
    }
}

$staged = New-Object System.Collections.Generic.List[pscustomobject]
$skipped = New-Object System.Collections.Generic.List[pscustomobject]

foreach ($root in $roots) {
    foreach ($rel in $SearchPaths) {
        $dir = Join-Path $root $rel
        if (-not (Test-Path $dir)) { continue }
        foreach ($pat in $filePatterns) {
            Get-ChildItem -Path $dir -Filter $pat -File -ErrorAction SilentlyContinue | ForEach-Object {
                $dest = Join-Path $inbox $_.Name
                if (Test-Path $dest) {
                    $skipped.Add([pscustomobject]@{ Source = $_.FullName; Reason = 'already in review-inbox' })
                } else {
                    $staged.Add([pscustomobject]@{ Source = $_.FullName; Dest = $dest })
                }
            }
        }
    }
}

if ($staged.Count -eq 0) {
    Write-Host "No new artifacts found. ($($skipped.Count) already in review-inbox/.)"
    exit 0
}

if (-not (Test-Path $inbox)) {
    if ($DryRun) {
        Write-Host "[DryRun] Would create $inbox"
    } else {
        New-Item -ItemType Directory -Path $inbox | Out-Null
    }
}

$verb = if ($Copy) { 'Copy' } else { 'Move' }
Write-Host "Staging $($staged.Count) artifact(s) -> $inbox ($verb)" -ForegroundColor Cyan

foreach ($item in $staged) {
    if ($DryRun) {
        Write-Host "  [DryRun] $verb $($item.Source) -> $($item.Dest)"
        continue
    }
    if ($Copy) {
        Copy-Item -LiteralPath $item.Source -Destination $item.Dest
    } else {
        Move-Item -LiteralPath $item.Source -Destination $item.Dest
    }
    Write-Host "  $verb $(Split-Path -Leaf $item.Source)"
}

if ($skipped.Count -gt 0) {
    Write-Host ""
    Write-Host "Skipped $($skipped.Count) (already present):" -ForegroundColor DarkGray
    foreach ($s in $skipped) {
        Write-Host "  $($s.Source) ($($s.Reason))" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "Next: invoke the /backlog-intake skill to extract, dedupe, classify, and merge into ai_docs/backlog.md." -ForegroundColor Green
