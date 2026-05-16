# Phase Group: Setup and Analysis (Phases -1 through 5)

<!-- purpose: Sub-file of the mcp-server-surface-test full prompt. Contains phases -1, 0, 0.5, 1, 2, 3, 4, 5. -->
<!-- Parent orchestrator: ../full.md — read that file first for cross-cutting principles and execution strategy. -->

---

### Phase -1: MCP server precondition (MUST run first, hard gate)

This prompt is a contract with the Roslyn MCP server. Without it, nothing below is meaningful.

1. **Check the tool list.** Verify `mcp__roslyn__server_info` appears in your current tool surface. If it does not, STOP and tell the user:

   > *"This prompt requires the Roslyn MCP server (mcp__roslyn__* tools must be callable). Start the server — for example `dotnet tool run roslynmcp` or ensure the plugin's stdio entry is active in `.mcp.json` / `settings.json` — confirm `mcp__roslyn__server_info` is available, then rerun."*

   Do **not** substitute `Read`, `Grep`, `Bash: dotnet build`, or similar host-side fallbacks. The entire point of this audit is to exercise the MCP server; a run without it produces no audit-grade evidence.

2. **Call `server_info`.** Capture:
   - `version`, `catalogVersion`, `runtime`, `os`.
   - `surface.tools.{stable,experimental}`, `surface.resources.{stable,experimental}`, `surface.prompts.{stable,experimental}`, `surface.registered.*`, `surface.registered.parityOk` (must be `true`; `false` is a P2 finding).
   - `connection.state` — if `initializing` or `degraded`, wait briefly and call `server_heartbeat` once before proceeding. If the state never becomes `ready`, halt and surface the diagnostic.

3. **Record the live totals** from `server_info` — these are the authoritative counts the coverage ledger and scorecard will reconcile against. Any prose in this prompt that disagrees with the live numbers is drift and is itself a finding (log it in *Improvement suggestions*).

4. **Sanity-check the catalog resource.** Read `roslyn://server/catalog` and confirm per-category counts match `server_info.surface`. A mismatch here is a P2 finding.

5. **Workspace health probe (post-load).** After Phase 0 loads a workspace, call `workspace_health(workspaceId)` once before Phase 1's first semantic call. Capture `status` (`healthy` / `degraded` / `unhealthy`), `staleness` indicators, and any returned remediation hints. A non-`healthy` status before any mutation is a P1 finding — surface it and either reload or halt; do not march on against a degraded workspace. (`server_heartbeat` covers transport readiness; `workspace_health` covers per-workspace state.)

**Hard-gate checkpoint:** Is `server_info` callable? Is `connection.state == ready`? Is `parityOk == true`? Did the catalog-resource counts match `server_info`? Did `workspace_health` (once a workspace is loaded) return `healthy`? Any `no` is a halt-or-escalate, not a silent proceed.

---

### Phase 0: Setup, live surface baseline, and repo shape

1. Pick the entrypoint: `.sln` / `.slnx` / `.csproj`.
2. **Create the disposable worktree** (mandatory, default mode). Run `git worktree add .worktrees/surface-test-<ts> -b mcp-server-surface-test/<ts>` from the audited repo root, where `<ts>` is the same UTC `yyyyMMddTHHmmssZ` used for the report filename. The worktree path is **inside** the audited repo root (under `.worktrees/`, which `git` ignores by convention and which falls under `workspace_load`'s sanctioned-root check). Record the absolute worktree path + branch name in the *Isolation* header row before any write-capable call. Phase 6's preview→apply chains run against this checkout; the audited repo's primary working tree is never touched. **`--no-worktree` flag:** record `degraded — --no-worktree flag, Phase 6 applies skipped` in the *Isolation* row and skip worktree creation entirely.
2a. **Capture the Isolation baseline.** Run `git -C <audited-repo-root> status --porcelain` against the audited repo's **primary checkout** and record the exact output verbatim in the *Isolation baseline* sub-row of the header. Empty output is the common case (clean tree); non-empty output is allowed but records pre-existing operator state out of scope for this audit. The Final surface closure's run-end git-status diff (step 3a) compares against this baseline — any new `M` / `A` / `D` / `??` entry that appears at run end is an audit-prompt leak. This baseline is the catch-all that makes the *Mutation isolation contract* (Phase 7 / 8b / 13 — all writes target the disposable worktree, never the primary checkout) audit-verifiable instead of trust-based.
3. **Debug-log channel check.** Is the client surfacing `notifications/message`? Record `yes` / `partial` / `no` in the header.
4. Read `roslyn://server/resource-templates` to capture all resource URI templates.
5. Call `workspace_load` (lean summary default; pass `verbose=true` only if you need the full project tree).
6. Call `workspace_list` to confirm the session; `workspace_status` to confirm clean load (look at `WorkspaceDiagnosticCount` / `WorkspaceErrorCount` / `WorkspaceWarningCount`).
7. **`workspace_warm` (v1.28+).** Optional but recommended: call `workspace_warm(workspaceId)` immediately after load to prime `GetCompilationAsync` + semantic models. Record `projectsWarmed`, `coldCompilationCount`, `elapsedMs`. This makes every downstream perf measurement cache-hit-dominated — note whether you ran it in the report header so timings are comparable across runs.
8. Call `project_graph`.
9. Record repo-shape constraints (projects, tests, analyzers, source generators, DI, `.editorconfig`, CPM, multi-targeting, network/restore constraints).
10. **Restore precheck.** Run `dotnet restore <entrypoint>` from the host shell before any Phase-1 semantic-analysis tool. Unrestored package references historically crashed `find_unused_symbols`, `type_hierarchy`, `callers_callees`, and `impact_analysis` (FLAG-A; filtered in v1.7+). Also: without restore, `compile_check`/`project_diagnostics` flood with CS0246 errors from missing types, exhausting the per-turn output budget (principle #12). If the host has no shell, mark this step `blocked` — note degradation risk in the header.
11. **Seed the coverage ledger** from the live catalog so every tool/resource/prompt has a planned phase or a provisional skip reason. No hand-maintained list; trust `roslyn://server/catalog`.
12. **Seed the promotion scorecard** with one row per experimental entry (tool + resource + prompt). Leave `recommendation` blank until Final surface closure.
13. **Seed the Performance baseline** table. Every exercised read surface contributes a row; writers contribute in Phase 8b.5.
14. **Live-surface drift detection.** After seeding the coverage ledger, diff its tool/resource/prompt name set against the names this prompt mentions in its phase guidance:
    - **Names in catalog but never named in the prompt's phase guidance** → log under *Improvement suggestions* as `guidance gap (not coverage gap)` — the live ledger still drives coverage, but the prompt's phase mapping has missed an opportunity to give targeted treatment.
    - **Names this prompt mentions in code-fenced examples or numbered steps but absent from the catalog** → P1 FAIL under *MCP server issues* with category `prompt drift`. The prompt is referencing a removed/renamed surface and would mislead a cold-context reader.
    - When a separate `/surface-audit` skill is available in the host, prefer delegating this diff to it (one structured table back) rather than re-walking the catalog from scratch in the main agent.

**MCP audit checkpoint:** Did `workspace_load` succeed? Does `workspace_list` show the session? Did `dotnet restore` complete (or was step 10 marked `blocked` with a reason)? Did `workspace_health` return `healthy` (Phase -1 step 5)? Were isolation path, repo shape, debug-log channel, and seeded ledger/scorecard/baseline tables all captured? Did drift detection (step 14) produce its two output buckets (or report "none")? On total load failure, mark workspace-scoped rows `blocked` and continue only with workspace-independent families (`server_info`, server resources, `analyze_snippet`, `evaluate_csharp`, prompts the client can render offline).

---

### Phase 0.5: Subagent dispatch plan (MANDATORY for `--full`, skip when `--single-agent`)

The full tier exercises 250+ MCP tool calls across 19 phases. A single agent that tries to run them all in one context will burn through its window long before Phase 19 and silently truncate to a "representative probe" — that is a contract violation of the `--full` tier, not a feature. The fix is structural: the **orchestrator owns coordination** (workspace lifecycle, worktree lifecycle, report writes, finding emission) and **dispatches phase groups to `audit-phase-runner` subagents** that each return a compact structured summary the orchestrator pastes into the report.

**Orchestrator-owned phases (never dispatched):**

- Phase -1, 0 — already complete by the time you reach this section.
- Phase 6 **setup** (`git worktree add`, branch creation) and **teardown** (`dotnet build-server shutdown` → `git worktree remove --force`). The `try/finally` discipline must be owned by a single, surviving caller — a crashed subagent cannot leak the worktree.
- Phase 18 — depends on synthesizing prior phase outputs.
- Phase 19 — finding emission (routing decision via `Test-IsMaintainer`, `gh issue create` calls).
- All writes to `audit-reports/<timestamp>_<repo-id>_mcp-server-surface-test.md` and `_latest-promotion-scorecard.json`. Subagents return structured summaries; the orchestrator alone touches the files (no concurrent-writer hazard).

**Subagent dispatch groups (run via `audit-phase-runner`, parallel where independent):**

Group all dispatches in a single message with multiple `Agent` tool-use blocks where the listed groups are independent. Phases inside a group are run by the same subagent in sequence so they can share intermediate analysis (e.g., the diagnostic IDs Phase 1 surfaces feed the metric thresholds in Phase 2).

| Group | Phases | Parallel-safe with | Notes |
|---|---|---|---|
| **G1** | 1, 2 | G2, G3, G5 | diagnostics + metrics — read-only, workspace-scoped, no shared state with other groups |
| **G2** | 3, 4 | G1, G3, G5 | symbol + flow analysis on selected types/methods |
| **G3** | 5, 9 (Phase 9 if Phase 10 already complete) | G1, G2, G5 | snippet/script + undo verification |
| **G4** | 6 sub-phases (after orchestrator creates worktree) | — | runs serially against the disposable worktree; orchestrator passes the worktree path; subagent must NOT call `git worktree remove` |
| **G5** | 7, 8, 8b | G1, G2, G3 | configuration + build/test + concurrency stress; the heaviest group, give it the biggest context budget |
| **G6** | 10, 11, 12 | G7 | file/cross-project ops + semantic search + scaffolding |
| **G7** | 13, 14 | G6 | project mutation + navigation/completions |
| **G8** | 15, 16, 17 | — | resources + prompts + negative testing; run after G6/G7 so the coverage ledger is mostly populated |

**Dispatch contract per group:**

Each subagent prompt must include: (a) the workspace `workspaceId` (already loaded — subagents reuse the orchestrator's MCP session, no `workspace_load` needed), (b) the audit report path (read-only — for context only, do not write), (c) the disposable worktree path when relevant (G4 only), (d) the explicit phase numbers + the relevant prompt section excerpt, (e) "return a compact structured summary in <RESULT> envelope" instruction. The orchestrator pastes each returned summary into the report under the matching phase heading.

**G2 data passthrough (Phase 2 → Phase 3/4):** When dispatching G2, the orchestrator MUST also paste Phase 2's `get_complexity_metrics` result (or a compact projection of it: list of `{ symbol, kind, cyclomatic }` entries sorted descending by cyclomatic score) into the subagent brief. Phase 3 and Phase 4 selection rules ("top-N by cyclomatic score") require this data — without it, the subagent falls back to alphabetical selection (functionally correct, less targeted). If G1 was skipped or its Phase 2 result was empty, paste an explicit `complexity: none` marker so the subagent knows to use the alphabetical fallback rather than ask.

**Completion gate (load-bearing — replaces the soft `skipped-budget` fallback):**

Any subagent that returns `skipped-budget`, `skipped-context`, `truncated`, or any equivalent self-imposed-limit marker for a phase is a **hard FAIL** of the `--full` contract. The orchestrator must either (a) re-dispatch that phase to a fresh subagent with a smaller scope, or (b) record `phase-failed-budget` in the coverage ledger and surface it in the report's *Coverage summary* as a P1 audit defect. Silent truncation labeled as "representative probe" is no longer an acceptable outcome — `--full` means full or it means honest failure with a named cause.

**Escape hatch — `--single-agent`:**

When the SKILL receives `--single-agent`, skip this dispatch plan and run all phases in the orchestrator's own context. The operator has accepted the truncation tradeoff explicitly. The completion gate above still applies — `skipped-budget` markers must surface as P1 audit defects in the coverage summary, not get buried in the ledger.

**MCP audit checkpoint:** Has the orchestrator decided which groups it will dispatch (default) or recorded `--single-agent` in the report header? Are subagent prompts ready to send with the workspaceId, report path, and per-group phase scopes?

---

### Phase 1: Broad diagnostics scan

1. `project_diagnostics` (no filters) for the solution-wide picture.
2. `project_diagnostics` with at least one non-default selector (project / file / severity / `offset+limit` pagination). **v1.8+ invariant:** `TotalErrors`/`TotalWarnings`/`TotalInfo` are invariant under `severityFilter` — only the returned arrays narrow. Zero-collapsed filtered totals are a regression.
3. `compile_check` (default `offset=0, limit=50`) — compare against `project_diagnostics`. Probe `severity=Error` and `file=<path>`.
4. `compile_check` with `emitValidation=true` (same pagination). Compare timing and diagnostics. With unrestored packages the emit phase short-circuits — run `dotnet restore` first before flagging.
5. `security_diagnostics` (OWASP-tagged).
6. `security_analyzer_status` — which analyzer packages are installed / missing.
7. `nuget_vulnerability_scan`. Requires .NET 8+ SDK and network access. Cross-reference with `security_diagnostics` (CVE vs source patterns).
8. `list_analyzers` — total rule count; note any LOAD_ERROR entries.
9. If the live schema exposes paging/filtering for `list_analyzers`, probe a non-default path.
10. `diagnostic_details` on one representative error and one warning.

**MCP audit checkpoint:** Do diagnostic tools agree on counts? Does the non-default `project_diagnostics` path preserve invariant totals? Does `emitValidation=true` find anything extra without hanging? Does `nuget_vulnerability_scan` fail cleanly when offline? Any analyzers fail to load? Are `diagnostic_details` locations accurate?

---

### Phase 2: Code quality metrics

1. `get_complexity_metrics` (no filters). Flag cyclomatic > 10 or nesting > 4.
2. `get_cohesion_metrics(minMethods=3)` to find types with LCOM4 > 1. v1.8+ ignores `[LoggerMessage]` / `[GeneratedRegex]` source-gen partials; `SharedFields` should contain only real field/property names.
3. `get_coupling_metrics` if exposed — note if the tool returns "No such tool" (open backlog row `coupling-metrics-tool`).
4. `find_unused_symbols(includePublic=false)`.
5. `find_unused_symbols(includePublic=true)` — public APIs with zero internal references?
6. `find_duplicated_methods` and `find_duplicate_helpers` — cross-check output against reads of the flagged locations; note any false positives (the BCL-wrapper false positive is tracked as `find-duplicate-helpers-framework-wrapper-false-positive`).
7. `find_duplicated_code` — token-stream-level duplication (broader than `find_duplicated_methods`); spot-check 2–3 reported clusters against actual source ranges.
8. `find_dead_locals` on a few chosen methods (complements `find_unused_symbols`'s symbol-level scope).
9. `find_dead_fields` — class-field-level dead detection; complements `find_unused_symbols(includePublic=false)` with finer granularity at private/internal field scope.
8. `get_namespace_dependencies` — circular dependencies?
9. `get_nuget_dependencies` — audit package references.
10. `suggest_refactorings` — ranked aggregation across complexity / cohesion / dead code. Do the recommended tool sequences match the actual tools for each category?

**MCP audit checkpoint:** Are complexity scores plausible? Are LCOM4 scores sane (score=1 types actually cohesive)? Are source-gen partials correctly excluded from LCOM4? Does `find_unused_symbols` miss obvious dead code or falsely flag used symbols? Do `find_duplicated_methods` / `find_duplicate_helpers` produce an acceptable false-positive rate? Does `suggest_refactorings` rank sensibly?

---

### Phase 3: Deep symbol analysis (pick 3–5 key types)

**Selection rule (deterministic, do not ask the operator):** Select types by descending cyclomatic score from Phase 2's `get_complexity_metrics` result — take the top 3–5. If Phase 2 did not run or returned no results, fall back to the first 3–5 type names in alphabetical order from `document_symbols` on the primary project's root namespace. Subagents dispatched for this phase must never call `AskUserQuestion` to disambiguate — apply the rule, record the resulting type list in the summary, and proceed.

For each key type:

1. `symbol_search` to locate by name.
2. `symbol_info` for metadata.
3. `document_symbols` on its file.
4. `type_hierarchy`.
5. `find_implementations` on any interface/abstract type discovered. Verify completeness.
6. `find_references`.
7. `find_consumers` — dependency-kind classification.
7b. `find_type_consumers` on the same type — type-scoped consumer enumeration; cross-check against `find_consumers`. Discrepancies between symbol-scoped and type-scoped surfaces are FLAG worthy.
8. `find_shared_members` — private members shared across public methods.
9. `find_type_mutations`. v1.8+ classifies each mutating member by `MutationScope` (`FieldWrite` / `CollectionWrite` / `IO` / `Network` / `Process` / `Database`). Types whose whole purpose is IO (e.g. a snapshot store) should now report their `WriteAllText` / `Delete` methods with `MutationScope=IO`, even without instance-field reassignment.
10. `find_type_usages` — return types, parameters, fields, casts.
11. `callers_callees` on 2–3 methods.
12. `find_property_writes` on settable properties (init vs post-construction).
13. `member_hierarchy` on overrides/implements.
14. `symbol_relationships`. v1.7+ auto-promotes a return-type-token caret to the enclosing member (`preferDeclaringMember=true` default). Point the locator at the return-type token of a known method and assert the result describes the **method**, not the type.
15. `symbol_signature_help`. Auto-promotion applies; pass `preferDeclaringMember=false` once to confirm literal-token resolution still works when requested.
16. `impact_analysis` on a refactor candidate. Also try `probe_position` at the same cursor to cross-check position resolution.
17. `symbol_impact_sweep(metadataName | filePath+line+column)` — verify `references` / `nonExhaustiveSwitches` / `mapperCallsites` / `suggestedTasks` buckets. For properties (v1.18+) also expect `persistenceLayerFindings` with `To*`/`From*` mapper symmetry checks.

**MCP audit checkpoint:** Does `find_implementations` return all concrete implementations? Are `find_references` and `find_consumers` consistent? Does `find_type_usages` categorize correctly? Do `callers_callees` match what you'd expect from reading code? Does `symbol_relationships` combine data correctly? Does `impact_analysis` produce a reasonable blast radius? Does `symbol_impact_sweep.references` match `find_references` on the same symbol? Does `probe_position` agree with the position your locator used?

---

### Phase 4: Flow analysis (pick 3–5 complex methods)

**Selection rule (deterministic, do not ask the operator):** Select methods by descending cyclomatic score from Phase 2's `get_complexity_metrics` result with score > 5 — take the top 3–5. If Phase 2 did not run, returned no scores above the threshold, or returned no results at all, fall back to the first 3–5 method names in alphabetical order from `document_symbols` on the primary project's root namespace. Subagents dispatched for this phase must never call `AskUserQuestion` to disambiguate — apply the rule, record the resulting method list in the summary, and proceed.

For each:

1. `get_source_text` to read the file.
2. `analyze_data_flow` on the method body. **v1.8+:** expression-bodied members (`=> expr`) are supported — point the line range at the arrow or its expression and the resolver lifts the expression.
3. `analyze_control_flow` on the same range. **v1.8+:** for expression-bodied members the result is synthesized (`Succeeded=true, StartPointIsReachable=true, EndPointIsReachable=false`, one synthetic return at the arrow).
4. `get_operations` on key expressions (assignments, invocations, conditionals).
5. `get_syntax_tree` on the method's line range — cross-check against the IOperation view.
6. `trace_exception_flow` on a `throw` site inside the method (or on a known `catch` that handles downstream throws). Verify the response walks the control-flow edges and locates the nearest `catch` (or confirms an unhandled-at-boundary exit); cross-check against `find_references` on the exception type.

**MCP audit checkpoint:** Does `analyze_data_flow` identify variables correctly, including `Captured` / `CapturedInside` for lambdas? Does `analyze_control_flow` correctly detect unreachable code and synthesize expression-body results? Does `get_operations` produce a sensible tree? Does `trace_exception_flow` produce a coherent throw→catch path or a clear unhandled-at-boundary signal?

---

### Phase 5: Snippet & script validation

`analyze_snippet` / `evaluate_csharp` complete within the script timeout unless `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` is raised. Multi-minute hangs that only clear on user message are principle-#9 territory (unknown attribution).

1. `analyze_snippet(kind="expression")` on `1 + 2` — no errors.
2. `analyze_snippet(kind="program")` on a small class — declared symbols listed.
3. `analyze_snippet(kind="statements")` on intentionally broken code (e.g. `int x = "hello";`). Verify CS0029 and that `StartColumn` is user-relative (~9), not wrapper-relative (pre-fix this was 66 — FLAG-C, fixed v1.7+).
4. `analyze_snippet(kind="returnExpression")` on `return 42;` — value-bearing return allowed; compare to `kind="statements"` rejecting returns by design (FLAG-007 documented behaviour).
5. `evaluate_csharp("Enumerable.Range(1, 10).Sum()")` → 55.
6. `evaluate_csharp` on a multi-line script.
7. `evaluate_csharp` on code that throws at runtime (`int.Parse("abc")`) — error reported gracefully.
8. `evaluate_csharp` on an infinite loop — timeout fires, server does not hang. Expect at most one call lasting until the configured timeout.

**MCP audit checkpoint:** Do the different `kind` values wrap correctly? Are compile errors accurate and well-formatted? Does `evaluate_csharp` handle runtime errors and timeouts cleanly?
