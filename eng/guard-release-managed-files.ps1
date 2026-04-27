# PreToolUse hook guard for release-managed files.
#
# Reads Claude Code's hook-input JSON from stdin. If the target path is in the
# release-managed set (the 5 version-source files plus 4 release-critical guard
# scripts), block the edit unless an override sentinel is present.
#
# Override sentinel: a file at $env:CLAUDE_PROJECT_DIR/.release-managed-edit-allowed
# whose mtime is within RELEASE_SENTINEL_TTL_SECONDS (default 1800s / 30 min).
# Skills like /bump, /release-cut, and /ship create this sentinel before mutating
# release-managed files and remove it at end of flow.
#
# Replaces the prior prompt-based hook whose "look for phrase in reasoning text"
# override was unreliable -- prompt-based PreToolUse hooks do not consistently
# receive assistant prose, only the tool input.
#
# Hook config: hooks/hooks.json -> PreToolUse -> Edit|Write|MultiEdit.
# Exit codes:
#   0 -- allow (path not in set, OR sentinel valid)
#   2 -- block (path in set, no valid sentinel) -- prints denial to stderr
# Other exits indicate script failure; hook treats as allow.

param()

$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

try {
    $payload = $raw | ConvertFrom-Json
} catch {
    exit 0
}

$filePath = $payload.tool_input.file_path
if (-not $filePath) { exit 0 }

$repoRoot = $env:CLAUDE_PROJECT_DIR
if (-not $repoRoot) { $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }

$normalized = $filePath -replace '\\', '/'
$repoRootNorm = ($repoRoot -replace '\\', '/').TrimEnd('/')
if ($normalized.StartsWith($repoRootNorm + '/', [StringComparison]::OrdinalIgnoreCase)) {
    $relative = $normalized.Substring($repoRootNorm.Length + 1)
} else {
    $relative = $normalized
}
$relativeLower = $relative.ToLowerInvariant()
$basenameLower = (Split-Path -Leaf $relativeLower)

# False-positive avoidance: anything under tests/ or fixtures/ is not the repo-root version source.
if ($relativeLower -match '(^|/)(tests|fixtures)/') { exit 0 }

$managedExact = @(
    'directory.build.props',
    'manifest.json',
    '.claude-plugin/plugin.json',
    '.claude-plugin/marketplace.json',
    'changelog.md',
    'eng/verify-version-drift.ps1',
    'hooks/hooks.json',
    'eng/verify-skills-are-generic.ps1'
)

$isManaged = $false
$matchedAs = $null
if ($managedExact -contains $relativeLower) {
    $isManaged = $true
    $matchedAs = $relativeLower
} elseif ($basenameLower -eq 'bannedsymbols.txt') {
    $isManaged = $true
    $matchedAs = $relative
}

if (-not $isManaged) { exit 0 }

$sentinel = Join-Path $repoRoot '.release-managed-edit-allowed'
$ttlSeconds = 1800
if ($env:RELEASE_SENTINEL_TTL_SECONDS) {
    $parsed = 0
    if ([int]::TryParse($env:RELEASE_SENTINEL_TTL_SECONDS, [ref]$parsed) -and $parsed -gt 0) {
        $ttlSeconds = $parsed
    }
}

$staleMessage = ''
if (Test-Path $sentinel) {
    $age = (Get-Date) - (Get-Item $sentinel).LastWriteTime
    if ($age.TotalSeconds -le $ttlSeconds) { exit 0 }
    $staleMessage = " (sentinel exists but is stale: $([int]$age.TotalSeconds)s > ${ttlSeconds}s TTL -- re-touch it or re-run the skill)"
}

$msg = "Blocked: ``$matchedAs`` is a release-managed file. Use the ``/bump``, ``/release-cut``, or ``/ship`` skill (these create the override sentinel automatically), or manually run:`n`n    New-Item -ItemType File -Force `"$repoRoot/.release-managed-edit-allowed`" | Out-Null`n`nto create the sentinel for the next ${ttlSeconds}s.$staleMessage See ``ai_docs/workflow.md`` Release-managed file guard."

[Console]::Error.WriteLine($msg)
exit 2
