#requires -Version 7.0
<#
.SYNOPSIS
    Validates changelog fragments and requires one for change-bearing work.

.DESCRIPTION
    Validates every current changelog.d/*.md fragment (excluding README.md),
    then compares the branch, index, worktree, and untracked files with main.
    Shipped changes require at least one valid fragment changed by the current
    work. Internal ai_docs planning/provenance is exempt.

    A release bump may consume all fragments without creating a replacement
    only when the change set is confined to consumed fragments and all seven
    canonical version files. Called from verify-release.ps1.
#>

[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$fragmentDir = Join-Path $repoRoot 'changelog.d'
$validCategories = @('Fixed', 'Changed', 'Changed — BREAKING', 'Added', 'Maintenance')
$releaseVersionPaths = @(
    'Directory.Build.props',
    '.claude-plugin/plugin.json',
    '.claude-plugin/marketplace.json',
    'manifest.json',
    '.claude-plugin/mcp.json',
    '.claude-plugin/server.json',
    'CHANGELOG.md'
)

function Invoke-Git {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = @(& git -C $repoRoot @Arguments 2>$null)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "git $($Arguments[0]) failed while evaluating changelog requirements (exit $exitCode)."
    }

    return @($output | ForEach-Object { $_.ToString() })
}

function Get-ChangedPaths {
    $insideWorkTree = (& git -C $repoRoot rev-parse --is-inside-work-tree 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $insideWorkTree -ne 'true') {
        throw "Changelog change enforcement requires a git worktree: $repoRoot"
    }

    $paths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $baseCandidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_BASE_REF)) {
        $baseCandidates.Add("origin/$($env:GITHUB_BASE_REF)")
        $baseCandidates.Add($env:GITHUB_BASE_REF)
    }
    $baseCandidates.Add('origin/main')
    $baseCandidates.Add('main')

    $baseRef = $null
    foreach ($candidate in ($baseCandidates | Select-Object -Unique)) {
        & git -C $repoRoot rev-parse --verify --quiet $candidate *> $null
        if ($LASTEXITCODE -eq 0) {
            $baseRef = $candidate
            break
        }
    }

    if ($null -eq $baseRef) {
        throw 'Changelog change enforcement could not resolve the target branch. Fetch full git history before validation.'
    }

    foreach ($path in (Invoke-Git @('diff', '--name-only', "$baseRef...HEAD"))) {
        [void] $paths.Add($path.Replace('\', '/'))
    }

    foreach ($arguments in @(
        @('diff', '--name-only'),
        @('diff', '--cached', '--name-only'),
        @('ls-files', '--others', '--exclude-standard'))) {
        foreach ($path in (Invoke-Git $arguments)) {
            [void] $paths.Add($path.Replace('\', '/'))
        }
    }

    return @($paths)
}

function Test-IsChangeBearingPath {
    param([Parameter(Mandatory)][string] $Path)

    return $Path -ne 'CHANGELOG.md' -and
        $Path -notlike 'changelog.d/*' -and
        $Path -notlike 'ai_docs/*'
}

function Test-IsStrictAssembledRelease {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $ChangedPaths,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.IO.FileInfo[]] $CurrentFragments
    )

    if ($CurrentFragments.Count -ne 0) {
        return $false
    }

    $changedFragmentPaths = @($ChangedPaths | Where-Object {
        $_ -like 'changelog.d/*.md' -and $_ -ne 'changelog.d/README.md'
    })
    if ($changedFragmentPaths.Count -eq 0) {
        return $false
    }

    foreach ($fragmentPath in $changedFragmentPaths) {
        if (Test-Path -LiteralPath (Join-Path $repoRoot $fragmentPath)) {
            return $false
        }
    }

    foreach ($requiredPath in $releaseVersionPaths) {
        if ($ChangedPaths -notcontains $requiredPath) {
            return $false
        }
    }

    $unexpectedPaths = @($ChangedPaths | Where-Object {
        $releaseVersionPaths -notcontains $_ -and $_ -notlike 'changelog.d/*.md'
    })
    return $unexpectedPaths.Count -eq 0
}

if (-not (Test-Path -LiteralPath $fragmentDir -PathType Container)) {
    throw "Missing changelog fragment directory: $fragmentDir"
}

$fragments = @(Get-ChildItem -LiteralPath $fragmentDir -Filter '*.md' -File |
    Where-Object { $_.Name -ne 'README.md' })
$errors = [System.Collections.Generic.List[string]]::new()

foreach ($file in $fragments) {
    if ($file.Name -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*\.md$') {
        $errors.Add("$($file.Name): filename must be lowercase kebab-case")
    }

    $content = Get-Content -LiteralPath $file.FullName -Raw
    $lines = $content -split "\r?\n"
    if ($lines.Count -eq 0 -or $lines[0].Trim() -ne '---') {
        $errors.Add("$($file.Name): missing YAML frontmatter (file must start with ---)")
        continue
    }

    $frontmatterEnd = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '---') {
            $frontmatterEnd = $i
            break
        }
    }

    if ($frontmatterEnd -eq -1) {
        $errors.Add("$($file.Name): YAML frontmatter not closed (no second ---)")
        continue
    }

    $frontmatter = if ($frontmatterEnd -gt 1) {
        $lines[1..($frontmatterEnd - 1)] -join "`n"
    }
    else {
        ''
    }
    $categoryMatches = [regex]::Matches($frontmatter, '(?m)^category\s*:\s*(.+?)\s*$')
    if ($categoryMatches.Count -eq 0) {
        $errors.Add("$($file.Name): missing 'category' key in frontmatter")
        continue
    }
    if ($categoryMatches.Count -gt 1) {
        $errors.Add("$($file.Name): duplicate 'category' keys in frontmatter")
        continue
    }

    $category = $categoryMatches[0].Groups[1].Value.Trim()
    if ($validCategories -notcontains $category) {
        $errors.Add(
            "$($file.Name): invalid category '$category' — expected one of: $($validCategories -join ' / ')")
        continue
    }

    $bodyLines = if ($frontmatterEnd + 1 -lt $lines.Count) {
        @($lines[($frontmatterEnd + 1)..($lines.Count - 1)])
    }
    else {
        @()
    }
    $body = ($bodyLines -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($body)) {
        $errors.Add("$($file.Name): no body content after frontmatter")
        continue
    }

    $nonBlankBodyLines = @($bodyLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($nonBlankBodyLines.Count -ne 1) {
        $errors.Add("$($file.Name): body must contain exactly one nonblank bullet line")
        continue
    }

    $firstBodyLine = $nonBlankBodyLines[0].Trim()
    $expectedPrefix = "- **$category`:**"
    if (-not $firstBodyLine.StartsWith($expectedPrefix, [System.StringComparison]::Ordinal)) {
        $errors.Add(
            "$($file.Name): first body line must begin '$expectedPrefix' to match frontmatter category")
        continue
    }

    $summary = $firstBodyLine.Substring($expectedPrefix.Length).TrimStart()
    $duplicateCategory = $validCategories | Where-Object {
        $summary.StartsWith("**$_`:**", [System.StringComparison]::Ordinal)
    }
    if ($null -ne $duplicateCategory) {
        $errors.Add("$($file.Name): body repeats a leading category after '$expectedPrefix'")
    }
}

if ($errors.Count -gt 0) {
    $message = "changelog.d/ fragment validation failed ($($errors.Count) error(s)):`n" +
        ($errors -join "`n")
    Write-Error $message
    exit 1
}

$changedPaths = @(Get-ChangedPaths)
$changeBearingPaths = @($changedPaths | Where-Object { Test-IsChangeBearingPath $_ })
$changedCurrentFragments = @($fragments | Where-Object {
    $changedPaths -contains "changelog.d/$($_.Name)"
})
$assembledRelease = Test-IsStrictAssembledRelease `
    -ChangedPaths $changedPaths `
    -CurrentFragments $fragments

if ($changeBearingPaths.Count -gt 0 -and
    $changedCurrentFragments.Count -eq 0 -and
    -not $assembledRelease) {
    $samplePaths = $changeBearingPaths[0..([Math]::Min(4, $changeBearingPaths.Count - 1))]
    Write-Error (
        'Change-bearing work requires a changed, validated changelog.d/<row-id>.md fragment. ' +
        "Changed paths include: $($samplePaths -join ', ')")
    exit 1
}

if ($assembledRelease) {
    Write-Host 'Changelog fragment verification passed (strict assembled release).'
    exit 0
}

Write-Host "changelog.d/ fragment verification passed ($($fragments.Count) current; $($changedCurrentFragments.Count) changed)."
