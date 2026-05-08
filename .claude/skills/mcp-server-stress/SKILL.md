---
name: mcp-server-stress
description: "Comprehensive Roslyn MCP server audit + experimental-promotion scorecard, run against a loaded C# repo. Single canonical run — always exercises apply tools against a disposable worktree the skill creates and tears down post-run, and always emits the promotion scorecard. Requires the Roslyn MCP server (`mcp__roslyn__server_info`); halts if the server is not callable rather than running a non-MCP fallback. Maintainer-only skill (lives under `.claude/skills/`, not shipped to plugin consumers). The static SKILL.md audit (frontmatter parity + tool-reference resolution against the live catalog) is owned by `/surface-audit`, not this skill. Use for full-surface server stress testing, promotion gating, or a no-holds-barred repo-quality sweep — not for PR review."
user-invocable: true
argument-hint: "[<target-repo-path>] [--target=<path>] [--no-worktree]"
---

# /mcp-server-stress $ARGUMENTS

Run a comprehensive Roslyn-MCP audit against the current repository. The skill bundles its own audit prompt — no per-repo prompt copy is required.

## Step 1 — Hard precondition: Roslyn MCP server must be callable

This skill is a **null-op without the Roslyn MCP server**. The audit's entire purpose is to exercise the server's live surface — without it, the run produces no audit-grade evidence.

1. Verify `mcp__roslyn__server_info` appears in your current tool surface and call it. The response must include `connection.state: "ready"`.
2. If the call fails, the tool is missing, or `connection.state` is `initializing` / `degraded` / absent, **stop and report**:

   > *"This skill requires the Roslyn MCP server (`mcp__roslyn__*` tools must be callable, `connection.state` must be `ready`). Start the server — for example `dotnet tool run roslynmcp` or ensure the plugin's stdio entry is active in your client config — confirm `mcp__roslyn__server_info` returns `ready`, then re-invoke this skill."*

   Do **not** substitute `Read`, `Grep`, `Bash: dotnet build`, or any other host-side fallback. There is no generic non-MCP audit fallback in this skill — a broken server precondition halts the run.

## Step 2 — Read and run the canonical audit prompt

Read `${CLAUDE_PLUGIN_ROOT}/.claude/skills/mcp-server-stress/prompts/prompt.md` and run it verbatim. Always-on flags:

- **Promotion scorecard always emitted.** The `_latest-promotion-scorecard.json` sibling artifact is mandatory output.
- **Phase 6 apply pass always exercised** against a disposable worktree the skill creates at run start and tears down at run end. The audited repo's working tree and `main` branch are never mutated by Phase 6.

### Arguments

`$ARGUMENTS` may include any combination of the following tokens, in any order. Tokens are space-separated.

#### Target repo (default: current Claude Code session's repo root)

The audit needs a C# workspace to load via `mcp__roslyn__workspace_load`. By default the audit targets the **current session's repo root** — the typical case when you invoke the skill from a Claude Code session inside the repo you want to audit. Override the target in two equivalent ways:

- `--target=<absolute-path-or-repo-root>` — explicit flag form. Example: `--target=C:/Code-Repo/DotNet-Firewall-Analyzer`.
- *(bare path)* — any single token in `$ARGUMENTS` that resolves to an existing directory containing a `.sln`/`.slnx`/`.csproj` file. Example: `/mcp-server-stress C:/Code-Repo/DotNet-Firewall-Analyzer`. The bare-path shorthand is purely ergonomic; the explicit `--target=` form is preferred in scripted invocations because it cannot collide with future flags.

Resolution rules:

1. If both `--target=<path>` and a bare path appear, **stop and report the conflict** — pick one form. Do not silently prefer one over the other.
2. The resolved target path becomes the **audited repo root** throughout the prompt — *every* output path computed by the prompt (`<audited-repo>/ai_docs/audit-reports/...`, `<audited-repo>/backlog.d/...`, the disposable worktree base) anchors here, not at the agent session's CWD. This is the cross-repo invocation pattern: the agent session lives in `Roslyn-Backed-MCP`, but the audit reads from and writes evidence to `<target>` exclusively.
3. If the resolved path does not exist, contains no `.sln`/`.slnx`/`.csproj`, or is not a git working tree, **stop and report** — the audit cannot proceed without a loadable workspace and a tree the disposable-worktree step can branch from.
4. Pass the resolved target to the prompt's Phase 0 as the `workspace_load` argument; the prompt's *Isolation* row in the report header records the resolved target path so audit consumers can trace which workspace produced the report.

#### `--no-worktree`

- *(no flag, default)* — full canonical run with the disposable-worktree apply pass exercised.
- `--no-worktree` — degraded-mode run for environments that genuinely cannot create a git worktree (tight CI sandbox, missing `git` binary, read-only checkout). When this flag is set, the prompt records the gap in the report header's *Isolation* row as `degraded — --no-worktree flag, Phase 6 applies skipped` and Phase 6 sub-phases that require a worktree are marked `skipped-safety — --no-worktree`. The promotion scorecard still emits, but writer recommendations default to `needs-more-evidence` for any tool whose round-trip evidence depended on the disposable worktree.

#### Reject

Reject any token that is neither a valid target path, `--target=<path>`, nor `--no-worktree`. One-line message; ask the user to fix or drop the offending token.

The prompt is the source of truth for phase content, output schema, and hard-gate checkpoints. This SKILL.md supplies the orchestration wrapper: when a phase is listed in the phase-runner offload map below, execute that phase through the `audit-phase-runner` subagent when the host supports subagents; otherwise run the same phase inline and record `phase-runner: inline fallback` in the report header.

## Step 3 — Mutation safety: disposable worktree is mandatory (default mode)

The audit is **read-only against the audited repository's `main` branch**. Phase 6 (refactor pass) writes apply-mode mutations, but only inside a disposable worktree the prompt creates and tracks. The flow is:

1. Before any Phase 6 apply, the prompt creates a disposable branch + worktree at run start and records the path in the report header (the *Isolation* row). This is **mandatory** in default mode — Phase 6 cannot run against the audited repo's primary checkout.
2. Phase 6's preview → apply chains run against that disposable checkout. Applies are exercised as test fixtures of the apply-tool surface (preview→apply→revert round-trips, `compile_check` after apply, `build_workspace` + `test_run` after apply). The point is to exercise the write path of the MCP server, not to ship product changes.
3. The disposable worktree is torn down at run end via `dotnet build-server shutdown` followed by `git worktree remove --force` (the Windows lock-release sequence is mandatory; `dotnet build-server shutdown` releases `testhost.exe` / `VBCSCompiler.exe` locks on the worktree's `bin/` dirs). Teardown runs even on apply failure — the prompt wraps the Phase 6 chain in `try/finally` discipline.
4. The audited repo's `main` branch is **never** directly mutated. No PR is opened from the audit run. No commits land in the audited repo's history.

The `--no-worktree` flag (Step 2) opts into a degraded mode where the disposable worktree is skipped — see Step 2 for the contract and the report-header record requirement.

## Step 4 — Phase-runner offload map

Use the repo-local `audit-phase-runner` subagent for phases that are long-running or log-heavy but not workspace-version-sensitive:

| Phase | Execution owner | Summary expected |
|---|---|---|
| Phase 1 — broad diagnostics scan | `audit-phase-runner` when available; inline fallback otherwise | diagnostics counts, top failures, elapsed time |
| Phase 2 — code quality metrics | `audit-phase-runner` when available; inline fallback otherwise | hotspot counts, metric bands, elapsed time |
| Phase 8 — build and test validation | `audit-phase-runner` when available; inline fallback otherwise | build/test verdict, pass/fail counts, failing names |
| Phase 8b — concurrency audit | `audit-phase-runner` when available; inline fallback otherwise | concurrency matrix counts, anomalies, elapsed time |

Run these phases inline in the main audit context: Phase -1, 0, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, and 18.

Hard boundary: Phase 6 and every preview/apply chain stay inline. Do not delegate workspace-version-sensitive mutations because the runner does not share the main audit context's preview evidence or disposable-worktree mutation ledger.

### Runner brief

When delegating, pass a compact brief with:

- `phase`: one of `1`, `2`, `8`, or `8b`
- `repoRoot`: absolute audited repo root
- `workspaceId`: loaded workspace id when applicable
- `solutionPath`: loaded solution or project path
- `reportPath`: current audit report draft path
- The relevant phase excerpt from the canonical prompt

The runner must return the `## Audit Phase Runner Summary` markdown table defined in `.claude/agents/audit-phase-runner.md`. Paste that table into the phase's report slot. If the runner is unavailable, run the phase inline and emit the same summary table yourself.

## Step 5 — Execute the chosen prompt

Read `prompts/prompt.md` in full and follow it phase by phase. Persist the audit draft after each phase as the prompt instructs — the canonical report path lives in the prompt's *Output Format* section.

### Phase 0 hand-off: prefer `/surface-audit` for live-surface drift detection

The prompt's Phase 0 includes a *live-surface drift detection* sub-step that diffs the seeded coverage ledger against names referenced in the prompt's phase guidance. When a separate `/surface-audit` skill is available in the host's tool surface, prefer delegating that diff to it (one structured table back) instead of re-walking the live catalog from scratch in this skill's main agent.

- **When `/surface-audit` is available** — invoke it with the audited repo root, take the returned drift table, and paste it under Phase 0's drift-detection output slot. The two output buckets (`guidance gap` and `prompt drift`) map directly onto the structured table /surface-audit returns.
- **When `/surface-audit` is not available** — fall through to the in-prompt logic in Phase 0 step 14. Do not block the audit on the optional skill: the prompt's drift-detection still produces a valid result without it. Note in the report header which path you took (`drift-detection: delegated to /surface-audit` vs `drift-detection: in-prompt`).

Delegation is a performance and consistency optimization, not a correctness requirement; the in-prompt logic remains the authoritative fallback.

## Operational notes

### Archiving old audit reports — `scripts/archive-old-reports.ps1`

Reports written to the audit-reports directory accumulate over time. The skill ships a small PowerShell wrapper at `.claude/skills/mcp-server-stress/scripts/archive-old-reports.ps1` that moves `*.md` files older than N days (default 30) into a year-stamped `archive/<YYYY>/` subdirectory, where `<YYYY>` is each file's `LastWriteTime` year. The reports directory path defaults to the audit-deep convention and can be overridden via `-ReportsRelativePath`.

Invocation (Bash on Windows or any shell with `pwsh` on path):

```bash
# Preview the archive plan without mutating anything.
pwsh -NoProfile -File .claude/skills/mcp-server-stress/scripts/archive-old-reports.ps1 -DryRun

# Archive reports older than 60 days under the default reports directory.
pwsh -NoProfile -File .claude/skills/mcp-server-stress/scripts/archive-old-reports.ps1 -OlderThanDays 60

# Archive against a non-default reports directory in a host repo.
pwsh -NoProfile -File .claude/skills/mcp-server-stress/scripts/archive-old-reports.ps1 -ReportsRelativePath docs/audits
```

Behavior contract:

- **Pinned filenames are never archived** — `README.md` and `deep-review-session-checklist.md` stay in place regardless of age.
- **Idempotent** — running twice is safe. The destination year-subdirectory is created on demand. If a file with the same name already exists at the destination, the move is skipped and a warning is emitted.
- **Read-only when `-DryRun` is set** — no filesystem mutations occur; the script reports what it would do.
- **Independent of the Roslyn MCP server** — unlike the audit itself, the archive script does not require any MCP tooling to be running.

The script is invoked manually (no automatic scheduler today). Recommended cadence: run once at the end of each release cut or at the start of a new audit pass.

## Hard rules

- **Server-required.** No generic non-MCP fallback exists. If `mcp__roslyn__server_info` is not callable or `connection.state` is not `ready`, halt.
- **Read-only against `main`.** All apply-mode mutations confine to the disposable worktree the prompt creates. Never push or merge from inside this skill.
- **No PR.** This skill produces an audit report, not a refactor PR. Phase 6 mutations are exercised as apply-tool fixtures inside the disposable worktree and torn down at run end. There is nothing to PR.
- **Cite, don't summarize.** Every finding must reference a concrete file:line and a tool call — no abstract claims.
- **Disposable-worktree teardown is mandatory.** The Phase 6 apply chain runs inside `try/finally` so teardown executes even on apply failure. `dotnet build-server shutdown` always precedes `git worktree remove --force` on Windows.
