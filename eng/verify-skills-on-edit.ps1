# PostToolUse hook dispatcher.
# Reads Claude Code's hook-input JSON from stdin and, when the edited path is a
# shipped skill (skills/<name>/SKILL.md or any *.md under skills/), invokes the
# generic-skills linter so violations surface in the same turn as the edit
# instead of waiting for CI.
#
# Hook config: hooks/hooks.json -> PostToolUse -> Edit|Write|MultiEdit.
# Verifier:    eng/verify-skills-are-generic.ps1.
#
# Silent on non-skill paths and on clean skill edits (the verifier prints a
# one-line success message; that is fine — the user sees confirmation that the
# guard ran). Exits with the verifier's exit code so Claude surfaces failures.

param()

$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

try {
    $payload = $raw | ConvertFrom-Json
} catch {
    # Malformed payload — do not block the edit.
    exit 0
}

$filePath = $payload.tool_input.file_path
if (-not $filePath) { exit 0 }

# Normalize Windows backslashes to forward slashes for the regex.
$normalized = $filePath -replace '\\', '/'
if ($normalized -notmatch '/skills/[^/]+/.*\.md$') { exit 0 }

$repoRoot = $env:CLAUDE_PROJECT_DIR
if (-not $repoRoot) { $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }

$verifier = Join-Path $repoRoot 'eng/verify-skills-are-generic.ps1'
if (-not (Test-Path $verifier)) { exit 0 }

& pwsh -NoProfile -NonInteractive -File $verifier -RepoRoot $repoRoot
exit $LASTEXITCODE
