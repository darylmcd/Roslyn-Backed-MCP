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

# ---------------------------------------------------------------------------
# Prefix-agnostic assertion (additive pass — the banned-pattern loop above is
# unchanged so the in-process echo test's literal substrings stay green).
#
# The server's MCP tool prefix is CLIENT-ASSIGNED: the same tool surfaces as
# `mcp__roslyn__server_info` on a self-hosted entry and as
# `mcp__plugin_roslyn-mcp_roslyn__server_info` on the marketplace install. A
# shipped skill must therefore never instruct the agent to CALL or VERIFY a
# bare prefixed literal. Citing those prefixes as illustrative examples is
# fine — `exemptSpans` neutralizes any line carrying the disclaimer.
# ---------------------------------------------------------------------------
$prefixPolicy = $policy.prefixAgnostic
if ($null -eq $prefixPolicy) {
    throw "Genericity policy is missing prefixAgnostic: $policyPath"
}
$imperativePatterns = @($prefixPolicy.imperativePatterns)
$exemptSpans = @($prefixPolicy.exemptSpans)
$residualUnsweptAllowlist = @($prefixPolicy.residualUnsweptAllowlist)
$canonicalBlocks = @($policy.canonicalPrecheckBlocks)
if ($imperativePatterns.Count -eq 0 -or $exemptSpans.Count -eq 0 -or $canonicalBlocks.Count -eq 0) {
    throw "Genericity policy is missing prefixAgnostic.imperativePatterns, prefixAgnostic.exemptSpans, or canonicalPrecheckBlocks: $policyPath"
}

# The allowlist is a SHRINKING amnesty for files not yet swept onto the
# canonical note. Assert every entry still exists so it cannot rot into a
# permanent exemption pointing at deleted paths.
$allowedSet = @{}
foreach ($allowed in $residualUnsweptAllowlist) {
    $allowedSet[$allowed.ToLowerInvariant()] = $true
    if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $allowed) -PathType Leaf)) {
        $issues.Add("eng/banned-skill-markers.json: residualUnsweptAllowlist entry '$allowed' no longer exists -- drop the stale entry.")
    }
}

function Get-RelativeSkillPath {
    param([string]$FullName, [string]$Root)
    return ($FullName.Substring($Root.Length + 1) -replace '\\', '/')
}

function Assert-PrefixAgnostic {
    foreach ($file in $skillFiles) {
        $rel = Get-RelativeSkillPath -FullName $file.FullName -Root $RepoRoot
        if ($allowedSet.ContainsKey($rel.ToLowerInvariant())) { continue }
        $lines = @(Get-Content -LiteralPath $file.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $exempt = $false
            foreach ($span in $exemptSpans) {
                if ($line -match $span) { $exempt = $true; break }
            }
            if ($exempt) { continue }
            foreach ($pattern in $imperativePatterns) {
                if ($line -match $pattern) {
                    $issues.Add("${rel}:$($i + 1): bare roslyn prefix inside a tool-surface imperative -> $line")
                    break
                }
            }
        }
    }
}

function Assert-CanonicalBlockIdentity {
    foreach ($file in $skillFiles) {
        $rel = Get-RelativeSkillPath -FullName $file.FullName -Root $RepoRoot
        if ($allowedSet.ContainsKey($rel.ToLowerInvariant())) { continue }
        $lines = @(Get-Content -LiteralPath $file.FullName)
        foreach ($block in $canonicalBlocks) {
            $canonical = @($block.text)
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -notmatch $block.anchorPattern) { continue }

                $extracted = New-Object System.Collections.Generic.List[string]
                if ([string]::IsNullOrEmpty($block.terminatorPattern)) {
                    $extracted.Add($lines[$i])
                } else {
                    for ($j = $i; $j -lt $lines.Count; $j++) {
                        if ($j -gt $i -and $lines[$j] -match $block.terminatorPattern) { break }
                        $extracted.Add($lines[$j])
                    }
                }

                # The canonical text must be the byte-identical LEADING slice of the
                # block. Per-skill trailing prose AFTER it is allowed (e.g. the
                # workspace-health skill's "a failing precheck is itself the answer").
                if ($extracted.Count -lt $canonical.Count) {
                    $issues.Add("${rel}:$($i + 1): canonical block '$($block.id)' is truncated ($($extracted.Count) lines, expected at least $($canonical.Count)).")
                    continue
                }
                for ($k = 0; $k -lt $canonical.Count; $k++) {
                    if (-not [string]::Equals($extracted[$k], $canonical[$k], [System.StringComparison]::Ordinal)) {
                        $issues.Add("${rel}:$($i + 1 + $k): canonical block '$($block.id)' drifted from eng/banned-skill-markers.json -> $($extracted[$k])")
                        break
                    }
                }
            }
        }
    }
}

Assert-PrefixAgnostic
Assert-CanonicalBlockIdentity

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
