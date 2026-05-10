<#
.SYNOPSIS
    Shared rendering helper for `/mcp-server-surface-test` Phase 19 finding emission and
    `/backlog-intake --publish` Issue filing. Single source of truth for the finding-envelope
    contract so both paths emit byte-identical bodies.

.DESCRIPTION
    Exposes three renderers, one repo-id helper, and one refusal predicate:

      Get-FindingRepoId
          Derive a kebab-case `source_repo` slug from a target repo path. Parses
          `git remote get-url origin` -> `owner/repo`; falls back to the lowercased
          repo dir basename when no remote is set.

      Test-FindingShouldRefusePublicFile
          Returns $true when severity == "P0" OR area == "security". Both auto-file
          paths short-circuit on this — public Issue filing is never an option for
          pre-disclosure-relevant findings.

      Render-FindingBody
          Renders the inner Markdown body block — the same text used by both stdout
          print and `gh issue create --body-file`. Field ordering is fixed and anchors
          are sorted, so two runs with the same logical input produce byte-identical
          output (renderer determinism).

      Render-FindingFragment
          Renders a `backlog.d/<id>.md` fragment — YAML frontmatter (id, source_audit,
          source_repo, severity, area, server_version, anchors) + the same body block
          Render-FindingBody emits. Used by the maintainer overlay's Phase 19.

      Render-FindingIssue
          Renders a `[pscustomobject]` carrying the Issue title, labels, and body. The
          caller writes the body to a tempfile and invokes `gh issue create
          --title <title> --label area:<area> --label severity:<severity> --body-file <tempfile>`.

.PARAMETER Finding
    A hashtable / pscustomobject with the keys: `id`, `severity`, `area`, `source_repo`,
    `source_audit`, `server_version`, `anchors` (array of `path:line` strings),
    `finding`, `repro`, `proposed_fix`. Missing fields are treated as empty strings.

.NOTES
    The file is intentionally script-only (no module manifest). Consumers dot-source it:

        . "${env:CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/lib/render-finding.ps1"
        $body = Render-FindingBody -Finding $finding

    Tested in `tests/RoslynMcp.Tests/Skills/RenderFindingScriptTests.cs` via in-process
    pwsh invocation; no live `gh` call ever runs in CI.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# ---------- Internal helpers ----------

function Get-FindingFieldString {
    param(
        [Parameter(Mandatory)] $Finding,
        [Parameter(Mandatory)] [string]$Name
    )
    if ($Finding -is [System.Collections.IDictionary]) {
        if ($Finding.Contains($Name)) { return [string]$Finding[$Name] }
        return ''
    }
    $member = $Finding.PSObject.Properties[$Name]
    if ($null -ne $member) { return [string]$member.Value }
    return ''
}

function Get-FindingFieldArray {
    param(
        [Parameter(Mandatory)] $Finding,
        [Parameter(Mandatory)] [string]$Name
    )
    $raw = $null
    if ($Finding -is [System.Collections.IDictionary]) {
        if ($Finding.Contains($Name)) { $raw = $Finding[$Name] }
    }
    else {
        $member = $Finding.PSObject.Properties[$Name]
        if ($null -ne $member) { $raw = $member.Value }
    }
    if ($null -eq $raw) { return @() }
    if ($raw -is [string]) { return @($raw) }
    return @($raw)
}

# ---------- Public API ----------

function Get-FindingRepoId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$RepoRoot
    )

    if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
        throw "RepoRoot '$RepoRoot' does not exist or is not a directory."
    }

    $remote = $null
    try {
        $remote = (& git -C $RepoRoot remote get-url origin 2>$null) | Select-Object -First 1
    }
    catch {
        $remote = $null
    }

    if (-not [string]::IsNullOrWhiteSpace($remote)) {
        # Match owner/repo from common remote URL shapes:
        #   https://github.com/owner/repo.git
        #   git@github.com:owner/repo.git
        #   ssh://git@github.com/owner/repo
        $m = [regex]::Match($remote, '[:/](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$')
        if ($m.Success) {
            $candidate = $m.Groups['repo'].Value
            return ConvertTo-FindingSlug $candidate
        }
    }

    # No usable remote — fall back to the resolved directory basename.
    $basename = (Split-Path -Leaf (Resolve-Path -LiteralPath $RepoRoot).ProviderPath)
    return ConvertTo-FindingSlug $basename
}

function ConvertTo-FindingSlug {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Value
    )
    $s = $Value.ToLowerInvariant()
    $s = $s -replace '[^a-z0-9]+', '-'
    $s = $s.Trim('-')
    if ([string]::IsNullOrWhiteSpace($s)) {
        throw "Cannot derive a kebab-case slug from input '$Value'."
    }
    return $s
}

function Test-FindingShouldRefusePublicFile {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)] $Finding
    )
    $severity = (Get-FindingFieldString -Finding $Finding -Name 'severity').Trim()
    $area = (Get-FindingFieldString -Finding $Finding -Name 'area').Trim().ToLowerInvariant()
    return ($severity -eq 'P0') -or ($area -eq 'security')
}

function Render-FindingBody {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] $Finding
    )

    $id             = Get-FindingFieldString -Finding $Finding -Name 'id'
    $sourceRepo     = Get-FindingFieldString -Finding $Finding -Name 'source_repo'
    $severity       = Get-FindingFieldString -Finding $Finding -Name 'severity'
    $area           = Get-FindingFieldString -Finding $Finding -Name 'area'
    $serverVersion  = Get-FindingFieldString -Finding $Finding -Name 'server_version'
    $findingText    = Get-FindingFieldString -Finding $Finding -Name 'finding'
    $repro          = Get-FindingFieldString -Finding $Finding -Name 'repro'
    $proposedFix    = Get-FindingFieldString -Finding $Finding -Name 'proposed_fix'

    # Anchors are sorted alphabetically for renderer determinism — two logically equivalent
    # inputs (different list order) emit byte-identical bodies.
    $anchors = @(Get-FindingFieldArray -Finding $Finding -Name 'anchors' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Culture '')

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("- id: $id")
    [void]$sb.AppendLine("- source-repo: $sourceRepo")
    [void]$sb.AppendLine("- severity: $severity")
    [void]$sb.AppendLine("- area: $area")
    [void]$sb.AppendLine("- server-version: $serverVersion")
    [void]$sb.AppendLine("- anchors:")
    if ($anchors.Count -eq 0) {
        [void]$sb.AppendLine("  - (none)")
    }
    else {
        foreach ($a in $anchors) {
            [void]$sb.AppendLine("  - $a")
        }
    }
    [void]$sb.AppendLine("- finding: $findingText")
    [void]$sb.AppendLine("- repro: $repro")
    [void]$sb.AppendLine("- proposed-fix: $proposedFix")
    # Trim the trailing newline so the body is suitable for `--body-file` and for inclusion in
    # a fragment without producing a double blank line.
    return $sb.ToString().TrimEnd("`r", "`n")
}

function Render-FindingFragment {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] $Finding
    )

    $id            = Get-FindingFieldString -Finding $Finding -Name 'id'
    $sourceAudit   = Get-FindingFieldString -Finding $Finding -Name 'source_audit'
    $sourceRepo    = Get-FindingFieldString -Finding $Finding -Name 'source_repo'
    $severity      = Get-FindingFieldString -Finding $Finding -Name 'severity'
    $area          = Get-FindingFieldString -Finding $Finding -Name 'area'
    $serverVersion = Get-FindingFieldString -Finding $Finding -Name 'server_version'
    $anchors       = @(Get-FindingFieldArray -Finding $Finding -Name 'anchors' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Culture '')

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("id: $id")
    [void]$sb.AppendLine("source_audit: $sourceAudit")
    [void]$sb.AppendLine("source_repo: $sourceRepo")
    [void]$sb.AppendLine("severity: $severity")
    [void]$sb.AppendLine("area: $area")
    [void]$sb.AppendLine("server_version: $serverVersion")
    [void]$sb.AppendLine("anchors:")
    if ($anchors.Count -eq 0) {
        [void]$sb.AppendLine("  - (none)")
    }
    else {
        foreach ($a in $anchors) {
            [void]$sb.AppendLine("  - $a")
        }
    }
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine((Render-FindingBody -Finding $Finding))
    return $sb.ToString().TrimEnd("`r", "`n")
}

function Render-FindingIssue {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] $Finding
    )

    $id       = Get-FindingFieldString -Finding $Finding -Name 'id'
    $severity = Get-FindingFieldString -Finding $Finding -Name 'severity'
    $area     = Get-FindingFieldString -Finding $Finding -Name 'area'

    $title = $id
    $labels = @("area:$area", "severity:$severity")

    $bodyHeader = ''
    if (Test-FindingShouldRefusePublicFile -Finding $Finding) {
        # The refusal banner is rendered inside the body so a misconfigured caller that ignores
        # the predicate still ships the warning to whatever destination it picks. Defense in depth:
        # the caller MUST also short-circuit before invoking `gh issue create`.
        $bodyHeader = @(
            '**SECURITY / P0 finding — DO NOT FILE PUBLICLY.**',
            'Escalate via GitHub security advisories: https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new',
            ''
        ) -join [Environment]::NewLine
    }

    $body = $bodyHeader + (Render-FindingBody -Finding $Finding)

    return [pscustomobject]@{
        title          = $title
        labels         = $labels
        body           = $body
        refusedPublic  = (Test-FindingShouldRefusePublicFile -Finding $Finding)
    }
}
