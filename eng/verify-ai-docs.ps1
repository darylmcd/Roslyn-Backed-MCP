param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$stalePatterns = @(
    'agent_session_instructions.md',
    'editor_notes.md',
    'open_and_deferred_items.md',
    'ai_docs/tmp',
    'ai_docs/quickstart.md'
)

# Enumerate only files git considers part of the project (tracked + untracked-but-not-ignored).
# Skips .gitignore'd paths (.claude/ user state, artifacts/, bin/, obj/, etc.) so doc checks
# only validate files that will actually ship.
$gitFilesRaw = & git -C $RepoRoot ls-files --cached --others --exclude-standard 2>$null
if ($LASTEXITCODE -ne 0 -or -not $gitFilesRaw) {
    Write-Error "Unable to enumerate files via 'git ls-files'. Is this a git checkout?"
    exit 1
}

$allFiles = foreach ($relative in $gitFilesRaw) {
    $fullPath = Join-Path $RepoRoot $relative
    # Test-Path + Get-Item is not atomic and trips $ErrorActionPreference='Stop'
    # when Get-Item sees a stale read — e.g. a tracked file that the CI runner's
    # checkout hasn't written yet. Use a BCL existence probe + FileInfo, both
    # synchronous and immune to the PowerShell pipeline's resume semantics.
    if ([System.IO.File]::Exists($fullPath)) {
        [System.IO.FileInfo]::new($fullPath)
    }
}

$staleSearchExtensions = @('.md', '.ps1', '.yml', '.yaml', '.json')
$contentFiles = $allFiles | Where-Object {
    $staleSearchExtensions -contains $_.Extension -and $_.FullName -ne $PSCommandPath
}
$markdownFiles = $allFiles | Where-Object { $_.Extension -eq '.md' }

$issues = New-Object System.Collections.Generic.List[string]

foreach ($pattern in $stalePatterns) {
    $matches = Select-String -Path $contentFiles.FullName -Pattern $pattern -SimpleMatch
    foreach ($match in $matches) {
        $issues.Add("Stale reference: $($match.Path):$($match.LineNumber) -> $pattern")
    }
}

foreach ($file in $markdownFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $matches = [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')

    foreach ($match in $matches) {
        $target = $match.Groups[1].Value.Trim()

        if ($target -match '^(https?:|mailto:|#)') {
            continue
        }

        # Skip template placeholder links like ([#{prNumber}]({prUrl})) — doc authors
        # use these in example blocks; they are not resolvable filesystem paths.
        if ($target -match '\{[^}]+\}') {
            continue
        }

        $pathPart = $target.Split('#')[0].Split('?')[0] -replace '%20', ' '
        if ([string]::IsNullOrWhiteSpace($pathPart)) {
            continue
        }

        if ($pathPart -match '^[a-zA-Z]:\\') {
            if (-not (Test-Path -LiteralPath $pathPart)) {
                $issues.Add("Broken absolute link: $($file.FullName) -> $target")
            }

            continue
        }

        $resolved = Join-Path -Path $file.DirectoryName -ChildPath $pathPart
        if (-not (Test-Path -LiteralPath $resolved)) {
            $issues.Add("Broken relative link: $($file.FullName) -> $target")
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "AI docs validation passed."

# Shipped-skill generality check runs as part of doc-side validation (not
# release-side), so the check still gates a PR even when CI's detect-docs-only
# step skips verify-release.ps1. Both `./skills/**/SKILL.md` markdown bodies and
# any future `./skills/**/*.md` additions are validated by verify-skills-are-
# generic.ps1. verify-release.ps1 invokes this script too, so local full-release
# runs continue to catch the same violations.
& (Join-Path $PSScriptRoot 'verify-skills-are-generic.ps1') -RepoRoot $RepoRoot