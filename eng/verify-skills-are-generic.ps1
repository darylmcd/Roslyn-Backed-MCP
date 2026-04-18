param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

# Repo-specific markers that must NEVER appear in shipped skills (./skills/).
# Repo-only maintainer skills belong in .claude/skills/ — Claude Code auto-
# discovers them locally and they are not bundled by plugin.json's "skills" glob.
# Each pattern is a .NET regex. URLs are stripped before the scan so
# https://github.com/<owner>/Roslyn-Backed-MCP/... links remain allowed.
$bannedPatterns = @(
    'state\.json',
    'backlog-sweep',
    'schemaVersion',
    '\bai_docs/',
    '\bbacklog\.md\b',
    '\beng/',
    'just verify-',
    'Directory\.Build\.props',
    'BannedSymbols\.txt'
)

$skillsDir = Join-Path $RepoRoot 'skills'
if (-not (Test-Path $skillsDir)) {
    Write-Host "No shipped skills/ directory — nothing to check."
    exit 0
}

$skillFiles = Get-ChildItem -Path $skillsDir -Recurse -File -Filter 'SKILL.md'
$issues = New-Object System.Collections.Generic.List[string]

foreach ($file in $skillFiles) {
    $lines = Get-Content -LiteralPath $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # Strip URLs before scanning — a GitHub link to this repo's docs
        # legitimately contains `ai_docs/` and is fine for installers to click.
        $stripped = [regex]::Replace($line, 'https?://[^\s)]+', '')
        foreach ($pattern in $bannedPatterns) {
            if ($stripped -match $pattern) {
                $rel = $file.FullName.Substring($RepoRoot.Length + 1) -replace '\\', '/'
                $issues.Add("${rel}:$($i + 1): banned pattern '$pattern' -> $line")
                break
            }
        }
    }
}

if ($issues.Count -gt 0) {
    Write-Host ""
    Write-Host "Shipped skills under ./skills/ must be generic (not coupled to this repo)." -ForegroundColor Red
    Write-Host "Repo-only skills belong in .claude/skills/ (auto-discovered locally, not shipped)." -ForegroundColor Red
    Write-Host ""
    foreach ($issue in ($issues | Sort-Object -Unique)) {
        Write-Host $issue -ForegroundColor Red
    }
    Write-Host ""
    exit 1
}

Write-Host "Shipped skills under ./skills/ are generic ($($skillFiles.Count) SKILL.md checked)."
