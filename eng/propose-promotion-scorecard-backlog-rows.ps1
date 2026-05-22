#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Propose backlog rows for non-ready promotion-scorecard verdicts.

.DESCRIPTION
    Reads an aggregated promotion scorecard JSON file and emits stable,
    idempotent backlog-row proposals for entries with verdict
    `promote: blocked` or `needs-more-evidence`. Entries already ready for
    promotion are reported in `skipped` so callers can keep routing them through
    the existing promote-tier path instead of turning them into backlog debt.
#>
[CmdletBinding()]
param(
    [string]$AggregatedScorecardPath = '',
    [string]$ExistingBacklogPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $AggregatedScorecardPath) {
    $AggregatedScorecardPath = Join-Path $repoRoot 'audit-reports/_aggregated-promotion-scorecard.json'
}
if (-not $ExistingBacklogPath) {
    $ExistingBacklogPath = Join-Path $repoRoot 'ai_docs/backlog.md'
}

if (-not (Test-Path -LiteralPath $AggregatedScorecardPath)) {
    throw "Aggregated promotion scorecard not found: $AggregatedScorecardPath"
}

function ConvertTo-BacklogIdPart {
    param([Parameter(Mandatory)][string]$Value)

    $lower = $Value.ToLowerInvariant()
    $slug = [regex]::Replace($lower, '[^a-z0-9]+', '-')
    $slug = [regex]::Replace($slug, '-+', '-').Trim('-')
    if (-not $slug) { return 'unknown' }
    return $slug
}

function Test-BacklogContainsRow {
    param(
        [Parameter(Mandatory)][string]$BacklogPath,
        [Parameter(Mandatory)][string]$RowId
    )

    if (-not (Test-Path -LiteralPath $BacklogPath)) {
        return $false
    }

    $text = Get-Content -LiteralPath $BacklogPath -Raw
    return $text.Contains("``$RowId``", [StringComparison]::Ordinal)
}

$raw = Get-Content -LiteralPath $AggregatedScorecardPath -Raw
$scorecard = $raw | ConvertFrom-Json
$proposals = New-Object System.Collections.Generic.List[pscustomobject]
$skipped = New-Object System.Collections.Generic.List[pscustomobject]

foreach ($entry in @($scorecard.entries)) {
    $kind = [string]$entry.kind
    $name = [string]$entry.name
    $verdict = [string]$entry.verdict

    if ($verdict -eq 'promote: blocked') {
        $suffix = 'blocked'
        $nextDeliverable = 'create or update a backlog row for the blocker before promotion is retried'
    } elseif ($verdict -eq 'needs-more-evidence') {
        $suffix = 'needs-more-evidence'
        $nextDeliverable = 'record the missing evidence and decide whether to run more audits or keep the surface experimental'
    } else {
        $reason = if ($verdict -eq 'promote: ready') { 'promote-ready' } else { 'unsupported-verdict' }
        $skipped.Add([pscustomobject]@{
            kind = $kind
            name = $name
            verdict = $verdict
            reason = $reason
        }) | Out-Null
        continue
    }

    $id = 'promotion-scorecard-{0}-{1}-{2}' -f (ConvertTo-BacklogIdPart $kind), (ConvertTo-BacklogIdPart $name), $suffix
    $action = if (Test-BacklogContainsRow -BacklogPath $ExistingBacklogPath -RowId $id) { 'update-existing' } else { 'add-new' }
    $blockers = @(@($entry.blockers) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    $blockerText = if ($blockers.Count -gt 0) {
        ' Blockers: ' + ($blockers -join '; ') + '.'
    } else {
        ''
    }
    $sourceRepos = @()
    if ($entry.PSObject.Properties.Name -contains 'sourceRepos' -and $null -ne $entry.sourceRepos) {
        foreach ($property in $entry.sourceRepos.PSObject.Properties) {
            foreach ($repo in @($property.Value)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$repo)) {
                    $sourceRepos += ('{0}:{1}' -f $property.Name, $repo)
                }
            }
        }
    }
    $sourceRepoText = if ($sourceRepos.Count -gt 0) { ' Source repos: ' + (($sourceRepos | Sort-Object -Unique) -join ', ') + '.' } else { '' }

    $do = "Promotion scorecard verdict ``$verdict`` for $kind ``$name`` means this entry should not silently disappear from release prep.$blockerText$sourceRepoText Next deliverable: $nextDeliverable. Anchors: audit-reports/_aggregated-promotion-scorecard.json. Evidence: aggregated promotion scorecard entry ``$kind/$name``."

    $proposals.Add([pscustomobject]@{
        id = $id
        pri = 'Low'
        deps = 'none'
        action = $action
        kind = $kind
        name = $name
        verdict = $verdict
        do = $do
    }) | Out-Null
}

$result = [pscustomobject]@{
    schemaVersion = 1
    source = $AggregatedScorecardPath
    proposals = @($proposals)
    skipped = @($skipped)
    summary = [pscustomobject]@{
        proposed = $proposals.Count
        skipped = $skipped.Count
    }
}

$result | ConvertTo-Json -Depth 12
