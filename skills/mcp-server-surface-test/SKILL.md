---
name: mcp-server-surface-test
description: "Consumer-facing audit of the Roslyn MCP server's live surface against a loaded C# repo. Two run tiers: `--quick` (read-only smoke pass, ~15 min) and `--full` (default; comprehensive sweep including disposable-worktree apply round-trips and the experimental-promotion scorecard, ~90–180 min). Findings print to stdout by default; pass `--auto-file` to file each finding as a GitHub Issue at https://github.com/darylmcd/Roslyn-Backed-MCP. Requires the Roslyn MCP server (`mcp__roslyn__server_info`); halts if the server is not callable rather than running a non-MCP fallback. Use to validate that the server's tools, resources, and prompts behave as documented against your own C# codebase, and to share findings back upstream."
user-invocable: true
argument-hint: "[<target-repo-path>] [--quick | --full] [--auto-file] [--no-worktree]"
---

# /mcp-server-surface-test $ARGUMENTS

Run a Roslyn MCP server audit against a loaded C# repo. The skill bundles two prompt files — `prompts/quick.md` for the bounded read-only tier and `prompts/full.md` for the comprehensive run — and routes between them based on `$ARGUMENTS`.

## Step 1 — Hard precondition: Roslyn MCP server must be callable

This skill is a **null-op without the Roslyn MCP server**. The audit's entire purpose is to exercise the server's live surface — without it, the run produces no audit-grade evidence.

1. Verify `mcp__roslyn__server_info` appears in your current tool surface and call it. The response must include `connection.state: "ready"`.
2. If the call fails, the tool is missing, or `connection.state` is `initializing` / `degraded` / absent, **stop and report**:

   > *"This skill requires the Roslyn MCP server (`mcp__roslyn__*` tools must be callable, `connection.state` must be `ready`). Start the server — for example `dotnet tool run roslynmcp` or ensure the plugin's stdio entry is active in your client config — confirm `mcp__roslyn__server_info` returns `ready`, then re-invoke this skill."*

   Do **not** substitute `Read`, `Grep`, `Bash: dotnet build`, or any other host-side fallback. There is no generic non-MCP audit fallback in this skill — a broken server precondition halts the run.

## Step 2 — Parse $ARGUMENTS and route to a tier prompt

`$ARGUMENTS` may include any combination of the following tokens, in any order. Tokens are space-separated.

### Target repo (default: current Claude Code session's repo root)

The audit needs a C# workspace to load via `mcp__roslyn__workspace_load`. By default the audit targets the **current session's repo root**. Override the target in two equivalent ways:

- `--target=<absolute-path-or-repo-root>` — explicit flag form.
- *(bare path)* — any single token in `$ARGUMENTS` that resolves to an existing directory containing a `.sln`/`.slnx`/`.csproj` file.

Resolution rules:

1. If both `--target=<path>` and a bare path appear, **stop and report the conflict** — pick one form. Do not silently prefer one over the other.
2. The resolved target path becomes the **audited repo root** throughout the prompt — *every* output path computed by the prompt (the audit report, the disposable worktree base, finding emission) anchors here, not at the agent session's CWD.
3. If the resolved path does not exist, contains no `.sln`/`.slnx`/`.csproj`, or is not a git working tree, **stop and report** — the audit cannot proceed without a loadable workspace and a tree the disposable-worktree step can branch from.
4. Pass the resolved target to the prompt's Phase 0 as the `workspace_load` argument; the prompt's *Isolation* row in the report header records the resolved target path.

### Tier flags

- *(no flag, default)* — `--full` is the default. Read `prompts/full.md` and run it phase by phase.
- `--quick` — read-only smoke pass. Read `prompts/quick.md` instead. Target runtime ≤15 minutes vs `--full`'s 90–180 minutes. No apply-mode mutations, no disposable worktree, no test runs, no `nuget_vulnerability_scan` (network-dependent).
- `--full` — explicit form of the default; routes to `prompts/full.md`.
- **Conflict:** `--quick` and `--full` together → stop and report; pick one.

### `--no-worktree` (full tier only)

- *(no flag, default)* — full canonical run with the disposable-worktree apply pass exercised.
- `--no-worktree` — degraded mode for environments that genuinely cannot create a git worktree. Phase 6 sub-phases that require a worktree are marked `skipped-safety — --no-worktree`. The promotion scorecard still emits, but writer recommendations default to `needs-more-evidence` for any tool whose round-trip evidence depended on the disposable worktree. **Has no effect under `--quick`** (the quick tier already skips Phase 6); reject the combination with a one-line message.

### `--auto-file` (both tiers)

- *(no flag, default)* — actionable findings render to stdout as ready-to-paste GitHub Issue bodies. The skill prints the Issue title, labels, and body; the operator decides what to do with each.
- `--auto-file` — opt-in. After the audit completes and findings are rendered, the skill calls `gh issue create` against `https://github.com/darylmcd/Roslyn-Backed-MCP` for each actionable finding. Requirements:
  - `gh` is on `PATH`. If missing, fall back to stdout-print and emit one warning line.
  - `gh auth status` reports an authenticated session. If not, fall back to stdout-print and emit one warning line.
  - **Refusal contract:** the skill does **not** call `gh issue create` for any finding whose `severity == P0` or whose `area == security`. Such findings print to stdout with this header line: `**SECURITY / P0 finding — DO NOT FILE PUBLICLY.** Escalate via GitHub security advisories at https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new`. The refusal applies regardless of `--auto-file` and is the load-bearing pre-disclosure safeguard.

### Reject

Reject any token that is neither a valid target path, `--target=<path>`, `--quick`, `--full`, `--no-worktree`, nor `--auto-file`. One-line message; ask the user to fix or drop the offending token.

## Step 3 — Mutation safety: disposable worktree (full tier, default mode)

The audit is **read-only against the audited repository's `main` branch**. Phase 6 of the full tier writes apply-mode mutations, but only inside a disposable worktree the prompt creates and tracks. The flow is:

1. Before any Phase 6 apply, the prompt creates a disposable branch + worktree at run start and records the path in the report header (the *Isolation* row). This is **mandatory** in default full-mode — Phase 6 cannot run against the audited repo's primary checkout.
2. Phase 6's preview → apply chains run against that disposable checkout. Applies are exercised as test fixtures of the apply-tool surface (preview→apply→revert round-trips, `compile_check` after apply, `build_workspace` + `test_run` after apply). The point is to exercise the write path of the MCP server, not to ship product changes.
3. The disposable worktree is torn down at run end via `dotnet build-server shutdown` followed by `git worktree remove --force` (the Windows lock-release sequence is mandatory; `dotnet build-server shutdown` releases `testhost.exe` / `VBCSCompiler.exe` locks on the worktree's `bin/` dirs). Teardown runs even on apply failure — the prompt wraps the Phase 6 chain in `try/finally` discipline.
4. The audited repo's `main` branch is **never** directly mutated. No PR is opened from the audit run. No commits land in the audited repo's history.

The `--no-worktree` flag opts into a degraded mode where the disposable worktree is skipped — see Step 2 for the contract and the report-header record requirement. The `--quick` tier always skips the disposable worktree (no apply-mode phases run at all).

## Step 4 — Execute the chosen prompt

- `--quick`: read `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/prompts/quick.md` and run it phase by phase.
- `--full` (default): read `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/prompts/full.md` and run it phase by phase.

Persist the audit draft after each phase as the prompt instructs — the canonical report path lives in the prompt's *Output Format* section: `<audited-repo-root>/audit-reports/<timestamp>_<repo-id>_mcp-server-surface-test.md`.

## Step 5 — Finding emission (both tiers)

After the audit phases complete, the prompt's final finding-emission phase walks every actionable finding (entries in *MCP server issues* or *Improvement suggestions* with a concrete fix sketch) and renders each through one shared envelope:

- **Default (no `--auto-file`):** print each finding to stdout as a ready-to-paste GitHub Issue body. The render block is `## TITLE`, label list, and the structured body fields (`id`, `source-repo`, `severity`, `area`, `server-version`, `anchors`, `finding`, `repro`, `proposed-fix`).
- **With `--auto-file`:** call `gh issue create --repo darylmcd/Roslyn-Backed-MCP --title <title> --label <area:X,severity:Y> --body-file <tempfile>` per finding, except those refused by the P0/security refusal contract (Step 2). Refused findings still print to stdout with the security-advisory escalation banner.

## Operational notes

### Archiving old audit reports — `scripts/archive-old-reports.ps1`

Reports written to the `audit-reports/` directory accumulate over time. The skill ships a small PowerShell wrapper at `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/scripts/archive-old-reports.ps1` that moves `*.md` files older than N days (default 30) into a year-stamped `archive/<YYYY>/` subdirectory. The reports directory path defaults to `audit-reports` and can be overridden via `-ReportsRelativePath`.

Invocation (any shell with `pwsh` on path):

```bash
# Preview the archive plan without mutating anything.
pwsh -NoProfile -File ${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/scripts/archive-old-reports.ps1 -DryRun

# Archive reports older than 60 days under the default reports directory.
pwsh -NoProfile -File ${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/scripts/archive-old-reports.ps1 -OlderThanDays 60
```

Behavior contract:

- **Pinned filenames are never archived** — `README.md` stays in place regardless of age.
- **Idempotent** — running twice is safe. The destination year-subdirectory is created on demand.
- **Read-only when `-DryRun` is set.**
- **Independent of the Roslyn MCP server** — the archive script does not require MCP tooling.

The script is invoked manually (no automatic scheduler).

## Hard rules

- **Server-required.** No generic non-MCP fallback exists. If `mcp__roslyn__server_info` is not callable or `connection.state` is not `ready`, halt.
- **Read-only against `main`.** All apply-mode mutations confine to the disposable worktree the prompt creates. Never push or merge from inside this skill.
- **No PR.** This skill produces an audit report, not a refactor PR. Phase 6 mutations are exercised as apply-tool fixtures inside the disposable worktree and torn down at run end.
- **Cite, don't summarize.** Every finding must reference a concrete file:line and a tool call — no abstract claims.
- **Disposable-worktree teardown is mandatory** (full tier, default mode). The Phase 6 apply chain runs inside `try/finally` so teardown executes even on apply failure. `dotnet build-server shutdown` always precedes `git worktree remove --force` on Windows.
- **P0 / security findings are never auto-filed.** The refusal applies whether the operator passed `--auto-file` or not — those findings are stdout-only with the security-advisory escalation banner.
