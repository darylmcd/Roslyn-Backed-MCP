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

function Get-FragmentFamilyStem {
    param([Parameter(Mandatory)][string]$Name)

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($Name)
    $segments = @($baseName -split '-' | Where-Object { $_.Length -gt 0 })
    if ($segments.Count -lt 3) {
        return $null
    }

    $suffixWidth = if ($segments.Count -ge 5) { 2 } else { 1 }
    return ($segments[0..($segments.Count - $suffixWidth - 1)] -join '-')
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
        foreach ($fragment in $breakingFragments) {
            Write-Host "BREAKING FRAGMENT CONFIRMATION: $($fragment.Name)"
            Write-Host (Get-Content -LiteralPath $fragment.FullName -Raw)

            $familyStem = Get-FragmentFamilyStem -Name $fragment.Name
            if (-not [string]::IsNullOrWhiteSpace($familyStem)) {
                $familySiblings = @(
                    $fragments |
                        Where-Object {
                            $_.Name -ne $fragment.Name -and
                            [System.IO.Path]::GetFileNameWithoutExtension($_.Name).StartsWith(
                                "$familyStem-",
                                [System.StringComparison]::Ordinal)
                        }
                )
                $breakingSiblings = @(
                    $familySiblings |
                        Where-Object { (Get-FragmentCategory -Path $_.FullName) -eq 'Changed — BREAKING' }
                )
                if ($familySiblings.Count -gt 0 -and $breakingSiblings.Count -eq 0) {
                    Write-Warning (
                        "Breaking fragment family mismatch for '$($fragment.Name)': " +
                        'all sibling fragments are non-breaking: ' +
                        ($familySiblings.Name -join ', '))
                }
            }
        }
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
$releaseHeaderPattern = '(?m)^## \[(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\](?:[ \t]+-[^\r\n]+)?[ \t]*\r?$'
$releaseHeaders = [regex]::Matches($changelog, $releaseHeaderPattern)
if ($releaseHeaders.Count -eq 0) {
    # A header that is present but off-contract is a different operator problem than a
    # missing release section; reporting both as 'no released section' sends the reader
    # hunting for a heading that is plainly there.
    if ([regex]::IsMatch($changelog, '(?m)^## \[\d+\.\d+\.\d+\]')) {
        Write-Error (
            'CHANGELOG.md contains ## [X.Y.Z] headers but none match the release-header ' +
            "contract. Expected '## [X.Y.Z]' or '## [X.Y.Z] - <date>' with nothing else on " +
            'the line.')
    }
    else {
        Write-Error 'CHANGELOG.md has no released ## [X.Y.Z] section.'
    }
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
$breakingSectionPattern = '(?m)^### Changed — BREAKING[ \t]*\r?$'

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
