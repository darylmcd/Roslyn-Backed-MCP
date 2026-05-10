#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-file end-to-end workflow for processing audit reports across this repo + sibling repos.

.DESCRIPTION
    Wraps the two existing maintenance scripts into a single invocation so the
    full audit-report consolidation pipeline runs from one command:

      1. eng/stage-review-inbox.ps1
         Discovers `*_mcp-server-audit.md`, `*_mcp-server-surface-test.md`,
         `*_experimental-promotion.md`, and `*_roslyn-mcp-retro.md` under
         either `audit-reports/` or `ai_docs/audit-reports/` across this
         repo and every sibling under the parent folder. Self -> COPY,
         siblings -> MOVE (defaults). Reports land in `review-inbox/`.

      2. eng/aggregate-promotion-scorecards.ps1
         Probes both `ai_docs/audit-reports/` and top-level `audit-reports/`
         for `_latest-promotion-scorecard.json` per repo, applies the quorum
         rule, writes the consolidated JSON to `audit-reports/_aggregated-promotion-scorecard.json`.

      3. Prints the next step — invoke `/backlog-intake` (or `/backlog-intake --publish`)
         to triage `review-inbox/` into `ai_docs/backlog.md` rows and (optionally)
         file Issues via the shared renderer. The intake skill archives processed
         files into `review-inbox/archive/<batch-id>/` automatically.

    No fully-automated /backlog-intake step exists because /backlog-intake is a
    Claude Code skill (judgment work) rather than a CLI script (mechanical work).
    This wrapper handles every mechanical phase and leaves the judgment phase
    for the operator to invoke explicitly.

.PARAMETER DryRun
    Pass-through: forwards -DryRun to stage-review-inbox.ps1 (aggregate is read-only
    and always safe to run).

.PARAMETER CopyFromSiblings
    Pass-through: forwards -CopyFromSiblings to stage-review-inbox.ps1. Use when
    sibling audits are still mid-flight and you want a snapshot without disturbing
    the source repos.

.PARAMETER SkipAggregate
    Skip the aggregate-promotion-scorecards step (e.g., when only staging is wanted).

.PARAMETER SkipStage
    Skip the stage-review-inbox step (e.g., when reports are already in review-inbox/
    and only the scorecard rollup is needed).

.PARAMETER SiblingRepoParent
    Forwarded to both child scripts. Defaults to the parent of this repo.

.EXAMPLE
    pwsh ./eng/process-audit-reports.ps1

    Run the full pipeline: stage (move-from-siblings + copy-from-self), aggregate,
    print next step.

.EXAMPLE
    pwsh ./eng/process-audit-reports.ps1 -DryRun

    Show what would be staged without copying / moving anything. Aggregate still
    runs (read-only).
#>
[CmdletBinding()]
param(
    [string]$SiblingRepoParent = '',
    [switch]$DryRun,
    [switch]$CopyFromSiblings,
    [switch]$SkipAggregate,
    [switch]$SkipStage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$stageScript    = Join-Path $PSScriptRoot 'stage-review-inbox.ps1'
$aggregateScript = Join-Path $PSScriptRoot 'aggregate-promotion-scorecards.ps1'

if (-not (Test-Path -LiteralPath $stageScript))     { throw "Missing dependency: $stageScript" }
if (-not (Test-Path -LiteralPath $aggregateScript)) { throw "Missing dependency: $aggregateScript" }

# ---------- Step 1: stage ----------
if (-not $SkipStage) {
    Write-Host "=== Step 1/3: stage-review-inbox ===" -ForegroundColor Cyan
    $stageArgs = @{}
    if ($DryRun)            { $stageArgs['DryRun'] = $true }
    if ($CopyFromSiblings)  { $stageArgs['CopyFromSiblings'] = $true }
    if ($SiblingRepoParent) { $stageArgs['SiblingRepoParent'] = $SiblingRepoParent }
    & $stageScript @stageArgs
    Write-Host ""
} else {
    Write-Host "=== Step 1/3: stage-review-inbox SKIPPED (-SkipStage) ===" -ForegroundColor DarkGray
    Write-Host ""
}

# ---------- Step 2: aggregate ----------
if (-not $SkipAggregate) {
    Write-Host "=== Step 2/3: aggregate-promotion-scorecards ===" -ForegroundColor Cyan
    $aggregatedDir = Join-Path $repoRoot 'audit-reports'
    if (-not (Test-Path -LiteralPath $aggregatedDir)) {
        if ($DryRun) {
            Write-Host "[DryRun] Would create $aggregatedDir"
        } else {
            New-Item -ItemType Directory -Path $aggregatedDir | Out-Null
        }
    }
    $aggregatedFile = Join-Path $aggregatedDir '_aggregated-promotion-scorecard.json'

    $aggregateArgs = @{}
    if ($SiblingRepoParent) { $aggregateArgs['SiblingRepoParent'] = $SiblingRepoParent }
    if (-not $DryRun)       { $aggregateArgs['OutputFile'] = $aggregatedFile }

    $json = & $aggregateScript @aggregateArgs
    $parsed = $json | ConvertFrom-Json
    $summary = $parsed.summary
    $with    = $parsed.siblingReposWithScorecard.Count
    $missing = $parsed.siblingReposMissingScorecard.Count

    Write-Host "  Sibling scorecards found: $with (missing: $missing)"
    Write-Host "  Verdict counts: promoteReady=$($summary.promoteReady) promoteBlocked=$($summary.promoteBlocked) needsMoreEvidence=$($summary.needsMoreEvidence)"
    if (-not $DryRun) {
        Write-Host "  Wrote: $aggregatedFile" -ForegroundColor Green
    }
    Write-Host ""
} else {
    Write-Host "=== Step 2/3: aggregate-promotion-scorecards SKIPPED (-SkipAggregate) ===" -ForegroundColor DarkGray
    Write-Host ""
}

# ---------- Step 3: next-step pointer ----------
Write-Host "=== Step 3/3: invoke /backlog-intake ===" -ForegroundColor Cyan
Write-Host "  In Claude Code, run:"
Write-Host "    /backlog-intake             # extract -> backlog.md rows; archives processed files"
Write-Host "    /backlog-intake --publish   # also files Issues via the shared renderer"
Write-Host ""
Write-Host "  /backlog-intake archives consumed reports into review-inbox/archive/<batch-id>/"
Write-Host "  automatically — no manual cleanup needed."
