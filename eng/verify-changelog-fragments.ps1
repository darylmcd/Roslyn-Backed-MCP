#requires -Version 7.0
<#
.SYNOPSIS
    Validates changelog.d/*.md fragments have proper YAML frontmatter before a release bump.

.DESCRIPTION
    Scans changelog.d/*.md (excluding README.md). For each fragment, verifies:
      - File starts with a --- frontmatter block
      - Frontmatter has a 'category' key
      - Category is one of the five canonical values
      - File has non-empty body after the closing ---

    A malformed fragment discovered here is a PR-time catch rather than a release-cut
    surprise. Called from verify-release.ps1.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$fragmentDir = Join-Path $repoRoot 'changelog.d'

$validCategories = @('Fixed', 'Changed', 'Changed — BREAKING', 'Added', 'Maintenance')

$fragments = Get-ChildItem -Path $fragmentDir -Filter '*.md' |
    Where-Object { $_.Name -ne 'README.md' }

if ($fragments.Count -eq 0) {
    Write-Host "changelog.d/ — no fragments (ok)"
    exit 0
}

$errors = @()

foreach ($file in $fragments) {
    $content = Get-Content $file.FullName -Raw

    # Must start with ---
    if (-not $content.TrimStart().StartsWith('---')) {
        $errors += "$($file.Name): missing YAML frontmatter (file must start with ---)"
        continue
    }

    # Find the closing ---
    $lines = $content -split "`n"
    $frontmatterEnd = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '---') {
            $frontmatterEnd = $i
            break
        }
    }

    if ($frontmatterEnd -eq -1) {
        $errors += "$($file.Name): YAML frontmatter not closed (no second ---)"
        continue
    }

    # Extract category value
    $frontmatter = $lines[1..($frontmatterEnd - 1)] -join "`n"
    if ($frontmatter -notmatch 'category\s*:\s*(.+)') {
        $errors += "$($file.Name): missing 'category' key in frontmatter"
        continue
    }
    $category = $Matches[1].Trim()

    if ($validCategories -notcontains $category) {
        $errors += "$($file.Name): invalid category '$category' — expected one of: $($validCategories -join ' / ')"
        continue
    }

    # Must have non-empty body
    $body = ($lines[($frontmatterEnd + 1)..($lines.Count - 1)] -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($body)) {
        $errors += "$($file.Name): no body content after frontmatter"
    }
}

if ($errors.Count -gt 0) {
    $msg = "changelog.d/ fragment validation failed ($($errors.Count) error(s)):`n" +
           ($errors -join "`n")
    Write-Error $msg
    exit 1
}

Write-Host "changelog.d/ — $($fragments.Count) fragment(s) valid"
