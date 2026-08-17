param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

# Repo-specific markers that must NEVER appear in shipped skills (./skills/).
# Repo-only maintainer skills belong in .claude/skills/ — Claude Code auto-
# discovers them locally and they are not bundled by plugin.json's "skills" glob.
# Each pattern is a .NET regex. URLs are stripped before the scan so
# https://github.com/<owner>/Roslyn-Backed-MCP/... links remain allowed, and
# placeholder-rooted paths (`<audited-repo-root>/...`, `<Roslyn-Backed-MCP-root>/...`)
# are stripped too — an explicitly-rooted path is a deliberate cross-repo pointer,
# not repo coupling.
#
# NOTE: a bare `schemaVersion` pattern used to live here as a proxy for the sweep
# `state.json` shape. `state\.json` below catches that coupling directly, while the
# bare word also fired on a shipped skill's OWN artifact schema — so the proxy was
# retired. Do not re-add it without fenced-code-block awareness in the scanner.
$policyPath = Join-Path $PSScriptRoot 'banned-skill-markers.json'
if (-not (Test-Path -LiteralPath $policyPath -PathType Leaf)) {
    throw "Genericity policy not found: $policyPath"
}

$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$bannedPatterns = @($policy.bannedPatterns)
$stripPatterns = @($policy.stripPatterns)
if ([string]::IsNullOrWhiteSpace($policy.filePattern) -or
    $bannedPatterns.Count -eq 0 -or
    $stripPatterns.Count -eq 0) {
    throw "Genericity policy is missing filePattern, bannedPatterns, or stripPatterns: $policyPath"
}

$skillsDir = Join-Path $RepoRoot 'skills'
if (-not (Test-Path $skillsDir)) {
    Write-Host "No shipped skills/ directory — nothing to check."
    exit 0
}

# Scan EVERY shipped markdown file, not just SKILL.md — prompt bodies and READMEs
# ship to installers verbatim and leak repo coupling just as readily.
$skillFiles = @(Get-ChildItem -Path $skillsDir -Recurse -File -Filter $policy.filePattern)
$issues = New-Object System.Collections.Generic.List[string]

foreach ($file in $skillFiles) {
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # Strip URLs before scanning — a GitHub link to this repo's docs
        # legitimately contains `ai_docs/` and is fine for installers to click.
        $stripped = $line
        foreach ($stripPattern in $stripPatterns) {
            $stripped = [regex]::Replace($stripped, $stripPattern, '')
        }
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

Write-Host "Shipped skills under ./skills/ are generic ($($skillFiles.Count) .md files checked)."
