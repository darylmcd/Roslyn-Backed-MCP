#Requires -Version 7.0

[CmdletBinding()]
param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '../..'),
    [string] $VerifierPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-PublicationBoundary {
    param([Parameter(Mandatory)][string] $Command)

    $gitPattern = '(?im)(?:^|[\r\n;&|])\s*&?\s*git(?:\.exe)?(?:\s+-C\s+(?:"[^"]+"|''[^'']+''|\S+))?\s+(?:commit|push)(?:\s|$)'
    $githubPattern = '(?im)(?:^|[\r\n;&|])\s*&?\s*gh(?:\.exe)?\s+pr\s+(?:create|merge)(?:\s|$)'
    $shipPattern = '(?im)(?:^|[\r\n;&|])\s*&?\s*(?:gbash|bash)(?:\.exe)?\b[^\r\n;&|]*\bship-(?:preflight|staged-guard)\.sh\b'

    return $Command -match $gitPattern -or
        $Command -match $githubPattern -or
        $Command -match $shipPattern
}

function Write-Denial {
    param([Parameter(Mandatory)][string] $Reason)

    [ordered]@{
        hookSpecificOutput = [ordered]@{
            hookEventName = 'PreToolUse'
            permissionDecision = 'deny'
            permissionDecisionReason = $Reason
        }
    } | ConvertTo-Json -Depth 4 -Compress | Write-Output
}

try {
    $rawInput = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($rawInput)) {
        throw 'The hook received no JSON input.'
    }

    $hookInput = $rawInput | ConvertFrom-Json -ErrorAction Stop
    if ($hookInput.hook_event_name -ne 'PreToolUse') {
        return
    }

    $isShellPublication = $false
    if ($hookInput.tool_name -in @('Bash', 'shell_command')) {
        if ($hookInput.tool_input.PSObject.Properties.Name -notcontains 'command') {
            throw "The PreToolUse $($hookInput.tool_name) payload did not contain tool_input.command."
        }

        $command = [string] $hookInput.tool_input.command
        $isShellPublication = -not [string]::IsNullOrWhiteSpace($command) -and
            (Test-PublicationBoundary -Command $command)
    }

    $isGithubPublication = $hookInput.tool_name -match
        '^mcp__codex_apps__github_(?:create|merge)_pull_request$'
    if ($isGithubPublication -and
        $hookInput.tool_input.PSObject.Properties.Name -contains 'repository_full_name') {
        $targetRepoName = ([string] $hookInput.tool_input.repository_full_name).Split('/')[-1]
        $expectedRepoName = Split-Path -Leaf ([System.IO.Path]::GetFullPath($RepoRoot))
        $isGithubPublication = $targetRepoName -eq $expectedRepoName
    }

    if (-not $isShellPublication -and -not $isGithubPublication) {
        return
    }

    $resolvedRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $resolvedVerifier = if ([string]::IsNullOrWhiteSpace($VerifierPath)) {
        Join-Path $resolvedRoot 'eng/verify-changelog-fragments.ps1'
    }
    else {
        [System.IO.Path]::GetFullPath($VerifierPath)
    }

    if (-not (Test-Path -LiteralPath $resolvedVerifier -PathType Leaf)) {
        Write-Denial -Reason (
            "Roslyn-Backed-MCP publication is blocked because the changelog verifier is missing: $resolvedVerifier")
        return
    }

    $verificationOutput = @(& pwsh -NoProfile -File $resolvedVerifier -RepoRoot $resolvedRoot 2>&1)
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $details = ($verificationOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($details.Length -gt 1600) {
        $details = $details.Substring(0, 1600) + '...'
    }

    $instructions = @'
Inspect the complete change set and create or repair one semantic `changelog.d/<row-id>.md` fragment for the shipped work. Use this shape:
---
category: <Fixed|Changed|Changed — BREAKING|Added|Maintenance>
---

- **<same category>:** <consumer-facing summary>.

Internal `ai_docs/**`-only work is exempt. For an assembled version bump, repair the consumed-fragment, CHANGELOG, and six-version-file state instead of recreating fragments. Run `pwsh -NoProfile -File ./eng/verify-changelog-fragments.ps1`, then retry the publication command.
'@.Trim()
    $reason = "Roslyn-Backed-MCP publication is blocked because changelog verification failed:$([Environment]::NewLine)$details$([Environment]::NewLine)$([Environment]::NewLine)$instructions"
    Write-Denial -Reason $reason
}
catch {
    [Console]::Error.WriteLine(
        "Roslyn-Backed-MCP changelog hook failed closed: $($_.Exception.GetType().Name)")
    exit 2
}
