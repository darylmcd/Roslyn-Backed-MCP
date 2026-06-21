#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Aggregate per-repo `_latest-promotion-scorecard.json` files from sibling
    audit repos into a single quorum-aware verdict per tool / resource / prompt.

.DESCRIPTION
    Per the per-repo-promotion-scorecard initiative: each audited repo writes
    its canonical `<audited-repo>/audit-reports/_latest-promotion-scorecard.json`
    (repo root) when /mcp-server-stress runs. The legacy
    `<audited-repo>/ai_docs/audit-reports/...` location was removed as a stale
    duplicate (#937); it is retained only as a backward-compat fallback probe,
    NOT as the canonical path. This script gathers the scorecards across
    configured sibling repos under a parent folder and merges them into one
    in-memory map keyed by `<kind>|<name>`. Each entry is then assigned an
    aggregated verdict using a quorum rule:

      * `promote: ready`            — at least 2 sibling repos voted `promote`
                                      AND zero `keep-experimental` votes
                                      AND zero `deprecate` votes.
      * `promote: blocked`          — at least one `keep-experimental` or
                                      `deprecate` vote (a single workspace's
                                      hard-stop blocks the quorum).
      * `needs-more-evidence`       — fewer than 2 `promote` votes and no
                                      blockers. Insufficient sample size to
                                      flip a tier.

    Discovery semantics mirror `eng/stage-review-inbox.ps1` (the convention
    init 5 of the same backlog sweep established): walk every immediate
    subdirectory under `$SiblingRepoParent` (defaults to the parent of this
    repo), skip the running repo itself unless `-IncludeSelf`, and probe
    each candidate for `_latest-promotion-scorecard.json` under any of the
    paths in `$ScorecardSearchPaths`. The default probes the canonical
    repo-root `audit-reports/` location (written by `/mcp-server-surface-test`)
    FIRST, then the deprecated `ai_docs/audit-reports/` location as a
    backward-compat fallback (#937 removed it as canonical). First match wins
    per repo.

    Missing scorecards are NOT errors. The aggregator simply notes
    `missingFromRepos`. Empty configured sibling sets emit a clean
    `no scorecards available` verdict with zero entries.

    Output: a single JSON object on stdout. Shape:

        {
          "schemaVersion": 1,
          "generatedAt": "<UTC ISO>",
          "siblingReposScanned": [...],
          "siblingReposWithScorecard": [...],
          "siblingReposMissingScorecard": [...],
          "entries": [
            {
              "kind": "tool",
              "name": "scaffold_test_apply",
              "category": "scaffolding",
              "currentTier": "experimental",
              "verdict": "promote: ready" | "promote: blocked" | "needs-more-evidence",
              "promoteVotes": 2,
              "keepExperimentalVotes": 0,
              "deprecateVotes": 0,
              "needsMoreEvidenceVotes": 1,
              "sourceRepos": { "promote": [...], "keep-experimental": [...], ... },
              "blockers": [...]
            }
          ],
          "summary": {
            "promoteReady": N,
            "promoteBlocked": N,
            "needsMoreEvidence": N,
            "noScorecardsAvailable": $true|$false
          }
        }

.PARAMETER SiblingRepoParent
    Parent folder to scan for sibling repos. Defaults to the parent of this
    repo (matches stage-review-inbox.ps1's discovery convention).

.PARAMETER ExcludeRepoFolders
    Extra folder names to skip under `$SiblingRepoParent`. The running repo's
    own folder is excluded by default unless `-IncludeSelf` is passed.

.PARAMETER IncludeSelf
    Include this repo itself in the scan. Off by default — the audited-repo
    pattern means scorecards land under each *audited* repo, and this repo
    typically only audits siblings.

.PARAMETER ScorecardSearchPaths
    Per-repo relative paths to probe for `_latest-promotion-scorecard.json`.
    First match wins per repo. The default probes the canonical repo-root
    `audit-reports/` (written by /mcp-server-surface-test) FIRST, then the
    deprecated `ai_docs/audit-reports/` as a backward-compat fallback (#937
    removed the latter as canonical). Order matters: canonical must come first
    so a stale `ai_docs/audit-reports/` copy never shadows the live scorecard.

.PARAMETER OutputFile
    Optional file path. When set, the JSON is written to this path in addition
    to stdout. Useful for callers that want both pipeable JSON and a persisted
    artifact.

.EXAMPLE
    ./eng/aggregate-promotion-scorecards.ps1

    Default: aggregate scorecards from every sibling repo under the parent
    folder. Emit JSON to stdout.

.EXAMPLE
    ./eng/aggregate-promotion-scorecards.ps1 -SiblingRepoParent C:\Customer-Repos -OutputFile aggregated.json

    Aggregate from a custom sibling parent and persist the JSON to disk.
#>
[CmdletBinding()]
param(
    [string]$SiblingRepoParent = '',
    [string[]]$ExcludeRepoFolders = @(),
    [switch]$IncludeSelf,
    # ORDER IS LOAD-BEARING — canonical repo-root path MUST be probed first.
    # #937 made `<repo>/audit-reports/_latest-promotion-scorecard.json` (repo
    # root) the canonical per-repo scorecard and removed the duplicate under
    # `ai_docs/audit-reports/`. The `ai_docs/audit-reports/` entry below is a
    # backward-compat fallback ONLY; demoting it (was first) ensures a stale
    # copy left in a sibling's `ai_docs/audit-reports/` never shadows the live
    # canonical scorecard ("first match wins per repo" in the probe loop).
    # Do NOT reorder these so `ai_docs/audit-reports/` is probed first again.
    # See ai_docs/audit-reports/README.md ("Do not store the ... scorecard here").
    [string[]]$ScorecardSearchPaths = @(
        'audit-reports/_latest-promotion-scorecard.json',
        'ai_docs/audit-reports/_latest-promotion-scorecard.json'
    ),
    [string]$OutputFile = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$repoName = Split-Path -Leaf $repoRoot

if (-not $SiblingRepoParent) {
    $SiblingRepoParent = Split-Path -Parent $repoRoot
}

# Discover candidate repos.
$roots = New-Object System.Collections.Generic.List[pscustomobject]
if ($IncludeSelf) {
    $roots.Add([pscustomobject]@{ Name = $repoName; Path = $repoRoot }) | Out-Null
}

if (Test-Path $SiblingRepoParent) {
    $excludeSet = @{}
    # Always exclude self from sibling-discovery regardless of -IncludeSelf.
    # When -IncludeSelf is set, self is already added explicitly above (line ~145)
    # and must not be re-discovered here — that would double-count it in
    # siblingReposScanned/siblingReposWithScorecard and double its votes in
    # quorum math, potentially spuriously satisfying the 2-vote promote threshold.
    # When -IncludeSelf is absent, self must not be counted at all.
    # The explicit add at line ~145 is the ONE and ONLY self-inclusion path.
    $excludeSet[$repoName] = $true
    foreach ($x in $ExcludeRepoFolders) { $excludeSet[$x] = $true }
    Get-ChildItem -Path $SiblingRepoParent -Directory -ErrorAction SilentlyContinue |
        Where-Object { -not $excludeSet.ContainsKey($_.Name) } |
        ForEach-Object {
            $roots.Add([pscustomobject]@{ Name = $_.Name; Path = $_.FullName }) | Out-Null
        }
}

$siblingReposScanned = New-Object System.Collections.Generic.List[string]
$siblingReposWithScorecard = New-Object System.Collections.Generic.List[string]
$siblingReposMissingScorecard = New-Object System.Collections.Generic.List[string]

# Map: "<kind>|<name>" -> aggregation accumulator.
$entries = @{}

foreach ($root in $roots) {
    $siblingReposScanned.Add($root.Name) | Out-Null

    # Probe each candidate path; first existing scorecard wins. The canonical
    # repo-root `audit-reports/` path (written by /mcp-server-surface-test) is
    # probed first; the deprecated `ai_docs/audit-reports/` path is a
    # backward-compat fallback only (#937 removed it as canonical). See the
    # $ScorecardSearchPaths default above for the ordering rationale.
    $scorecardPath = $null
    foreach ($candidate in $ScorecardSearchPaths) {
        $probe = Join-Path $root.Path $candidate
        if (Test-Path -LiteralPath $probe) { $scorecardPath = $probe; break }
    }
    if ($null -eq $scorecardPath) {
        $siblingReposMissingScorecard.Add($root.Name) | Out-Null
        continue
    }

    try {
        $raw = Get-Content -LiteralPath $scorecardPath -Raw -ErrorAction Stop
        $parsed = $raw | ConvertFrom-Json -ErrorAction Stop
    } catch {
        # Treat malformed scorecards as missing — surface the sibling name so
        # the operator can investigate. Do NOT crash the aggregator.
        $siblingReposMissingScorecard.Add("$($root.Name) (malformed: $($_.Exception.Message))") | Out-Null
        continue
    }

    $siblingReposWithScorecard.Add($root.Name) | Out-Null

    if (-not ($parsed.PSObject.Properties.Name -contains 'scorecard')) { continue }
    if ($null -eq $parsed.scorecard) { continue }

    foreach ($entry in $parsed.scorecard) {
        $kind = if ($entry.PSObject.Properties.Name -contains 'kind') { [string]$entry.kind } else { 'unknown' }
        $name = if ($entry.PSObject.Properties.Name -contains 'name') { [string]$entry.name } else { '' }
        if (-not $name) { continue }
        $key = "$kind|$name"

        if (-not $entries.ContainsKey($key)) {
            $entries[$key] = [pscustomobject]@{
                kind                  = $kind
                name                  = $name
                category              = if ($entry.PSObject.Properties.Name -contains 'category') { [string]$entry.category } else { '' }
                currentTier           = if ($entry.PSObject.Properties.Name -contains 'currentTier') { [string]$entry.currentTier } else { '' }
                promote               = New-Object System.Collections.Generic.List[string]
                keepExperimental      = New-Object System.Collections.Generic.List[string]
                needsMoreEvidence     = New-Object System.Collections.Generic.List[string]
                deprecate             = New-Object System.Collections.Generic.List[string]
                blockers              = New-Object System.Collections.Generic.List[string]
            }
        }

        $rec = if ($entry.PSObject.Properties.Name -contains 'recommendation') { [string]$entry.recommendation } else { '' }
        switch ($rec) {
            'promote'             { $entries[$key].promote.Add($root.Name) | Out-Null }
            'keep-experimental'   { $entries[$key].keepExperimental.Add($root.Name) | Out-Null }
            'needs-more-evidence' { $entries[$key].needsMoreEvidence.Add($root.Name) | Out-Null }
            'deprecate'           { $entries[$key].deprecate.Add($root.Name) | Out-Null }
            default               { } # Ignore unknown recommendations.
        }

        if ($entry.PSObject.Properties.Name -contains 'blockers' -and $null -ne $entry.blockers) {
            foreach ($b in $entry.blockers) {
                if ($b) { $entries[$key].blockers.Add("[$($root.Name)] $b") | Out-Null }
            }
        }
    }
}

# Compute aggregated verdicts.
$aggregated = New-Object System.Collections.Generic.List[pscustomobject]
$promoteReady = 0
$promoteBlocked = 0
$needsMore = 0

foreach ($key in ($entries.Keys | Sort-Object)) {
    $e = $entries[$key]
    $promoteCount = $e.promote.Count
    $blockerCount = $e.keepExperimental.Count + $e.deprecate.Count

    if ($blockerCount -gt 0) {
        $verdict = 'promote: blocked'
        $promoteBlocked++
    } elseif ($promoteCount -ge 2) {
        $verdict = 'promote: ready'
        $promoteReady++
    } else {
        $verdict = 'needs-more-evidence'
        $needsMore++
    }

    $aggregated.Add([pscustomobject]@{
        kind                    = $e.kind
        name                    = $e.name
        category                = $e.category
        currentTier             = $e.currentTier
        verdict                 = $verdict
        promoteVotes            = $promoteCount
        keepExperimentalVotes   = $e.keepExperimental.Count
        deprecateVotes          = $e.deprecate.Count
        needsMoreEvidenceVotes  = $e.needsMoreEvidence.Count
        sourceRepos             = [pscustomobject]@{
            promote             = @($e.promote)
            'keep-experimental' = @($e.keepExperimental)
            'needs-more-evidence' = @($e.needsMoreEvidence)
            deprecate           = @($e.deprecate)
        }
        blockers                = @($e.blockers)
    }) | Out-Null
}

$noScorecards = $siblingReposWithScorecard.Count -eq 0

$result = [pscustomobject]@{
    schemaVersion                  = 1
    generatedAt                    = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    siblingReposScanned            = @($siblingReposScanned)
    siblingReposWithScorecard      = @($siblingReposWithScorecard)
    siblingReposMissingScorecard   = @($siblingReposMissingScorecard)
    entries                        = @($aggregated)
    summary                        = [pscustomobject]@{
        promoteReady          = $promoteReady
        promoteBlocked        = $promoteBlocked
        needsMoreEvidence     = $needsMore
        noScorecardsAvailable = $noScorecards
    }
}

$json = $result | ConvertTo-Json -Depth 12

if ($OutputFile) {
    Set-Content -LiteralPath $OutputFile -Value $json -Encoding UTF8
}

# Always write JSON to stdout so callers can pipe.
Write-Output $json
