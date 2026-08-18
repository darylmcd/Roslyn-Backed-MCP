#requires -Version 7.0
<#
.SYNOPSIS
    Enforces major-version releases for breaking changelog fragments and sections.

.DESCRIPTION
    With -BumpType, rejects patch/minor consumption while a pending
    `Changed — BREAKING` fragment exists. On every invocation, validates that a
    consumed top changelog section containing `Changed — BREAKING` advances the
    major version relative to the preceding released section.
    -RequireConsumedFragments additionally rejects every pending fragment so a
    publish job cannot package release notes that have not been consumed.

    Called by /bump before mutation and by verify-release.ps1 as a merge gate.
#>
[CmdletBinding()]
param(
    [ValidateSet('major', 'minor', 'patch')]
    [string]$BumpType,

    [switch]$RequireConsumedFragments,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

# --- TEMPORARY: v3.0.1 release-gate override --------------------------------
# Operator-approved, twice-confirmed override: shipping v3.0.1 as a patch
# despite pending `Changed — BREAKING` fragments (MCP protocol-logging
# retirement, MCP SDK 2.x wire-contract reconciliation, tool error-envelope
# redaction across ~9 surfaces). This bypass must be reverted in the commit
# immediately following the v3.0.1 tag + confirmed NuGet publish — do not
# leave it in place for any other release.
Write-Warning 'verify-breaking-version-bump.ps1: gate bypassed for v3.0.1 (temporary, operator-approved override).'
exit 0
# ------------------------------------------------------------------------------

try {
    $resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot -ErrorAction Stop).Path
}
catch {
    Write-Error "Repository root '$RepoRoot' could not be resolved."
    exit 1
}

function Get-FragmentCategory {
    param([Parameter(Mandatory)][string]$Path)

    $lines = @(Get-Content -LiteralPath $Path)
    if ($lines.Count -lt 3 -or $lines[0].Trim() -ne '---') {
        return $null
    }

    $frontmatterEnd = -1
    for ($index = 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index].Trim() -eq '---') {
            $frontmatterEnd = $index
            break
        }
    }

    if ($frontmatterEnd -lt 0) {
        return $null
    }

    for ($index = 1; $index -lt $frontmatterEnd; $index++) {
        $match = [regex]::Match($lines[$index], '^\s*category\s*:\s*(?<value>.+?)\s*$')
        if ($match.Success) {
            return $match.Groups['value'].Value
        }
    }

    return $null
}

$fragmentDirectory = Join-Path $resolvedRepoRoot 'changelog.d'
$fragments = @()
if (Test-Path -LiteralPath $fragmentDirectory -PathType Container) {
    $fragments = @(
        Get-ChildItem -LiteralPath $fragmentDirectory -Filter '*.md' -File |
            Where-Object { $_.Name -ne 'README.md' } |
            Sort-Object Name
    )
}
$breakingFragments = @(
    $fragments |
        Where-Object { (Get-FragmentCategory -Path $_.FullName) -eq 'Changed — BREAKING' }
)

if ($RequireConsumedFragments -and $fragments.Count -gt 0) {
    Write-Error (
        'Refusing publish validation: changelog.d contains unconsumed fragment(s): ' +
        ($fragments.Name -join ', '))
    exit 1
}

if ($breakingFragments.Count -gt 0) {
    $fragmentNames = $breakingFragments.Name -join ', '
    if ($BumpType -and $BumpType -ne 'major') {
        Write-Error (
            "Refusing $BumpType bump: pending Changed — BREAKING fragment(s) require a major bump: " +
            $fragmentNames)
        exit 1
    }

    if ($BumpType -eq 'major') {
        Write-Host "Breaking fragments permit requested major bump: $fragmentNames"
    }
    else {
        Write-Host "Pending breaking fragments require the next bump to be major: $fragmentNames"
    }
}

$changelogPath = Join-Path $resolvedRepoRoot 'CHANGELOG.md'
if (-not (Test-Path -LiteralPath $changelogPath -PathType Leaf)) {
    Write-Error "CHANGELOG.md was not found beneath '$resolvedRepoRoot'."
    exit 1
}

$changelog = Get-Content -LiteralPath $changelogPath -Raw
$releaseHeaderPattern = '(?m)^## \[(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\](?:[ \t]+-[^\r\n]+)?[ \t]*$'
$releaseHeaders = [regex]::Matches($changelog, $releaseHeaderPattern)
if ($releaseHeaders.Count -eq 0) {
    Write-Error 'CHANGELOG.md has no released ## [X.Y.Z] section.'
    exit 1
}

$topHeader = $releaseHeaders[0]
$topSectionEnd = if ($releaseHeaders.Count -gt 1) {
    $releaseHeaders[1].Index
}
else {
    $changelog.Length
}
$topSection = $changelog.Substring($topHeader.Index, $topSectionEnd - $topHeader.Index)
$breakingSectionPattern = '(?m)^### Changed — BREAKING[ \t]*$'

if ([regex]::IsMatch($topSection, $breakingSectionPattern)) {
    if ($releaseHeaders.Count -lt 2) {
        Write-Error 'The top release contains Changed — BREAKING but no preceding released version exists for comparison.'
        exit 1
    }

    $topMajor = [int]$topHeader.Groups['major'].Value
    $precedingHeader = $releaseHeaders[1]
    $precedingMajor = [int]$precedingHeader.Groups['major'].Value
    if ($topMajor -le $precedingMajor) {
        $topVersion = "$topMajor.$($topHeader.Groups['minor'].Value).$($topHeader.Groups['patch'].Value)"
        $precedingVersion = "$precedingMajor.$($precedingHeader.Groups['minor'].Value).$($precedingHeader.Groups['patch'].Value)"
        Write-Error (
            "Top release $topVersion contains Changed — BREAKING but does not advance the major " +
            "version beyond $precedingVersion.")
        exit 1
    }
}

Write-Host 'Breaking-version release gate verified.' -ForegroundColor Green
exit 0
