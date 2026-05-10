<#
.SYNOPSIS
    Seed the GitHub Issue labels consumed by the surface-test issue template and the shared
    finding renderer.

.DESCRIPTION
    Idempotently creates `area:*` and `severity:*` labels at `darylmcd/Roslyn-Backed-MCP`
    via `gh label create --force`. The `--force` flag overwrites color/description on re-run
    so the script is safe to re-invoke. The label sets here MUST stay in lockstep with:

      - `ai_docs/items/backlog-d-fragment-schema.md` (the canonical fragment-envelope enum)
      - `.github/ISSUE_TEMPLATE/mcp-server-surface-test-finding.yml` (the public-form options)
      - `skills/mcp-server-surface-test/lib/render-finding.ps1` (the renderer that emits
        `--label area:<area>` and `--label severity:<severity>` arguments)

    Drift between any of these three surfaces is caught by
    `tests/RoslynMcp.Tests/Skills/IssueTemplateAndLabelSeedTests.cs`.

    Note that the seed list intentionally OMITS the labels the template/renderer refuse to file
    publicly: `severity:P0` and `area:security`. Both are refused upstream by the renderer's
    `Test-FindingShouldRefusePublicFile` predicate; emitting them as labels would invite a
    contributor to file a P0/security Issue manually, which is the exact failure mode SECURITY.md
    forbids. The fragment schema's enum still includes them — they are valid in `backlog.d/`
    fragments and are how the renderer detects refusal — but they never reach a public Issue.

.PARAMETER Repo
    GitHub `owner/repo` slug to seed. Defaults to `darylmcd/Roslyn-Backed-MCP`.

.PARAMETER DryRun
    When set, prints the `gh label create` commands without invoking them.

.NOTES
    Run-once, idempotent. Designed to be invoked manually after cloning the repo for the first
    time, or whenever the lockstep test surfaces a drift the maintainer wants to repair.
    Requires `gh` on PATH and `gh auth status` reporting authenticated for the target repo.
#>
[CmdletBinding()]
param(
    [string]$Repo = 'darylmcd/Roslyn-Backed-MCP',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Lockstep enums — duplicated here so the script is self-contained, but tested for drift.
$AreaLabels = @(
    @{ name = 'area:tools';        color = '0366d6'; description = 'MCP tool surface (read-side or write-side)' }
    @{ name = 'area:resources';    color = '5319e7'; description = 'MCP resource surface (URI templates, payloads)' }
    @{ name = 'area:prompts';      color = '8a63d2'; description = 'MCP prompt surface (prompts/list + prompts/get)' }
    @{ name = 'area:skills';       color = '0e8a16'; description = 'Shipped or maintainer-only skills (.claude/skills/ or skills/)' }
    @{ name = 'area:concurrency';  color = 'b60205'; description = 'Per-workspace lock contract, parallel reads, lifecycle stress' }
    @{ name = 'area:perf';         color = 'fbca04'; description = 'Wall-clock budgets (single-symbol reads, solution scans, writers)' }
    @{ name = 'area:docs';         color = '0075ca'; description = 'Documentation, README, AGENTS.md, ai_docs/' }
)

$SeverityLabels = @(
    @{ name = 'severity:P1'; color = 'd93f0b'; description = 'High — blocks a workflow or produces wrong results' }
    @{ name = 'severity:P2'; color = 'fbca04'; description = 'Medium — degrades a workflow but a workaround exists' }
    @{ name = 'severity:P3'; color = 'cccccc'; description = 'Low — UX polish or output-enrichment opportunity' }
)

$AllLabels = $AreaLabels + $SeverityLabels

if (-not $DryRun) {
    # Verify gh is on PATH and authenticated before touching anything.
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $ghCmd) {
        throw "gh is not on PATH. Install GitHub CLI (https://cli.github.com/) and re-run."
    }
    $authStatus = & gh auth status 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh is not authenticated. Run 'gh auth login' and re-run. Output: $authStatus"
    }
}

Write-Output "Seeding $($AllLabels.Count) labels at '$Repo' (dryRun=$([bool]$DryRun))..."

foreach ($label in $AllLabels) {
    $args = @(
        'label', 'create', $label.name,
        '--repo', $Repo,
        '--color', $label.color,
        '--description', $label.description,
        '--force'
    )

    if ($DryRun) {
        Write-Output "DRY-RUN: gh $($args -join ' ')"
        continue
    }

    & gh @args
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to create label '$($label.name)' on '$Repo' — see gh stderr above."
    }
    else {
        Write-Output "OK: $($label.name)"
    }
}

Write-Output ""
Write-Output "Done. Labels are in lockstep with:"
Write-Output "  - ai_docs/items/backlog-d-fragment-schema.md"
Write-Output "  - .github/ISSUE_TEMPLATE/mcp-server-surface-test-finding.yml"
Write-Output "  - skills/mcp-server-surface-test/lib/render-finding.ps1"
Write-Output ""
Write-Output "P0 / security findings deliberately have NO public label — they are refused by"
Write-Output "the renderer and routed via SECURITY.md (private GitHub security advisories)."
