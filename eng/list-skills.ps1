<#
.SYNOPSIS
    Lists all SKILL.md files with their frontmatter fields, including the `installed_as:` contract field.

.DESCRIPTION
    Walks `.claude/skills/*/SKILL.md` (maintainer-only, not shipped) and `skills/*/SKILL.md`
    (shipped plugin skills) from the repository root.  For each file, parses the YAML frontmatter
    and emits a formatted table with columns:

        name | installed_as | path

    When `installed_as:` is absent from the frontmatter, the column shows `[missing]`.
    This audits the required `installed_as:` discovery contract. Missing values are reported
    for repair; the repository's active contract test rejects them in CI.

.PARAMETER RepoRoot
    Path to the repository root.  Defaults to the parent of the directory containing this script.

.EXAMPLE
    pwsh -NoProfile -File eng/list-skills.ps1
    # Emits one row per skill and identifies any missing contract values.

.EXAMPLE
    pwsh -NoProfile -File eng/list-skills.ps1 -RepoRoot C:/Code-Repo/Roslyn-Backed-MCP
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

# Collect SKILL.md files from both skill directories.
$skillDirs = @(
    (Join-Path $RepoRoot '.claude' 'skills'),
    (Join-Path $RepoRoot 'skills')
)

$skillFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()

foreach ($dir in $skillDirs) {
    if (Test-Path $dir) {
        $found = Get-ChildItem -Path $dir -Recurse -File -Filter 'SKILL.md'
        foreach ($f in $found) {
            $skillFiles.Add($f)
        }
    }
}

if ($skillFiles.Count -eq 0) {
    Write-Host "No SKILL.md files found under .claude/skills/ or skills/."
    exit 0
}

# Parse frontmatter and collect rows.
$rows = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($file in $skillFiles) {
    $contents = Get-Content -LiteralPath $file.FullName -Raw

    # Extract the leading `---\n ... \n---\n` frontmatter block (non-greedy, Singleline).
    $frontmatterMatch = [regex]::Match($contents, '(?s)^---\s*\r?\n(?<body>.*?)\r?\n---\s*\r?\n')

    $name = '[no frontmatter]'
    $installedAs = '[missing]'

    if ($frontmatterMatch.Success) {
        $body = $frontmatterMatch.Groups['body'].Value
        foreach ($raw in ($body -split '\r?\n')) {
            $line = $raw.TrimEnd()
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $colon = $line.IndexOf(':')
            if ($colon -lt 0) { continue }
            $key   = $line.Substring(0, $colon).Trim()
            $value = $line.Substring($colon + 1).Trim()
            # Strip surrounding double quotes.
            if ($value.Length -ge 2 -and $value[0] -eq '"' -and $value[-1] -eq '"') {
                $value = $value.Substring(1, $value.Length - 2)
            }
            if ($key -eq 'name')         { $name         = $value }
            if ($key -eq 'installed_as') { $installedAs   = $value }
        }
    }

    # Compute a relative path for display (prefer forward slashes).
    $rel = $file.FullName
    if ($rel.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $rel = $rel.Substring($RepoRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    }

    $rows.Add([PSCustomObject]@{
        name         = $name
        installed_as = $installedAs
        path         = $rel
    })
}

# Sort by path for stable output.
$sorted = $rows | Sort-Object path

# Determine column widths for aligned table output.
$nameWidth  = [Math]::Max('name'.Length,         ($sorted | ForEach-Object { $_.name.Length }         | Measure-Object -Maximum).Maximum)
$asWidth    = [Math]::Max('installed_as'.Length,  ($sorted | ForEach-Object { $_.installed_as.Length } | Measure-Object -Maximum).Maximum)

$separator = ('-' * $nameWidth) + '-+-' + ('-' * $asWidth) + '-+-' + '-' * 10
$header    = ('{0,-' + $nameWidth + '} | {1,-' + $asWidth + '} | {2}') -f 'name', 'installed_as', 'path'

Write-Host ""
Write-Host $header
Write-Host $separator
foreach ($row in $sorted) {
    $line = ('{0,-' + $nameWidth + '} | {1,-' + $asWidth + '} | {2}') -f $row.name, $row.installed_as, $row.path
    if ($row.installed_as -eq '[missing]') {
        Write-Host $line -ForegroundColor Yellow
    } else {
        Write-Host $line
    }
}
Write-Host ""

$missingCount = ($sorted | Where-Object { $_.installed_as -eq '[missing]' }).Count
$totalCount   = $sorted.Count

Write-Host "$totalCount skill(s) found.  $missingCount missing `installed_as:` (shown in yellow)."
if ($missingCount -gt 0) {
    Write-Host "To fix: add `installed_as: <bare-name>` or `installed_as: roslyn-mcp:<bare-name>` to each SKILL.md frontmatter."
}
else {
    Write-Host "All skill frontmatter entries satisfy the installed_as contract."
}
Write-Host ""
