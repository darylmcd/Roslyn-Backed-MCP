# Deep Code Review & Refactor Agent Prompt

<!-- purpose: Living prompt for full-surface MCP audits, apply-tool exercise on a disposable worktree, and experimental→stable promotion scoring against the live Roslyn MCP server. Output is machine-parseable (fixed tables, dense per-call evidence, promotion scorecards). -->
<!-- DO NOT DELETE THIS FILE.
     Living document. When tools, resources, prompts, or skills are added/removed,
     the Phase 0 live-catalog capture + glob-based skill discovery in this prompt
     pick up the change automatically — no appendix edit required. -->

> **This prompt is a null-op without the Roslyn MCP server.** If `mcp__roslyn__server_info` is not callable in your current tool list, stop and ask the user to start the server. The very first step of Phase -1 verifies this as a hard gate.

> **Primary purpose:** produce (1) an MCP server audit (bugs, incorrect results, gaps) and (2) an experimental-tier promotion scorecard against one loaded C# repo. **Mechanism:** real refactoring plus tool calls that exercise the full surface. The static SKILL.md audit (frontmatter parity + tool-reference resolution against the live catalog) lives in `/surface-audit`, not here.

---

## Prompt

You are a senior .NET architect running a Roslyn-MCP audit against the loaded repo. You have **three missions** in priority order:

1. **MCP server audit (primary).** For every tool, resource, and prompt, ask whether the result is correct, complete, and consistent with sibling surfaces. Record issues, coverage, per-call `_meta.elapsedMs`, and error quality.
2. **Experimental promotion scorecard (primary).** Every experimental entry you exercise receives a rating — `promote`, `keep-experimental`, `needs-more-evidence`, or `deprecate` — with evidence citations. Feeds `docs/experimental-promotion-analysis.md` and release gating.
3. **Apply-tool exercise on a disposable worktree (supporting).** **Phase 6 only.** Drive preview→apply→revert round-trips, verify with `compile_check` / `build_workspace` / `test_run`. Applies are test fixtures of the apply-tool surface — they run inside a disposable worktree the prompt creates at run start and tears down at run end. The audited repo's `main` branch and primary working tree are never mutated; no commit ever lands in the audited repo's history; no PR is opened.

The static skills audit (SKILL.md frontmatter parity + tool-reference resolution against the live catalog) is owned by `/surface-audit`, which walks both `skills/*/SKILL.md` (shipped) and `.claude/skills/*/SKILL.md` (maintainer-local). It is a static-catalog check, not a server-execution check, and is not part of this run.

### Run shape

This prompt describes one canonical run. Phase 6 applies are always exercised against a disposable worktree the prompt creates at run start and tears down at run end (`workspace_close(drainProcesses=true)` to release the MCP host's analyzer DLL handles atomically with `dotnet build-server shutdown`, then `git worktree remove --force`, in that order — `dotnet build-server shutdown` alone leaves the host's analyzer-DLL lock in place on Windows). The promotion scorecard is always emitted. Typical duration: 90–180 min.

A single optional flag exists: `--no-worktree`, a degraded mode for environments that genuinely cannot create a git worktree (tight CI sandbox, missing `git` binary, read-only checkout). When set, Phase 6 is skipped, the *Isolation* row records `degraded — --no-worktree flag, Phase 6 applies skipped`, and writer rows whose round-trip evidence depended on the disposable worktree default to `needs-more-evidence` in the scorecard. Record `--no-worktree` in the report header so consumers know which evidence is missing.

**Known issues / prior findings.** When the audited repo has a tracked backlog or issue history, cross-check it and cite matching ids — common locations include `<audited-repo-root>/backlog.md`, a project's GitHub Issues, or a prior audit report. If no prior source exists, the regression section is **N/A**.

**Phase order.** Run in this order: **-1 → 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 8b → 10 → 9 → 11 → 12 → 13 → 14 → 15 → 16 → 17 → 18**. Phase 8b runs immediately after Phase 8 so it sees post-refactor state. Phase 9 runs after Phase 10 so `revert_last_apply` doesn't undo Phase 6 work.

**Portability and completeness contract.**

1. Runs against **any loadable C# repo**. Prefer `.sln` / `.slnx`; fall back to `.csproj`.
2. The disposable worktree is created at run start and recorded in the *Isolation* header row before any write-capable call. `--no-worktree` opts into degraded mode where Phase 6 applies are skipped — record the rationale in the *Isolation* row.
3. Persist `<audited-repo-root>/.audit-state.json` after every phase boundary and after every Phase 6 sub-phase. Minimum fields: `formatVersion`, `startedAtUtc`, `updatedAtUtc`, `targetRoot`, `reportPath`, `workspaceId`, `worktreePath`, `completedPhases`, `currentPhase`, `nextPhase`, and `lastCheckpointNote`. On startup, if this file exists and the user did not ask for a fresh run, read it before Phase -1 and resume from `nextPhase` after verifying the workspace/worktree still exist. If the checkpoint points at a missing worktree or stale workspace, run the cleanup path, record the recovery in the report header, and restart from Phase 0 instead of silently skipping earlier phases.
4. `server_info`, `roslyn://server/catalog`, and `roslyn://server/resource-templates` are the **authoritative live surface**. Any disagreement with prose in this prompt: the live catalog wins, and the drift is itself a finding.
5. Build a live **coverage ledger** from the catalog. Every live tool, resource, and prompt ends with exactly one final status: `exercised`, `exercised-apply`, `exercised-preview-only`, `skipped-repo-shape`, `skipped-safety`, `blocked`, or `scoped-but-skipped`. Silent omissions mean the audit is incomplete. Columns: `kind`, `name`, `tier`, `category`, `status`, `phase`, `lastElapsedMs`, `notes`. An agent MUST NOT assign `exercised` (or any `exercised-*` variant) unless at least one tool-call result for that tool name is recorded in the draft. If the tool was assigned to a phase but never called (context exhaustion, phase skipped), use `scoped-but-skipped`.
6. If the MCP client cannot invoke a live resource/prompt family, mark those rows `blocked` with the client limitation. Blocked experimental entries score `needs-more-evidence` in the scorecard — never `promote`.
7. Record repo-shape constraints up front: single vs multi-project, tests, analyzers, source generators, DI, `.editorconfig`, Central Package Management, multi-targeting, network/restore constraints.
8. Do not invent applicability. If the repo has no tests, no DI, no source generators, no multi-targeting, or only one project, record that and mark dependent steps `skipped-repo-shape`.

### Execution strategy and context conservation

1. Delegate long-running/log-heavy validation to subagents or background execution where available (full-suite `test_run`, `test_coverage`, shell-based builds). Keep experimental probes inline — promotion evidence is captured per call.
2. Helpers return structured summaries (tool, scope, pass/fail counts, failing test names, duration, coverage headline, anomalies) — never raw logs.
3. Do not delegate preview/apply chains or workspace-version-sensitive mutations unless the helper shares the same disposable checkout and workspace state.

### Cross-cutting audit principles (apply to every call)

Fixed output slots in the report; capture in real time.

1. **Inline severity signal.** Tag each result **PASS** / **FLAG** / **FAIL**. Accumulate for the report.
2. **Schema vs behaviour.** Compare actual behaviour to the MCP tool schema/description. Mismatches are high-value findings. *Output slot:* *Schema vs behaviour drift*.
3. **Performance (`_meta.elapsedMs`).** Every v1.8+ response carries `_meta.elapsedMs` (total wall-clock) plus, for workspace calls, `_meta.queuedMs` / `_meta.heldMs` / `_meta.gateMode`. Record per call. Budgets: single-symbol reads ≤5 s, solution scans ≤15 s, writers ≤30 s. The scorecard uses p50 per tool, so paged data matters. *Output slot:* *Performance baseline*.
4. **Error message quality.** When a tool errors, rate it **actionable** / **vague** / **unhelpful**. *Output slot:* *Error message quality*.
5. **Response-contract consistency.** Note inconsistencies across related surfaces (line-number base, field names, classification value types, pagination defaults). *Output slot:* *Response contract consistency* (conditional).
6. **Parameter-path coverage.** Happy-path defaults are not enough. For each major family you exercise, probe at least one non-default path when the live schema exposes it. If the schema does not expose a parameter this prompt mentions, record `N/A` rather than inventing it. *Output slot:* *Parameter-path coverage*.
7. **Precondition discipline.** Distinguish server defects from repo/environment constraints. A tool that fails because tests are absent or packages unrestored is not a server bug — record the precondition status, not a false regression.
8. **Debug log capture.** If the MCP client surfaces `notifications/message` log entries (the `McpLoggingProvider` forwards .NET `ILogger` events with `correlationId`), keep that channel visible for the entire run. Record every `Warning` / `Error` / `Critical`, plus `Information` entries touching workspace lifecycle, gate acquisition, lock contention, rate limits, or request timeouts. If the client can't show these, record that as a client limitation in the header — do not silently drop the channel.
9. **`evaluate_csharp` stalls — neutral diagnosis only.** The server applies a script timeout (default 10 s, env `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS`). A healthy call finishes or errors within budget + grace. Multi-minute freezes that only clear on user message: **cause = unknown — operator triages from MCP stderr + client logs.** Agents cannot reliably attribute these from inside the loop; do not speculate server-vs-client-vs-transport.
10. **Always emit text per turn.** Every assistant turn emits at least one sentence describing what is being dispatched. Empty tool-only turns read as silent stalls.
11. **Phases run sequentially.** Parallel within a phase for independent reads; never start phase N+1 before phase N is persisted to the draft.
12. **Output budget per turn.** When cumulative tool-result size passes ~250 KB, persist and start a new turn. Usual culprits: `compile_check`, `project_diagnostics`, `list_analyzers`, `get_namespace_dependencies`, `get_msbuild_properties`. Use pagination (`offset`, `limit`, `severity`, `file`). The `workspace_*` summary payloads (default) keep per-phase heartbeats at ~500 B; request `verbose=true` only when you need the full project tree.
13. **Workspace heartbeat.** Call `workspace_list` before each new phase. If the workspace is gone, reload from the recorded entrypoint — do not march on against a dead workspace.
14. **Experimental promotion signal (per call).** For every experimental tool/resource/prompt exercised, record one line in the draft: (a) correct result, (b) schema accurate, (c) error path actionable on at least one negative probe, (d) preview→apply round-trip clean where applicable, (e) wall-clock within budget for the input scale. Do not compute the final recommendation until Final surface closure — it must reflect all phases, not just first contact.

---

## Phase sub-files — read these now before executing

This prompt is the orchestrator. The phase-level instructions live in three sub-files under `prompts/phases/`. Read each file in full before executing the phases it covers:

1. **Read `prompts/phases/setup-and-analysis.md` now** to get the instructions for phases -1, 0, 0.5, 1, 2, 3, 4, and 5. These phases establish the MCP server precondition, load the workspace, seed the coverage ledger, and run diagnostics, metrics, symbol, flow, and snippet analysis.

2. **Read `prompts/phases/apply-and-test.md` now** to get the instructions for phases 6 (all sub-phases 6a–6z), 7, 8, 8b, 9, and 10. These phases exercise the apply-tool write path on the disposable worktree, validate build and test, run the concurrency audit, verify undo, and exercise file/project operations.

3. **Read `prompts/phases/output-and-close.md` now** to get the instructions for phases 11–19, Final surface closure, Output Format, Promotion scorecard schema, and the Appendix. These phases cover semantic search, scaffolding, project mutation, navigation, resources, prompts, boundary testing, regression verification, finding emission, and the mandatory report output.

Execute phases in the order stated in the *Phase order* line above: **-1 → 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 8b → 10 → 9 → 11 → 12 → 13 → 14 → 15 → 16 → 17 → 18**.

---

### Phase 0.5: Subagent dispatch plan

This orchestrator delegates log-heavy read-side phases to the `audit-phase-runner` agent via the **Subagent dispatch groups** defined in `prompts/phases/setup-and-analysis.md`. The dispatch groups are:

- **Group A** (read-side diagnostics and metrics): phases 1, 2
- **Group B** (symbol and flow analysis on selected types/methods): phases 3, 4
- **Group C** (build, test, concurrency): phases 7, 8, 8b

**Orchestrator-owned phases** (never delegated): Phase 6 **setup** and teardown of the disposable worktree remain in this orchestrator. The try/finally discipline for the worktree must live in this single surviving caller — a crashed runner subagent must not leak an open worktree. All preview/apply chains in Phase 6 stay inline.

See `prompts/phases/setup-and-analysis.md` for the full Phase 0.5 dispatch plan with group tables, timeout budgets, and runner handoff protocol.

### Phase 1: (full instructions in prompts/phases/setup-and-analysis.md)
