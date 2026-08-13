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
          "scorecardStaleness": [
            {
              "repo": "some-sibling",
              "serverVersion": "1.38.1",
              "currentServerVersion": "2.3.8",
              "versionStale": true,
              "generatedAt": "2026-05-16T06:25:47Z",
              "ageDays": 89.1,
              "ageStale": true
            }
          ],
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
            "staleScorecardCount": N,
            "noScorecardsAvailable": $true|$false
          }
        }

    Staleness detection (warn-only). Every consumed scorecard that carries a
    `serverVersion` and/or `generatedAt` contributes one `scorecardStaleness`
    row. A row is stale when its `serverVersion` differs from
    `-CurrentServerVersion` (defaults to this repo's own
    `Directory.Build.props` `<Version>`), or when its `generatedAt` is older
    than `-MaxScorecardAgeDays`. Each stale row also emits a warning line on
    stderr via `[Console]::Error` — deliberately NOT `Write-Warning`, whose
    stream `pwsh -File` renders onto stdout and would corrupt the JSON — and
    increments
    `summary.staleScorecardCount`. This is deliberately NON-fatal: the exit
    code stays 0 so every existing consumer that pipes stdout keeps working.
    A caller that wants a hard gate reads `summary.staleScorecardCount` and
    decides for itself.

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

.PARAMETER CurrentServerVersion
    Server version to compare each scorecard's `serverVersion` against. When
    omitted, it is parsed from this repo's `Directory.Build.props` `<Version>`.
    A scorecard whose `serverVersion` differs is reported as `versionStale`.
    Pass an explicit value to compare against something other than this
    checkout (or to make a test deterministic). If neither the parameter nor
    the props lookup yields a value, the version comparison is skipped.

.PARAMETER MaxScorecardAgeDays
    Maximum age, in days, of a scorecard's `generatedAt` before it is reported
    as `ageStale`. Defaults to 45 — roughly one release cadence, so a scorecard
    that missed a whole cycle surfaces. Set to 0 or a negative value to disable
    the age check.

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
    [string]$CurrentServerVersion = '',
    [int]$MaxScorecardAgeDays = 45,
    [string]$OutputFile = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$repoName = Split-Path -Leaf $repoRoot

if (-not $SiblingRepoParent) {
    $SiblingRepoParent = Split-Path -Parent $repoRoot
}

# Default the comparison baseline to this checkout's own build version. Resolved
# here rather than as a param default so the lookup can fail soft (missing or
# malformed props => version comparison is simply skipped, never a crash).
if (-not $CurrentServerVersion) {
    $propsPath = Join-Path $repoRoot 'Directory.Build.props'
    if (Test-Path -LiteralPath $propsPath) {
        $versionMatch = [regex]::Match(
            (Get-Content -LiteralPath $propsPath -Raw),
            '<Version>\s*([^<\s][^<]*?)\s*</Version>')
        if ($versionMatch.Success) { $CurrentServerVersion = $versionMatch.Groups[1].Value }
    }
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
$scorecardStaleness = New-Object System.Collections.Generic.List[pscustomobject]
$staleScorecardCount = 0

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

    # Staleness check. Deliberately BEFORE the `scorecard` property guard below:
    # a scorecard that carries only header metadata (no entries) is exactly the
    # kind of frozen artifact worth flagging, and skipping it here would hide it.
    # Set-StrictMode -Version Latest is live — every optional field is probed via
    # PSObject.Properties before it is read.
    $scorecardServerVersion = if ($parsed.PSObject.Properties.Name -contains 'serverVersion') { [string]$parsed.serverVersion } else { '' }
    $generatedAtRaw = if ($parsed.PSObject.Properties.Name -contains 'generatedAt') { $parsed.generatedAt } else { $null }

    # ConvertFrom-Json coerces an ISO-8601 string into [datetime], and a bare [string] cast then
    # renders it in the CURRENT CULTURE ("05/16/2026 06:25:47") — culture-dependent noise in a
    # machine-readable field. Normalize back to the canonical UTC form both branches emit.
    $generatedAtUtc = [datetime]::MinValue
    $generatedAtParsed = $false
    if ($generatedAtRaw -is [datetime]) {
        $generatedAtUtc = ([datetime]$generatedAtRaw).ToUniversalTime()
        $generatedAtParsed = $true
    } elseif ($null -ne $generatedAtRaw -and [string]$generatedAtRaw) {
        $generatedAtParsed = [datetime]::TryParse(
            [string]$generatedAtRaw,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$generatedAtUtc)
    }

    $scorecardGeneratedAt = if ($generatedAtParsed) {
        $generatedAtUtc.ToString('yyyy-MM-ddTHH:mm:ssZ', [System.Globalization.CultureInfo]::InvariantCulture)
    } elseif ($null -ne $generatedAtRaw) {
        # Unparseable but present — echo it verbatim so the operator can see the malformed value.
        [string]$generatedAtRaw
    } else {
        ''
    }

    if ($scorecardServerVersion -or $scorecardGeneratedAt) {
        $versionStale = ($scorecardServerVersion -and $CurrentServerVersion -and
            -not [string]::Equals($scorecardServerVersion, $CurrentServerVersion, [StringComparison]::Ordinal))

        $ageDays = $null
        $ageStale = $false
        if ($generatedAtParsed) {
            $ageDays = [math]::Round(((Get-Date).ToUniversalTime() - $generatedAtUtc).TotalDays, 1)
            $ageStale = ($MaxScorecardAgeDays -gt 0 -and $ageDays -gt $MaxScorecardAgeDays)
        }

        $scorecardStaleness.Add([pscustomobject]@{
            repo                 = $root.Name
            serverVersion        = $scorecardServerVersion
            currentServerVersion = $CurrentServerVersion
            versionStale         = [bool]$versionStale
            generatedAt          = $scorecardGeneratedAt
            ageDays              = $ageDays
            ageStale             = [bool]$ageStale
        }) | Out-Null

        if ($versionStale -or $ageStale) {
            $staleScorecardCount++
            $reasons = New-Object System.Collections.Generic.List[string]
            if ($versionStale) { $reasons.Add("serverVersion '$scorecardServerVersion' != current '$CurrentServerVersion'") | Out-Null }
            if ($ageStale) { $reasons.Add("generatedAt '$scorecardGeneratedAt' is $ageDays day(s) old (> $MaxScorecardAgeDays)") | Out-Null }
            # [Console]::Error, NOT Write-Warning. Under `pwsh -File` the warning stream is rendered
            # by the HOST onto stdout (with ANSI colour escapes), which would corrupt the JSON this
            # script contracts to emit there — verified: stdout began with 0x1B `ESC[33;1mWARNING:`.
            # Stdout is JSON and nothing else; every diagnostic goes to stderr.
            [Console]::Error.WriteLine(
                "WARNING: Stale promotion scorecard for '$($root.Name)' ($scorecardPath): $($reasons -join '; '). Re-run /mcp-server-surface-test against that repo.")
        }
    }

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
    scorecardStaleness             = @($scorecardStaleness)
    entries                        = @($aggregated)
    summary                        = [pscustomobject]@{
        promoteReady          = $promoteReady
        promoteBlocked        = $promoteBlocked
        needsMoreEvidence     = $needsMore
        staleScorecardCount   = $staleScorecardCount
        noScorecardsAvailable = $noScorecards
    }
}

$json = $result | ConvertTo-Json -Depth 12

if ($OutputFile) {
    Set-Content -LiteralPath $OutputFile -Value $json -Encoding UTF8
}

# Always write JSON to stdout so callers can pipe.
Write-Output $json
