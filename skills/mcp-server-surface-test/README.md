# /mcp-server-surface-test

Consumer-facing audit of the Roslyn MCP server's live surface against your loaded C# repo. Run when you want to verify that the server's tools, resources, and prompts behave as documented against your own codebase — and optionally share findings back upstream.

## Tiers

| Tier | Flag | Runtime | What it does |
|---|---|---|---|
| **Quick** | `--quick` | ≤15 min | Read-only smoke pass. No apply-mode mutations, no disposable worktree, no test runs, no network calls. Produces an audit report only. |
| **Full** | *(default)* or `--full` | 90–180 min | Comprehensive sweep including disposable-worktree apply round-trips, build/test validation, and the experimental→stable promotion scorecard. |

## What it does

1. Verifies the Roslyn MCP server is reachable (`mcp__roslyn__server_info` returns `connection.state: ready`). Halts otherwise — there is no non-MCP fallback.
2. Loads your C# workspace (`.sln` / `.slnx` / `.csproj`).
3. Runs through 10–17 audit phases (depending on tier) that exercise every tool, resource, and prompt the live catalog reports. Records `_meta.elapsedMs`, schema-vs-behaviour drift, error-message quality, and parameter-path coverage.
4. **Full tier only:** drives preview→apply→revert round-trips inside a **disposable worktree the skill creates and tears down at run end**. Your repo's `main` branch is never mutated; no commit ever lands in your repo's history; no PR is opened.
5. Writes a structured audit report to `<your-repo>/audit-reports/<timestamp>_<repo-id>_mcp-server-surface-test.md`.
6. Renders each actionable finding through a shared envelope and emits per `--auto-file`.

## Privacy and safety

- **What stays local by default.** All audit evidence — the prose report and the promotion scorecard JSON (full tier only) — is written to your repo's `audit-reports/` directory. Nothing leaves your machine unless you pass `--auto-file`.
- **What `--auto-file` sends.** The skill calls `gh issue create` against `https://github.com/darylmcd/Roslyn-Backed-MCP` with the rendered finding envelope (id, severity, area, anchors, finding/repro/proposed-fix). The body fields contain code paths and behaviour observations from your repo. **Review the printed finding bodies before passing `--auto-file` if any of that is sensitive.**
- **No telemetry.** The skill does not phone home, does not collect usage statistics, does not call any URL other than the GitHub API on `--auto-file` (and only when you opt in).
- **`gh` is required for `--auto-file`.** If `gh` is missing or not authenticated, the skill falls back to stdout-print and emits one warning line.

### Pre-disclosure / security findings

Findings whose `severity == P0` OR `area == security` are **never** auto-filed, regardless of the `--auto-file` flag. They print to stdout with a banner directing the operator to file a private disclosure via [GitHub security advisories](https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new) instead.

This refusal is non-negotiable. The intent is to prevent a vulnerability from leaking to a public Issue before it is fixed.

## Sharing findings

GitHub Issues at https://github.com/darylmcd/Roslyn-Backed-MCP are the public funnel for accepted findings. If you find something worth sharing:

1. Run `/mcp-server-surface-test --quick` (fast path) or `/mcp-server-surface-test --full` (comprehensive).
2. Review the finding bodies the skill prints to stdout.
3. Either copy/paste them into the [Surface-test finding](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/new?template=mcp-server-surface-test-finding.yml) issue template manually, or pass `--auto-file` and the skill calls `gh issue create` with the same body bytes.

The default print-to-stdout flow is the recommended starting point — it lets you review what the skill found before anything goes public.

The triage labels (`area:tools`, `area:perf`, `severity:P2`, etc.) are seeded via `scripts/seed-issue-labels.ps1` in this repo's checkout. Maintainers run that script once per repo bootstrap; consumers don't need to.

### Pre-disclosure refusal contract (verbatim)

Findings whose `severity == P0` OR `area == security` are **never** auto-filed, regardless of the `--auto-file` flag. They print to stdout with this banner:

> **SECURITY / P0 finding — DO NOT FILE PUBLICLY.**
> Escalate via GitHub security advisories: https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new

This refusal is non-negotiable. The intent is to prevent a vulnerability from leaking to a public Issue before it is fixed. See [SECURITY.md](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/SECURITY.md) for the full pre-disclosure path.

## Hard rules

- **Server-required.** No non-MCP fallback. If the Roslyn MCP server is not callable, the skill halts.
- **Read-only against `main`.** All apply-mode mutations confine to the disposable worktree (full tier only). The skill never pushes, never opens a PR, and never commits to your repo's history.
- **Cite, don't summarize.** Every finding references a concrete `file:line` and a tool call.
- **P0 / security findings are stdout-only.** See *Pre-disclosure / security findings* above.

## Operational notes

### Archiving old reports

The skill ships a small PowerShell wrapper at `scripts/archive-old-reports.ps1` that moves `*.md` reports older than N days (default 30) into a year-stamped `archive/<YYYY>/` subdirectory.

```bash
# Preview the archive plan without mutating anything.
pwsh -NoProfile -File ${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/scripts/archive-old-reports.ps1 -DryRun

# Archive reports older than 60 days.
pwsh -NoProfile -File ${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/scripts/archive-old-reports.ps1 -OlderThanDays 60
```

The script is idempotent and read-only when `-DryRun` is set.
