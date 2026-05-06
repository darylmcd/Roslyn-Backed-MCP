---
name: audit-deep
description: "Comprehensive Roslyn MCP server audit + experimental-promotion scorecard + plugin-skill audit, run against a loaded C# repo. Three modes — `full`, `promotion-only`, `read-only`. Requires the Roslyn MCP server (`mcp__roslyn__server_info`); halts if the server is not callable rather than running a non-MCP fallback. Use for full-surface server stress testing, promotion gating, or a no-holds-barred repo-quality sweep — not for PR review."
user-invocable: true
argument-hint: "[mode=full|promotion-only|read-only] (default: full)"
---

# /roslyn-mcp:audit-deep $ARGUMENTS

Run a comprehensive Roslyn-MCP audit against the current repository. The skill bundles its own audit prompt — no per-repo prompt copy is required.

## Step 1 — Hard precondition: Roslyn MCP server must be callable

This skill is a **null-op without the Roslyn MCP server**. The audit's entire purpose is to exercise the server's live surface — without it, the run produces no audit-grade evidence.

1. Verify `mcp__roslyn__server_info` appears in your current tool surface and call it. The response must include `connection.state: "ready"`.
2. If the call fails, the tool is missing, or `connection.state` is `initializing` / `degraded` / absent, **stop and report**:

   > *"This skill requires the Roslyn MCP server (`mcp__roslyn__*` tools must be callable, `connection.state` must be `ready`). Start the server — for example `dotnet tool run roslynmcp` or ensure the plugin's stdio entry is active in your client config — confirm `mcp__roslyn__server_info` returns `ready`, then re-invoke this skill."*

   Do **not** substitute `Read`, `Grep`, `Bash: dotnet build`, or any other host-side fallback. There is no generic non-MCP audit fallback in this skill — a broken server precondition halts the run.

## Step 2 — Parse `$ARGUMENTS` and pick the mode prompt

Recognized tokens (the only valid values for `mode`):

- `mode=full` (default) — full-repo sweep, including refactor pass with apply-mode mutations on a disposable worktree.
- `mode=promotion-only` — exercise the experimental-tier surface to produce a promotion scorecard. No Phase 6 product mutations.
- `mode=read-only` — preview-only / read-only across the entire surface. No applies anywhere. Promotion scorecard skipped (writers default to `needs-more-evidence`).

Unrecognized modes — including the historical `focused` value — are **not supported**; reject with a one-line message and ask the user to pick one of the three above.

Resolve the prompt body in this order:

1. `mode=full` → read `${CLAUDE_PLUGIN_ROOT}/skills/audit-deep/prompts/full.md` and run it verbatim.
2. `mode=promotion-only` → read `${CLAUDE_PLUGIN_ROOT}/skills/audit-deep/prompts/promotion-only.md` and run it verbatim.
3. `mode=read-only` → read `${CLAUDE_PLUGIN_ROOT}/skills/audit-deep/prompts/read-only.md` and run it verbatim.

The mode prompts are the source of truth for phase content, output schema, and hard-gate checkpoints. This SKILL.md supplies the orchestration wrapper: when a phase is listed in the phase-runner offload map below, execute that phase through the `audit-phase-runner` subagent when the host supports subagents; otherwise run the same phase inline and record `phase-runner: inline fallback` in the report header.

## Step 3 — Mutation safety: read-only against the audited repo's main branch

The audit is **read-only against the audited repository's `main` branch**. Phase 6 (refactor pass, `mode=full` only) writes apply-mode mutations, but only inside a disposable worktree the prompt creates and tracks. The flow is:

1. Before any Phase 6 apply, the prompt records a disposable branch / worktree / clone path in the report header (the *Isolation* row).
2. Phase 6's preview → apply chains run against that disposable checkout.
3. The audit report summarizes the changes; the operator decides whether to PR them. The audited repo's `main` branch is never directly mutated.

`mode=promotion-only` and `mode=read-only` skip Phase 6 entirely — no apply chains run, and the disposable worktree is optional.

## Step 4 — Phase-runner offload map

Use the repo-local `audit-phase-runner` subagent for phases that are long-running or log-heavy but not workspace-version-sensitive:

| Phase | Execution owner | Summary expected |
|---|---|---|
| Phase 1 — broad diagnostics scan | `audit-phase-runner` when available; inline fallback otherwise | diagnostics counts, top failures, elapsed time |
| Phase 2 — code quality metrics | `audit-phase-runner` when available; inline fallback otherwise | hotspot counts, metric bands, elapsed time |
| Phase 8 — build and test validation | `audit-phase-runner` when available; inline fallback otherwise | build/test verdict, pass/fail counts, failing names |
| Phase 8b — concurrency audit | `audit-phase-runner` when available; inline fallback otherwise | concurrency matrix counts, anomalies, elapsed time |

Run these phases inline in the main audit context: Phase -1, 0, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 16b, 17, and 18.

Hard boundary: Phase 6 and every preview/apply chain stay inline. Do not delegate workspace-version-sensitive mutations, even in `mode=full`, because the runner does not share the main audit context's preview evidence or disposable-checkout mutation ledger.

### Runner brief

When delegating, pass a compact brief with:

- `phase`: one of `1`, `2`, `8`, or `8b`
- `mode`: the selected mode
- `repoRoot`: absolute audited repo root
- `workspaceId`: loaded workspace id when applicable
- `solutionPath`: loaded solution or project path
- `reportPath`: current audit report draft path
- The relevant phase excerpt from the resolved mode prompt

The runner must return the `## Audit Phase Runner Summary` markdown table defined in `.claude/agents/audit-phase-runner.md`. Paste that table into the phase's report slot. If the runner is unavailable, run the phase inline and emit the same summary table yourself.

## Step 5 — Execute the chosen prompt

Read the resolved prompt file in full and follow it phase by phase. Persist the audit draft after each phase as the prompt instructs — the canonical report path lives in the prompt's *Output Format* section.

### Phase 0 hand-off: prefer `/surface-audit` for live-surface drift detection

The mode prompts' Phase 0 includes a *live-surface drift detection* sub-step that diffs the seeded coverage ledger against names referenced in the prompt's phase guidance. When a separate `/surface-audit` skill is available in the host's tool surface, prefer delegating that diff to it (one structured table back) instead of re-walking the live catalog from scratch in this skill's main agent.

- **When `/surface-audit` is available** — invoke it with the audited repo root, take the returned drift table, and paste it under Phase 0's drift-detection output slot. The two output buckets (`guidance gap` and `prompt drift`) map directly onto the structured table /surface-audit returns.
- **When `/surface-audit` is not available** — fall through to the in-prompt logic in Phase 0 step 14. Do not block the audit on the optional skill: the prompt's drift-detection still produces a valid result without it. Note in the report header which path you took (`drift-detection: delegated to /surface-audit` vs `drift-detection: in-prompt`).

Delegation is a performance and consistency optimization, not a correctness requirement; the in-prompt logic remains the authoritative fallback.

## Operational notes

### Archiving old audit reports — `scripts/archive-old-reports.ps1`

Reports written to the audit-reports directory accumulate over time. The skill ships a small PowerShell wrapper at `skills/audit-deep/scripts/archive-old-reports.ps1` that moves `*.md` files older than N days (default 30) into a year-stamped `archive/<YYYY>/` subdirectory, where `<YYYY>` is each file's `LastWriteTime` year. The reports directory path defaults to the audit-deep convention and can be overridden via `-ReportsRelativePath`.

Invocation (Bash on Windows or any shell with `pwsh` on path):

```bash
# Preview the archive plan without mutating anything.
pwsh -NoProfile -File skills/audit-deep/scripts/archive-old-reports.ps1 -DryRun

# Archive reports older than 60 days under the default reports directory.
pwsh -NoProfile -File skills/audit-deep/scripts/archive-old-reports.ps1 -OlderThanDays 60

# Archive against a non-default reports directory in a host repo.
pwsh -NoProfile -File skills/audit-deep/scripts/archive-old-reports.ps1 -ReportsRelativePath docs/audits
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
- **No PR.** This skill produces an audit report, not a refactor PR. Phase 6 mutations land in the disposable checkout's git history; the operator opens any PR separately.
- **Cite, don't summarize.** Every finding must reference a concrete file:line and a tool call — no abstract claims.
- **Mode is sticky.** Once you pick a mode in Step 2, the prompt's phase gating (which phases run in apply mode vs preview-only vs skipped) is fixed for the run.
