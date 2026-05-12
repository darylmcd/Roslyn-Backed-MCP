#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Audit experimental-tier entries in ServerSurfaceCatalog.*.cs for age using git blame.

.DESCRIPTION
    Walks every `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs` partial file,
    extracts lines that register experimental-tier surface entries via the `Tool(...)`,
    `Resource(...)`, or `Prompt(...)` factory call pattern (anchored on the string literal
    `"experimental"` in the third argument position), then runs `git blame` on each matching
    line to determine the commit timestamp. Entries older than `$ThresholdDays` days (UTC) are
    collected and emitted as a Markdown table.

    The script is ADVISORY ONLY. It does not auto-promote or auto-deprecate any entry. A non-zero
    exit code is never emitted due to aging — the script exits 0 always.

    NOTE: `git blame` returns the committer-date of the commit that last touched the line. Branch
    imports may inflate the apparent age when large changes land together. This is acceptable for an
    advisory audit script; treat the result as a lower bound on staleness.

    Age is computed in UTC days from the blame commit timestamp to the current UTC date.

.PARAMETER ThresholdDays
    Minimum age in days for an entry to appear in the output table. Default: 180.
    Pass 0 to emit ALL experimental entries regardless of age.

.PARAMETER RepoRoot
    Root of the git repository to inspect. Defaults to the parent directory of the `eng/` folder
    that contains this script (i.e., the Roslyn-Backed-MCP repo root).

.EXAMPLE
    ./eng/audit-experimental-age.ps1

    Emit entries that have been experimental for more than 180 days.

.EXAMPLE
    ./eng/audit-experimental-age.ps1 -ThresholdDays 0

    Emit ALL experimental entries regardless of age.

.EXAMPLE
    ./eng/audit-experimental-age.ps1 -ThresholdDays 90 -RepoRoot C:\Other\Repo

    Run against a different repo root with a 90-day threshold.
#>
[CmdletBinding()]
param(
    [int]$ThresholdDays = 180,
    [string]$RepoRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

# Normalize to absolute path.
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).ProviderPath

$catalogGlob = Join-Path $RepoRoot 'src' 'RoslynMcp.Host.Stdio' 'Catalog' 'ServerSurfaceCatalog.*.cs'
$catalogFiles = @(Get-ChildItem -Path $catalogGlob -ErrorAction SilentlyContinue)

if ($catalogFiles.Count -eq 0) {
    Write-Warning "No catalog partial files found at: $catalogGlob"
    exit 0
}

# Regex: captures method kind (Tool/Resource/Prompt), name (first arg), category (second arg).
# Anchors on the third string argument being "experimental". Handles both single-line and the
# common case where the closing paren is on the same line.
#
# Pattern: Tool|Resource|Prompt followed by optional whitespace, open paren, then:
#   "name"   , "category"  , "experimental"
# Single-quoted strings are NOT used in the catalog, so we only match double-quoted literals.
$entryRegex = [System.Text.RegularExpressions.Regex]::new(
    '(?<kind>Tool|Resource|Prompt)\s*\(\s*"(?<name>[^"]+)"\s*,\s*"(?<category>[^"]+)"\s*,\s*"experimental"',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
)

$nowUtc = [System.DateTime]::UtcNow.Date

$results = New-Object System.Collections.Generic.List[pscustomobject]

foreach ($file in $catalogFiles) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $m = $entryRegex.Match($line)
        if (-not $m.Success) { continue }

        $kind     = $m.Groups['kind'].Value
        $name     = $m.Groups['name'].Value
        $category = $m.Groups['category'].Value
        $lineNo   = $i + 1  # git blame uses 1-based line numbers

        # Relative path from repo root for git blame.
        $relPath = $file.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
        # Normalize to forward slashes for git.
        $relPath = $relPath -replace '\\', '/'

        # Run git blame with --porcelain to get the commit timestamp.
        # We capture author-time (Unix epoch) for accurate commit date.
        $blameArgs = @('blame', '-L', "$lineNo,$lineNo", '--porcelain', $relPath)
        $blameOutput = & git -C $RepoRoot @blameArgs 2>&1

        $commitHash = ''
        $commitDate = [System.DateTime]::MinValue

        if ($LASTEXITCODE -eq 0) {
            foreach ($blameLine in $blameOutput) {
                if ($blameLine -match '^([0-9a-f]{40})\s') {
                    $commitHash = $Matches[1].Substring(0, 8)
                }
                if ($blameLine -match '^author-time\s+(\d+)$') {
                    $epoch = [long]$Matches[1]
                    $commitDate = [System.DateTimeOffset]::FromUnixTimeSeconds($epoch).UtcDateTime.Date
                }
            }
        }

        $ageDays = if ($commitDate -ne [System.DateTime]::MinValue) {
            ($nowUtc - $commitDate).Days
        } else {
            -1
        }

        if ($ageDays -ge $ThresholdDays -or ($ThresholdDays -eq 0 -and $ageDays -ge 0)) {
            $results.Add([pscustomobject]@{
                Kind        = $kind
                Name        = $name
                Category    = $category
                File        = $relPath
                Line        = $lineNo
                AgeDays     = $ageDays
                BlameCommit = $commitHash
                BlameDate   = if ($commitDate -ne [System.DateTime]::MinValue) { $commitDate.ToString('yyyy-MM-dd') } else { 'unknown' }
            }) | Out-Null
        }
    }
}

if ($results.Count -eq 0) {
    Write-Output "No experimental entries found older than $ThresholdDays days."
    exit 0
}

# Sort by age descending.
$sorted = $results | Sort-Object -Property AgeDays -Descending

# Emit Markdown table.
Write-Output ''
Write-Output "## Experimental-Tier Age Audit (threshold: $ThresholdDays days)"
Write-Output ''
Write-Output "Generated: $($nowUtc.ToString('yyyy-MM-dd')) UTC | Entries: $($sorted.Count)"
Write-Output ''
Write-Output '| Kind | Name | Category | File | Line | Age (days) | Blame Commit | Blame Date |'
Write-Output '|------|------|----------|------|------|-----------|--------------|------------|'
foreach ($r in $sorted) {
    $shortFile = Split-Path -Leaf $r.File
    Write-Output "| $($r.Kind) | $($r.Name) | $($r.Category) | $shortFile | $($r.Line) | $($r.AgeDays) | $($r.BlameCommit) | $($r.BlameDate) |"
}
Write-Output ''

exit 0
