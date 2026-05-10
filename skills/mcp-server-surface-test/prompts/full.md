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

This prompt describes one canonical run. Phase 6 applies are always exercised against a disposable worktree the prompt creates at run start and tears down at run end (`dotnet build-server shutdown` + `git worktree remove --force`, in that order). The promotion scorecard is always emitted. Typical duration: 90–180 min.

A single optional flag exists: `--no-worktree`, a degraded mode for environments that genuinely cannot create a git worktree (tight CI sandbox, missing `git` binary, read-only checkout). When set, Phase 6 is skipped, the *Isolation* row records `degraded — --no-worktree flag, Phase 6 applies skipped`, and writer rows whose round-trip evidence depended on the disposable worktree default to `needs-more-evidence` in the scorecard. Record `--no-worktree` in the report header so consumers know which evidence is missing.

**Known issues / prior findings.** When the audited repo has a tracked backlog or issue history, cross-check it and cite matching ids — common locations include `<audited-repo-root>/backlog.md`, a project's GitHub Issues, or a prior audit report. If no prior source exists, the regression section is **N/A**.

**Phase order.** Run in this order: **-1 → 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 8b → 10 → 9 → 11 → 12 → 13 → 14 → 15 → 16 → 17 → 18**. Phase 8b runs immediately after Phase 8 so it sees post-refactor state. Phase 9 runs after Phase 10 so `revert_last_apply` doesn't undo Phase 6 work.

**Portability and completeness contract.**

1. Runs against **any loadable C# repo**. Prefer `.sln` / `.slnx`; fall back to `.csproj`.
2. The disposable worktree is created at run start and recorded in the *Isolation* header row before any write-capable call. `--no-worktree` opts into degraded mode where Phase 6 applies are skipped — record the rationale in the *Isolation* row.
3. `server_info`, `roslyn://server/catalog`, and `roslyn://server/resource-templates` are the **authoritative live surface**. Any disagreement with prose in this prompt: the live catalog wins, and the drift is itself a finding.
4. Build a live **coverage ledger** from the catalog. Every live tool, resource, and prompt ends with exactly one final status: `exercised`, `exercised-apply`, `exercised-preview-only`, `skipped-repo-shape`, `skipped-safety`, or `blocked`. Silent omissions mean the audit is incomplete. Columns: `kind`, `name`, `tier`, `category`, `status`, `phase`, `lastElapsedMs`, `notes`.
5. If the MCP client cannot invoke a live resource/prompt family, mark those rows `blocked` with the client limitation. Blocked experimental entries score `needs-more-evidence` in the scorecard — never `promote`.
6. Record repo-shape constraints up front: single vs multi-project, tests, analyzers, source generators, DI, `.editorconfig`, Central Package Management, multi-targeting, network/restore constraints.
7. Do not invent applicability. If the repo has no tests, no DI, no source generators, no multi-targeting, or only one project, record that and mark dependent steps `skipped-repo-shape`.
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
2. **Create the disposable worktree** (mandatory, default mode). Run `git worktree add ../<repo-name>-surface-test-<ts> -b mcp-server-surface-test/<ts>` from the audited repo root, where `<ts>` is the same UTC `yyyyMMddTHHmmssZ` used for the report filename. Record the absolute worktree path + branch name in the *Isolation* header row before any write-capable call. Phase 6's preview→apply chains run against this checkout; the audited repo's primary working tree is never touched. **`--no-worktree` flag:** record `degraded — --no-worktree flag, Phase 6 applies skipped` in the *Isolation* row and skip worktree creation entirely.
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

---

### Phase 6: Apply-tool exercise on the disposable worktree

**Apply-mode mutations happen here only**, and only inside the disposable worktree created in Phase 0 step 2. Phases 10 / 12 / 13 also drive at least one preview→apply round-trip per applicable family (against the same disposable worktree). Skip Phase 6 only when `--no-worktree` was passed; in that case state **N/A — skipped per --no-worktree flag** in the Phase 6 report section and proceed.

**The point of Phase 6 is to exercise the write path of the MCP server**, not to ship product changes. Applies are test fixtures: drive preview→apply→revert chains, verify behaviour with `compile_check` / `build_workspace` / `test_run` after each apply, and capture per-call evidence for the promotion scorecard. The disposable worktree is torn down at run end (see *Phase 6 teardown* below); nothing about Phase 6 produces a PR or a commit in the audited repo's history.

**`try/finally` discipline.** Wrap the entire Phase 6 sub-phase chain in a `try/finally` (or your host's equivalent error-handling structure) so teardown runs even if an apply fails mid-chain. The skill's correctness contract is "the disposable worktree is gone when the run ends" — apply failures are evidence to record in the report, not reasons to leave the worktree behind.

#### 6a. Fix All
1. Pick a diagnostic with many occurrences (IDE0005, CS8600).
2. `fix_all_preview(scope="solution", diagnosticId=…)`.
3. Inspect — all instances found? Probe a non-default scope (e.g. `scope="project"`).
4. `fix_all_apply(previewToken)`.
5. `compile_check`.

#### 6b. Rename
1. Pick a poorly-named symbol.
2. `rename_preview` with a better name.
3. `rename_apply`. Verify the response's `MutatedSymbol` (v1.28+) carries a fresh handle for the renamed identity — chain downstream calls off `MutatedSymbol.symbolHandle` instead of re-resolving by the new name.
4. `compile_check`; `find_references` on the new name — count matches preview.

#### 6c. Extract interface (when a consumer-heavy type was found in Phase 3)
1. `extract_interface_preview` with selected public members.
2. `extract_interface_apply`.
3. `bulk_replace_type_preview` to update consumers from concrete → interface.
4. `bulk_replace_type_apply`.
5. `compile_check`.

#### 6d. Extract type (when Phase 2 found LCOM4 > 1)
1. `find_shared_members`.
2. `extract_type_preview` with the independent cluster's members.
3. `extract_type_apply`.
4. `compile_check`.

#### 6e. Format & organize
1. `format_range_preview` / `format_range_apply` on a recently-edited region.
2. `format_document_preview` / `format_document_apply` on a heavily modified file.
3. `organize_usings_preview` / `organize_usings_apply` on files touched by code fixes.
4. `format_check` (solution-wide format verification) to confirm clean.

#### 6f. Curated code fixes
1. `code_fix_preview` on a specific diagnostic occurrence.
2. `code_fix_apply`.
3. `compile_check`.

#### 6f-ii. Diagnostic suppression
1. Pick a deliberate-pattern warning.
2. `set_diagnostic_severity` to downgrade the severity in `.editorconfig`.
3. `add_pragma_suppression` to insert `#pragma warning disable` at a specific site. After `apply`, call `verify_pragma_suppresses` on the site to confirm the pragma covers the intended diagnostic; if the scope is wrong, `pragma_scope_widen` to extend it.
4. `compile_check`.

#### 6g. Code actions
1. `get_code_actions` at a position with available refactorings.
2. `preview_code_action`.
3. `apply_code_action`.

#### 6h. Direct text edits
1. `apply_text_edit` — one small targeted edit.
2. `apply_multi_file_edit` — coordinated edits across two files.
3. `preview_multi_file_edit(fileEdits=[…])` — verify per-file diffs + one token; `preview_multi_file_edit_apply(previewToken)` to commit. **Negative probe:** mutate the workspace via a separate `format_document_apply`, then retry the now-stale token — expect a "workspace moved, regenerate preview" rejection.
4. `compile_check`.

#### 6i. Dead code removal
1. From Phase 2 `find_unused_symbols` results, pick confirmed dead symbols.
2. `remove_dead_code_preview`.
3. `remove_dead_code_apply`.
4. `compile_check`.
5. If any dead **interface** members showed up, exercise `remove_interface_member_preview(interfaceMemberHandle)`. Expect `previewToken` + `implementationCount` + implementation list, or `status=refused` with `externalCallers` populated.

#### 6j. Extract method
1. Pick a target from `suggest_refactorings` or `get_complexity_metrics` (complexity ≥ 10).
2. `analyze_data_flow` on the target range.
3. `extract_method_preview` with a descriptive name.
4. Verify the preview: parameters, return type, call site.
5. `extract_method_apply`; `compile_check`; `find_references` on the new method — ≥1 call site?

#### 6k. Advanced refactor previews (experimental; promotion-relevant)
1. **`restructure_preview`.** Pick a small idiom to normalize (e.g. `Task.Run(() => __expr__)` → `await __expr__`). Build pattern + goal as C# fragments with `__name__` placeholders. Preview on one file first, then project-wide. Apply via `preview_multi_file_edit_apply` on the returned token.
2. **`replace_string_literals_preview`.** Find a magic string in argument/initializer position (avoid XML docs / nameof / interpolation holes). Apply via `preview_multi_file_edit_apply`.
3. **`change_signature_preview`.** Pick a method with multiple callsites. Exercise `op=add`, `op=remove`, `op=rename` in separate previews. For `op=add`, verify the default value is spliced at every callsite (named-arg callers get a named argument). Apply via `preview_multi_file_edit_apply` (shared `IPreviewStore`; do **not** use `apply_composite_preview`). **Negative probe:** `op=reorder` must return an actionable unsupported-op error that points at `symbol_refactor_preview`. **Known limitation — backlog `change-signature-preview-callsite-summary` (P3):** preview `changes[].unifiedDiff` only enumerates the declaration-owner file; apply correctly rewrites every callsite. Verify post-apply with `compile_check` + `find_references`, not the preview diff. Do NOT re-raise.
4. **`symbol_refactor_preview`.** Compose rename + edit + restructure in one `operations` array (max 25 / 500 affected files). Verify each op sees rewritten state from earlier ops. Apply via `preview_multi_file_edit_apply` (shared `IPreviewStore`; **not** `apply_composite_preview`, which reads `ICompositePreviewStore` — a different store used by `extract_and_wire_interface_preview` / `class_split_preview` / `migrate_package_preview`).
5. **`change_type_namespace_preview`.** Preview moving a type's namespace while keeping its file location.
6. **`replace_invocation_preview` / `preview_record_field_addition` / `record_field_add_with_satellites_preview` / `extract_shared_expression_to_helper_preview` / `split_service_with_di_preview`.** Exercise each if the repo shape allows; mark `skipped-repo-shape` otherwise. Verify the returned preview token is accepted by the apply tool named in the tool description.

#### 6l. Atomic apply-with-verify
1. Produce a known-good preview via `organize_usings_preview`; pass the token to `apply_with_verify(previewToken, rollbackOnError=true)`. Assert `status=applied`.
2. Produce a known-bad preview (for example, `extract_method_preview` over a region that would introduce CS0136). On post-v1.15 repos the fix landed and this yields `status=applied`; on upstream regressions expect `status=rolled_back` with `introducedErrors` populated.

#### 6m. Session change tracking
1. After all Phase 6 applies, `workspace_changes` — verify every applied refactoring appears with correct descriptions, affected files, tool names, and timestamps; verify ordering.

#### 6z. Disposable worktree teardown (mandatory, runs in `finally`)

This sub-phase runs at the **end of Phase 6** as the `finally`-clause counterpart to the `try/finally` wrapping the Phase 6 chain. It also runs after Phases 10 / 12 / 13 if those phases issued additional applies inside the disposable worktree.

1. **Release Windows file locks first.** Run `dotnet build-server shutdown` from the audited repo root (or the disposable worktree, equivalent). This releases `testhost.exe` / `VBCSCompiler.exe` locks on `bin/{Debug,Release}/net*/` directories. The command prints one informational line on stdout — that is normal, not an error.
2. **Remove the worktree.** Run `git worktree remove --force <disposable-worktree-path>` from the audited repo root. The `--force` flag is required because Phase 6 leaves uncommitted apply-mode mutations in the worktree (intentionally — the point was to exercise the apply tools, not to commit their output).
3. **Verify cleanup.** Run `git worktree list` from the audited repo root and confirm the disposable worktree is gone. Run `git status` from the audited repo's primary checkout and confirm it is clean (Phase 6 must not have leaked changes outside the worktree).
4. **Branch cleanup.** Run `git branch -D mcp-server-surface-test/<ts>` from the audited repo root to delete the disposable branch. The branch only existed to host worktree state; it has no upstream and no history worth preserving.
5. **Record teardown outcome in the report header.** A new *Teardown* row: `clean` (worktree removed, branch deleted, primary checkout clean) / `partial — <what survived>` (e.g. `partial — branch survived; manual git branch -D required`) / `failed — <error>`.

If teardown fails for an unexpected reason, surface the failure in the report's *MCP server issues* section as a P1 finding tagged `surface-test teardown`. Do not retry blindly — the operator can clean up by hand.

**`--no-worktree` mode:** sub-phase 6z is `skipped — no worktree was created`.

**MCP audit checkpoint:** Does `fix_all_preview` find all instances without timing out? Does `rename_preview` catch references in comments/strings? Does `rename_apply.MutatedSymbol` resolve to the new identity? Does `bulk_replace_type_preview` miss any usages? Does `extract_type_preview` handle shared private members correctly? Does `format_range_preview` stay inside its range? Does `format_check` correctly report clean after the preceding format applies? Does `remove_dead_code_preview` touch only the targeted symbols? Does `set_diagnostic_severity` correctly create/update `.editorconfig`? Does `add_pragma_suppression` insert at the right line? Does `verify_pragma_suppresses` correctly validate scope? Does `extract_method_preview` infer correct parameters and return type? Do the advanced refactor previews each round-trip cleanly and return actionable errors on unsupported ops? Does `apply_with_verify` roll back cleanly on introduced errors? Does `workspace_changes` list every apply with correct ordering?

**Cross-tool chain validation.** After `rename_apply`: `find_references` on the new name = preview count. After `extract_interface_apply`: `type_hierarchy` on the new interface shows the implementor. After `fix_all_apply`: `project_diagnostics` on that diagnostic id = 0. After `organize_usings_apply`: `get_source_text` shows sorted usings. After `extract_method_apply`: `find_references` on the new method ≥ 1. After all applies: `workspace_changes` entry count matches the apply count.

**Mutation-family coverage.** By end of Phase 6 plus Phases 10 / 12 / 13, every write-capable family must be either exercised end-to-end or explicitly `skipped-safety` / `skipped-repo-shape`. A preview-only call does not cover an apply sibling unless the catalog exposes no separate apply tool.

---

### Phase 7: EditorConfig & MSBuild configuration

1. `get_editorconfig_options` on a source file. Do the returned options match `.editorconfig`?
2. `set_editorconfig_option` to set a benign key (e.g. `dotnet_sort_system_directives_first = true`). Verify the file was created/updated.
3. `get_editorconfig_options` again — change reflected?

#### 7b. MSBuild evaluation
1. `get_msbuild_properties` — verify key properties (`TargetFramework`, `RootNamespace`, `OutputType`).
2. `evaluate_msbuild_property(TargetFramework)` — matches step 1?
3. `evaluate_msbuild_items(itemType=Compile)` — reasonable count and paths?

**MCP audit checkpoint:** Are editor-config values accurate? Does `set_editorconfig_option` write the pair correctly? Are MSBuild property/item values consistent across the three tools?

---

### Phase 8: Build & test validation

Delegate heavy validation where possible (full-suite `test_run`, `test_coverage`, shell fallbacks). Keep selection (`test_discover`, `test_related_files`, `test_related`) in the primary agent.

1. `workspace_reload` — refresh post-Phase-6.
2. `build_workspace` — full MSBuild build.
3. `build_project` for individual projects you modified.
4. `test_discover` — find all tests.
5. `test_related_files` with the list of files you modified.
6. `test_related` on a symbol you refactored.
7. `test_run` with the filter from step 5.
8. `test_run` with no filter — full suite.
9. `test_coverage`.
10. `test_reference_map(projectName?)` — verify `{ coveredSymbols, uncoveredSymbols, coveragePercent, inspectedTestProjects, notes, mockDriftWarnings? }`. For repos using NSubstitute, check `mockDriftWarnings` flags interface methods production calls that the matching test class never stubs. For repos with no test project, the response should be a clean empty-with-reason result, not an error.
10b. `get_test_coverage_map(projectName?)` — production-symbol → covering-test-method map. Cross-check that any production symbol classified as `covered` in `test_reference_map` has at least one entry in `get_test_coverage_map`; an empty map for a `covered` symbol is a FLAG.
11. `validate_workspace(changedFilePaths=null, runTests=false)` — verify `overallStatus ∈ {clean, compile-error, analyzer-error, test-failure}`. Probe `changedFilePaths=null` to confirm auto-scoping off `IChangeTracker`. Probe `runTests=true` on the disposable worktree. **Negative probe:** fabricated `changedFilePaths` entry → clean "no related tests" result (not a crash).
12. `validate_recent_git_changes` — if git metadata is accessible, validate the last commit's touched files. Verify the bundle composes `compile_check` + diagnostics + related-tests correctly and reports a clean status on a passing commit.

If `test_discover` returns zero, record it and distinguish: `test_run` returns a clean zero-test result, `test_run` / `test_coverage` are `skipped-repo-shape`, or the server mishandles the no-test case.

**MCP audit checkpoint:** Does `build_workspace` match `compile_check` (same count / ids / locations)? Does `test_related_files` identify related tests correctly? Does `test_run` produce structured pass/fail — aggregated across projects, not just the last assembly? Does `test_coverage` produce coverage data? Does `test_reference_map.coveragePercent` agree roughly with `test_coverage`? Does `validate_workspace` produce a coherent `overallStatus`? Does `validate_recent_git_changes` succeed on a clean commit?

**Do not** call `revert_last_apply` yet — it would undo the last Phase 6 apply. Continue to Phase 8b, then Phase 10, then Phase 9.

---

### Phase 8b: Concurrency audit (per-workspace RW lock)

**Stability note.** Many AI hosts serialize MCP tool calls or cannot attribute wall-clock across truly parallel requests. That is expected. If true concurrency is unavailable, mark 8b.2 / 8b.3 / timing-sensitive parts of 8b.4 `blocked` — *client cannot issue concurrent tool calls* (or `skipped-safety` if parallel would be unsafe). Record once in the header; do not force speedup ratios against `tests/RoslynMcp.Tests/Benchmarks/WorkspaceReadConcurrencyBenchmark.cs`. Still run 8b.1 sequential baselines using `_meta.elapsedMs`.

**Single lock model.** One per-workspace `AsyncReaderWriterLock` via `WorkspaceExecutionGate`. `_rw-lock_` / `_legacy-mutex_` in old audit filenames are historical artifacts only.

**Purpose.** Produce a machine-readable concurrency matrix. Expected behaviour: reads overlap subject to the global throttle; writes are exclusive against in-flight readers; `workspace_close` / `workspace_reload` wait for in-flight readers.

#### 8b.0 Probe set (record in the report)

| Slot | Suggested probe (substitute equivalents) | Classification |
|---|---|---|
| R1 | `find_references` on a hot symbol used 50+ times | reader |
| R2 | `project_diagnostics` (no filters) | reader |
| R3 | `symbol_search` with >100 hits | reader |
| R4 | `find_unused_symbols(includePublic=false)` | reader |
| R5 | `get_complexity_metrics` (no filters) | reader |
| W1 | `format_document_preview` → `format_document_apply` on a Phase-6-touched file | writer |
| W2 | `set_editorconfig_option` with a benign key (then revert) | writer |

#### 8b.1 Sequential baseline
Call each R1–R5 once sequentially; record `_meta.elapsedMs`. Record any structured MCP log entries (correlation ids, gate warnings, rate-limit hits, timeouts).

#### 8b.2 Parallel fan-out
`N = min(4, max(2, Environment.ProcessorCount))`. The global throttle is `max(2, Environment.ProcessorCount)` (`src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`). Record the host core count + chosen N.
- Issue R1 ×N, R2 ×N, R3 ×N in parallel. Record wall-clock + speedup (`N × baseline / parallel`). Expected: between `0.7 × N` and `N`. < `0.7 × N` is a FLAG.
- If the client serializes calls: `blocked — client serializes tool calls`.

#### 8b.3 Read/write exclusion probe
- Start R1; while in flight, issue W1. Expected: W1 waits.
- Inverse: start W1; then issue R1. Expected: R1 waits.
- Any deviation is a FLAG.

#### 8b.4 Lifecycle stress
- Start R2; while in flight, call `workspace_reload`. Label `waits-for-reader` / `runs-concurrently` / `errors`. Record whether the reader returned stale/fresh/error.
- Start R3; call `workspace_close`. Expected: reader completes cleanly; close waits. Record the post-acquire `EnsureWorkspaceStillExists` behaviour.
- After close, `workspace_load` again for the remaining phases.

#### 8b.5 Writer reclassification

| # | Tool | Verification |
|---|---|---|
| 1 | `apply_text_edit` | Trivial edit on a Phase-6-touched file; content changes; `compile_check` passes. |
| 2 | `apply_multi_file_edit` | Coordinated edit across two files; both change; `compile_check` passes. |
| 3 | `revert_last_apply` | After row 1 or 2; prior edit reverted. *Shares evidence with Phase 9 audit-only revert.* |
| 4 | `set_editorconfig_option` | Set benign key; `get_editorconfig_options` reflects it. |
| 5 | `set_diagnostic_severity` | Set `dotnet_diagnostic.<id>.severity = suggestion`; appears in `.editorconfig`. |
| 6 | `add_pragma_suppression` | Insert `#pragma warning disable <id>` before a known diagnostic line; `get_source_text` shows the pragma. |

Writers should be measurably slower than the reader baseline — they mutate disk.

#### 8b.6 Concurrency matrix output
Use the schema in *Output Format → Concurrency matrix*. Every cell has a value or `N/A` (with a one-line reason).

**MCP audit checkpoint:** If 8b.2 was client-blocked, say so and answer **N/A — client serializes tool calls** for speedup / stability / benchmark-comparison prompts. Otherwise: did parallel reads overlap ≥ `0.7 × N`? Did the read/write probes match the documented contract? Did lifecycle stress reveal TOCTOU / stale issues? Did all six writers complete? Any gate-contention / rate-limit / deadlock entries? Is `parallel_speedup` stable across runs within ±15%?

---

### Phase 10: File, cross-project, and orchestration operations

**Default:** inspect previews for every applicable family on the disposable worktree, and drive at least one safe preview → apply → verification → cleanup chain per family that exposes an apply sibling. Under `--no-worktree`, apply siblings are `skipped-safety — --no-worktree`.

1. `move_type_to_file_preview` — move a type into its own file.
2. `move_file_preview` — move with namespace update.
3. `create_file_preview` — new source file.
4. `delete_file_preview` — unused source file (safe target).
5. Multi-project: `extract_interface_cross_project_preview`, `dependency_inversion_preview`, `move_type_to_project_preview`.
6. If DI present: `extract_and_wire_interface_preview`.
7. If a split candidate: `split_class_preview` and/or `split_service_with_di_preview`.
8. If a package-migration candidate: `migrate_package_preview`.
9. On the disposable worktree: one safe file/type preview → apply → cleanup loop with `move_type_to_file_apply` / `create_file_apply` / `move_file_apply` / `delete_file_apply`.
10. **`apply_composite_preview` — destructive despite the name.** Only call on the disposable worktree; under `--no-worktree`, mark `skipped-safety — --no-worktree` and note the naming friction.

**MCP audit checkpoint:** Do the previews produce valid diffs with correct namespace/reference updates? Does `extract_and_wire_interface_preview` correctly identify DI registrations? Does `split_class_preview` produce valid partial classes? Does `split_service_with_di_preview` produce valid DI rewires? Does the create/move/delete/apply round-trip leave the repo clean?

---

### Phase 9: Undo verification (run after Phase 10)

**Why after Phase 10:** `revert_last_apply` immediately after Phase 8 would undo the last Phase 6 apply. Add one audit-only apply on top, then revert.

1. Perform exactly one low-impact Roslyn apply whose reversal is safe — `format_document_preview` → `format_document_apply` on a single file is the canonical probe (prefer one already touched in Phase 6). This becomes the new top of the undo stack.
2. `revert_last_apply` — only the audit-only apply is undone; Phase 6 changes remain.
3. `compile_check`.
4. **`revert_apply_by_sequence` — non-tip rollback.** Add a SECOND audit-only apply on top of the same Phase-6 stack (a different `format_document_apply`-style probe), then call `revert_apply_by_sequence(sequenceNumber=<the-first-audit-only-apply>)`. Verify only the targeted entry is undone; the second audit-only apply remains. Negative probe: pass an out-of-range or already-reverted sequence number — expect a clear error, not silent success. After verification, `revert_last_apply` to clear the surviving audit-only entry.
5. `compile_check`.
6. Only Roslyn solution-level changes are revertible. If `revert_last_apply` / `revert_apply_by_sequence` error or no-op, document it.

**MCP audit checkpoint:** Does `revert_last_apply` restore the prior state? Does it report what was undone? Does it return a clear "nothing to revert" when called twice in succession? Does `revert_apply_by_sequence` correctly undo a non-tip entry without disturbing later entries? Does it reject out-of-range sequence numbers cleanly?

---

### Phase 11: Semantic search, discovery, and reflection/DI

1. `semantic_search("async methods returning Task<bool>")`. v1.8+ HTML-decodes ingress — a client that double-encodes (`Task&lt;bool&gt;`) gets the same results as the unencoded query.
2. `semantic_search("methods returning Task<bool>")` — broader paraphrase; explain the delta in modifier-sensitive matching.
3. `semantic_search("classes implementing IDisposable")` — cross-check against `find_implementations(IDisposable)`.
4. `semantic_grep` with a structural / regex-style pattern (e.g. a method-call shape or LINQ chain). Different surface from `semantic_search` — pattern-based rather than natural-language. Verify the result set is non-empty on a known-good pattern, and that an intentionally bogus pattern produces a clean empty result rather than a crash.
5. `find_reflection_usages` — `typeof`, `GetMethod`, `Activator`, etc.
6. `get_di_registrations` — full DI wiring audit.
7. `source_generated_documents` — source-gen outputs listed.

**MCP audit checkpoint:** Do paired `semantic_search` queries return relevant and explainable differences? Does `semantic_grep` produce sensible structural matches and reject malformed patterns cleanly? Does `find_reflection_usages` find all reflection patterns? Does `get_di_registrations` parse all registration styles? Are source-gen documents correctly listed?

---

### Phase 12: Scaffolding

**Default:** previews on the disposable worktree, plus apply one scaffold, verify, clean up. Under `--no-worktree`: apply siblings are `skipped-safety — --no-worktree`.

1. `scaffold_type_preview`. v1.8+ default is `internal sealed class`; records, interfaces, enums stay `public`. The file lands at `{projectRoot}/{namespace-folders}/T.cs` even when the namespace doesn't start with the project name. v1.17+ auto-implements interface stubs (`throw new NotImplementedException()` + required usings) when `baseType`/`interfaces` resolve to an interface with members — opt out with `implementInterface: false` and assert the body becomes empty.
2. `scaffold_test_preview` for an existing type. v1.8+ constructor-arg expressions: `IEnumerable<T>` / `ICollection<T>` / `IList<T>` / `IReadOnlyList<T>` → `System.Array.Empty<T>()`; dictionaries → `new Dictionary<K,V>()`; `string` → `string.Empty`. Previously every parameter was `default(T)`.
3. `scaffold_test_batch_preview(testProjectName, targets=[{targetTypeName, targetMethodName?}, …])` on 3–5 targets. Verify one composite token covers every generated file (not N tokens). Apply via `preview_multi_file_edit_apply` (**not** `apply_composite_preview`). Confirm every scaffolded test is discoverable.
4. `scaffold_first_test_file_preview` for a project that has no test file yet — bootstraps the test project shape.
5. Apply one scaffold via `scaffold_type_apply` (using a token from `scaffold_type_preview`) — verify the resulting file matches the preview diff and that `compile_check` passes.
6. Apply one test scaffold via `scaffold_test_apply` (using a token from `scaffold_test_preview`) — verify the new test is discoverable via `test_discover` and runs green via `test_run`.

**MCP audit checkpoint:** Does `scaffold_type_preview` infer the correct namespace and produce `internal sealed class` by default? Does the interface-stub emission toggle on `implementInterface`? Does `scaffold_type_apply` produce the file the preview promised? Does `scaffold_test_preview` produce compiling stubs that run green? Does `scaffold_test_apply` discover and run cleanly? Does `scaffold_test_batch_preview` emit one composite token? Does `scaffold_first_test_file_preview` bootstrap cleanly?

---

### Phase 13: Project mutation

**Default:** previews on the disposable worktree, plus at least one reversible preview → `apply_project_mutation` → verify → inverse preview → reverse apply. Under `--no-worktree`: apply siblings are `skipped-safety — --no-worktree`.

1. `add_package_reference_preview` / `remove_package_reference_preview`.
2. `add_project_reference_preview` / `remove_project_reference_preview`.
3. `set_project_property_preview` (e.g. `Nullable`, `LangVersion`).
4. `set_conditional_property_preview` scoped to a configuration.
5. `add_target_framework_preview` / `remove_target_framework_preview` (multi-targeting).
6. If CPM: `add_central_package_version_preview` / `remove_central_package_version_preview`.
7. `get_msbuild_properties` with `propertyNameFilter` / `includedNames` to probe a non-default path.
8. Apply + reverse one reversible mutation on the disposable worktree; verify with `workspace_reload` + `build_project` / `compile_check`.

**MCP audit checkpoint:** Do the previews produce correct XML diffs? Do conditional-property conditions evaluate correctly? Do framework additions/removals look right? Does the forward-reverse apply round-trip work? Are multi-targeting / CPM correctly marked `skipped-repo-shape` when unused?

---

### Phase 14: Navigation & completions

1. `go_to_definition` on a usage → correct declaration.
2. `goto_type_definition` on a variable → the type, not the variable declaration.
3. `enclosing_symbol` inside a method → the method.
4. `get_symbol_outline` on a non-trivial file (≥3 types or ≥10 members) — verify the outline tree depth, kind classification, and ordering match what `document_symbols` returns. Drift between these two surfaces (different kinds, different ordering, or one missing a member the other lists) is a FLAG.
5. `get_completions` after a dot. v1.8+ ranks locals/parameters → type members → types → long tail. For `filterText="To"`, in-scope `ToString` should appear before namespace-qualified externals like `ToBase64Transform`.
6. `find_references_bulk` with multiple symbol handles — batch results match individual `find_references`.
7. `find_overrides` on a virtual method; `find_base_members` on an override.

**MCP audit checkpoint:** Are navigation results accurate? Does `get_symbol_outline` agree with `document_symbols`? Does `get_completions` rank sensibly? Does `find_references_bulk` match individual calls?

---

### Phase 15: Resource verification

1. `roslyn://server/catalog` — machine-readable surface inventory.
2. `roslyn://server/resource-templates`.
3. `roslyn://workspaces` (summary); `roslyn://workspaces/verbose` for the project tree.
4. `roslyn://workspace/{id}/status` and `/status/verbose` — assert matching `WorkspaceVersion` + `SnapshotToken`.
5. `roslyn://workspace/{id}/projects` vs `project_graph` tool output.
6. `roslyn://workspace/{id}/diagnostics` vs `project_diagnostics` tool output.
7. `roslyn://workspace/{id}/file/{filePath}` vs `get_source_text`.
8. `roslyn://workspace/{id}/file/{filePath}/lines/{N-M}` — experimental line-range slice. Request a small known range (e.g. `lines/1-10`); verify the response starts with a `// roslyn://…/lines/N-M of T` marker comment and contains only the requested lines. Request an invalid range (`lines/10-5`) — expect a structured error, not a hang.

**MCP audit checkpoint:** Do resources agree with their tool counterparts? Are URI templates resolved correctly? Do summary/verbose versions share `WorkspaceVersion` + `SnapshotToken`? Is the line-range slice marker present and is the invalid range rejected cleanly?

---

### Phase 16: Prompt verification (`prompts/list` + `prompts/get`)

The live catalog is authoritative for prompt count. Exercise every live prompt unless truly `skipped-repo-shape` or `blocked`.

**Per-prompt checklist:**

1. **Schema sanity.** Look up the prompt in `roslyn://server/catalog`; verify argument list against `prompts/list`. Mismatch = FAIL.
2. **Rendered output correctness.** Invoke with realistic arguments from Phases 1–3. Every tool name in the rendered text must exist in the live catalog. Hallucinated tool = FAIL.
3. **Actionability.** Does the rendered text produce concrete tools + preview→apply chains + verification steps?
4. **Idempotency.** Same args twice → same rendered text modulo `_meta.elapsedMs`.

**Minimum exercised set:** `explain_error`, `suggest_refactoring`, `review_file`, `discover_capabilities`. Cover the remaining live prompt surface — the current snapshot includes `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection`, `session_undo`, `refactor_loop`. If the live catalog differs, trust the catalog and record prompt drift in *Improvement suggestions*.

Also exercise the prompt-renderer tool:

5. `get_prompt_text(promptName, parametersJson)` — verify the rendered messages array matches what `prompts/get` would have returned. Repeat for 2–3 prompts; no hallucinated tool names. **Negative probes:** unknown `promptName` (actionable error); malformed `parametersJson` (error cites the parse failure).

For every exercised prompt, append one row to the *Prompt verification* table.

**MCP audit checkpoint:** Do argument templates match `prompts/list`? Does every rendered prompt reference only live-catalog tools? Does `discover_capabilities` align with Phase -1's live catalog? Does `get_prompt_text` match the `prompts/get` surface?

---

### Phase 17: Boundary & negative testing

Deliberately probe edge cases. Verify inputs validated, error messages helpful, no crashes.

#### 17a. Invalid identifiers
1. Workspace-scoped tool with a non-existent workspace id → actionable error. v1.8+: error envelope's `tool` field carries the actual tool name, not `"unknown"`.
2. `find_references` / `symbol_info` with a fabricated symbol handle. v1.8+: structurally valid but unfindable handle (e.g. base64 of `{"MetadataName":"NonExistent.Type"}`) returns `category: NotFound`, not silent `{count:0, references:[]}`. Also applies to `find_consumers`, `find_type_usages`, `find_implementations`, `find_overrides`, `find_base_members`, `impact_analysis`, `find_type_mutations`.
3. `rename_preview` with the same fabricated handle.

#### 17b. Out-of-range positions
1. `go_to_definition` with line beyond end-of-file.
2. `enclosing_symbol` at line 0, column 0 (off-by-one probe).
3. `analyze_data_flow` with `startLine > endLine`.
4. `probe_position` on a position known to be whitespace.

#### 17c. Empty / degenerate inputs
1. `symbol_search("")` — empty result or clear error, not crash.
2. `analyze_snippet` with empty code body.
3. `evaluate_csharp` with empty input.

#### 17d. Stale and double-apply
1. `*_apply` with an already-consumed preview token → clear stale-token rejection.
2. Fresh preview token → advance the workspace version with a separate low-impact mutation on the disposable worktree → attempt to apply the now-stale token → clear workspace-version/staleness rejection. Under `--no-worktree`, mark `skipped-safety — --no-worktree` (no apply available to invalidate the token).
3. `revert_last_apply` twice in succession — second call returns clear "nothing to revert", not an error.

#### 17e. Post-close operations
1. `workspace_close`.
2. `workspace_status` with the now-closed id → clear error.
3. `workspace_load` to re-open (or defer reopen to end of phase if remaining phases are complete).

**MCP audit checkpoint:** For each error path — actionable, vague, or unhelpful? Did stale-token / version-mismatch cases fail cleanly? Any 500-level / unhandled exceptions? Did bad input drive the server into a degraded state affecting subsequent calls?

---

### Phase 18: Regression verification

Re-test 3–5 previously recorded issues from whichever prior source the audited repo maintains — a tracked backlog file at the repo root, a project's GitHub Issues, a prior audit report, or a saved repro list. If no prior source, `**N/A — no prior source**`.

1. Read the prior source; select 3–5 items reproducible with the current workspace.
2. For each, reproduce the exact scenario. Record **still reproduces** / **partially fixed** (describe) / **no longer reproduces — candidate for closure**.

### Phase 19: Finding emission (dual-path: stdout default, opt-in `gh issue create`)

For each **actionable** finding in the audit report (anything that lands in section 13 *MCP server issues* or in section 14 *Improvement suggestions* with a concrete fix sketch), render one finding envelope and emit it to **one of two destinations** depending on the `--auto-file` flag passed to the skill:

**Envelope (shared across both destinations):**

| Field | Source |
|---|---|
| `id` | kebab-case slug; prefix with the audited repo's id (derive from `git remote get-url origin` → `owner/repo` or fall back to repo dir basename). Example: `tradewise-find-references-stale-cache`. |
| `source-repo` | audited repo's kebab-case id |
| `severity` | `P0` / `P1` / `P2` / `P3` matching section 13 severity, or `P3`/`P2` for section-14 suggestions per their workflow blocking impact |
| `area` | one of `tools` / `resources` / `prompts` / `skills` / `concurrency` / `perf` / `docs` |
| `anchors` | one or more `path/to/file.ext:LINE` strings (relative to the audited repo's root) |
| `finding` | one to two sentences describing the bug or gap |
| `repro` | one to two sentences describing the minimal reproduction (which tool / inputs / expected vs. actual) |
| `proposed-fix` | one to two sentences pointing at the likely fix shape |

**Default (no `--auto-file`):** print each finding envelope to stdout as a ready-to-paste GitHub Issue body. Format:

```
## TITLE: <id>
Labels: area:<area>, severity:<severity>
Body:
- id: <id>
- source-repo: <source-repo>
- severity: <severity>
- area: <area>
- anchors:
  - <anchor1>
  - <anchor2>
- finding: <finding>
- repro: <repro>
- proposed-fix: <proposed-fix>
```

**With `--auto-file`** (and only when `gh` is on `PATH` and `gh auth status` is authenticated): for each non-refused finding, write the body block to a temp file and call:

```
gh issue create --repo darylmcd/Roslyn-Backed-MCP \
  --title "<id>" \
  --label "area:<area>" --label "severity:<severity>" \
  --body-file <tempfile>
```

Capture the returned Issue URL and append it to the audit report's *Finding emission* section. If `gh` is missing or unauthenticated, fall back to stdout-print and emit one warning line — do not silently drop findings.

**Refusal contract — load-bearing pre-disclosure safeguard:**

The skill **must not** call `gh issue create` for any finding whose `severity == P0` OR `area == security`. Such findings always print to stdout, regardless of the `--auto-file` flag, prefixed with:

```
**SECURITY / P0 finding — DO NOT FILE PUBLICLY.**
Escalate via GitHub security advisories: https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new
```

The refusal is non-negotiable and applies even when `--auto-file` is explicitly passed.

`**N/A — no actionable findings**` is a valid Phase 19 outcome when sections 13 + 14 are both empty.

---

### Final surface closure (mandatory before the report)

1. Compare the coverage ledger against the live catalog from Phase -1 / 0.
2. Every unaccounted tool/resource/prompt: call it now or assign a final explicit status with a one-line reason.
3. Confirm all audit-only mutations from Phases 8b / 9–13 were reverted or cleaned up. Only intentional Phase 6 product improvements remain. (8b writer-reclassification probes + W2 `set_editorconfig_option` reverted; W1 `format_document_apply` is the audit-only apply Phase 9 reverts.)
4. Ledger totals match live catalog; catalog summary matches `server_info`.
5. *Concurrency matrix* fully populated (or the whole Phase 8b is `blocked` with a single reason).
6. *Debug log capture* has at least one entry or explicitly states `client did not surface MCP log notifications`.
7. **Compute the experimental promotion scorecard.** For each experimental entry, use this rubric:
   - **`promote`** — ALL of: exercised end-to-end with ≥1 non-default parameter path; schema matched behaviour on every probe; zero FAIL findings in this run or prior backlog; p50 `elapsedMs` within budget (single-symbol reads ≤5 s, solution scans ≤15 s, writers ≤30 s); preview/apply round-tripped cleanly where applicable; error path actionable on ≥1 negative probe; catalog description matched actual behaviour.
   - **`keep-experimental`** — exercised with pass signal but missing ≥1 promote criterion (typically: writer round-trip not performed, or `--no-worktree` gated the apply, or a non-default path was not probed).
   - **`needs-more-evidence`** — not exercised (`skipped-repo-shape` / `skipped-safety` / `blocked`) OR one exercise too shallow to judge. Default for `blocked` entries.
   - **`deprecate`** — exercised, produced FAIL findings warranting removal. Pair with an *MCP server issues* entry.
8. *Schema vs behaviour drift*, *Error message quality*, *Parameter-path coverage*, *Performance baseline* tables populated. Every exercised tool contributes ≥1 row to *Performance baseline*; the other three can be empty-with-reason.
9. *Prompt verification* has one row per exercised prompt.

When all phases are done, `workspace_close` to release the session (if not already closed in Phase 17e).

---

## Output Format — MANDATORY

### What goes in git (nothing from this audit)

The audit run produces no commits in the audited repo's history. Phase 6 applies are exercised inside the disposable worktree the prompt creates, and the worktree is torn down at run end (sub-phase 6z). The audited repo's primary working tree and `main` branch are never mutated. The deliverable is the audit report + the promotion scorecard JSON — see below.

### What goes in a file (incremental draft + final canonical output)

Maintain a draft at `<canonical-path>.draft.md` and append findings after every phase. Never revisit prior phases by re-running tool calls — read from the draft. When all phases complete, atomically rename the draft to the canonical name.

The audit is **not finished** until both of:

1. The canonical file exists on disk at `<canonical-path>`.
2. The draft was continuously updated per phase (no one-shot final flush from working memory).

A one-shot flush from working memory is the most common cause of broken runs — the model accumulates ~1–2 MB of tool results in active context, then runs out of room when serializing the report. Persisting after each phase keeps each turn within budget and lets the next turn drop the prior phase's tool-result bulk.

This prompt writes **raw per-run evidence only**. The audit report belongs in the audited repo, not in any other location.

### Where to save the report

**Canonical path:** `<audited-repo-root>/audit-reports/<timestamp>_<repo-id>_mcp-server-surface-test.md`

- `<timestamp>` = current UTC `yyyyMMddTHHmmssZ`.
- The prose `.md` report stays in the audited repo's own `audit-reports/` directory. Cross-repo handoff to upstream happens via the Phase 19 finding emission (stdout-print or `gh issue create`), not by relocating the prose report.

### Promotion scorecard JSON (sibling artifact — MANDATORY)

In addition to the human-readable `.md` report, write a machine-readable scorecard at:

**`<audited-repo-root>/audit-reports/_latest-promotion-scorecard.json`**

The scorecard lives **next to its source evidence** in the audited repo's own `audit-reports/` folder, alongside the prose audit report. This file is **overwritten** each run. Schema:

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-05-05T18:36:19Z",
  "noWorktree": false,
  "auditedRepo": "roslyn-backed-mcp",
  "auditReportPath": "audit-reports/20260505T183619Z_roslyn-backed-mcp_mcp-server-surface-test.md",
  "serverVersion": "1.33.2",
  "catalogVersion": "2026.04",
  "experimentalSurface": {
    "tools": 56,
    "resources": 4,
    "prompts": 20
  },
  "scorecard": [
    {
      "kind": "tool",
      "name": "scaffold_test_apply",
      "category": "scaffolding",
      "currentTier": "experimental",
      "recommendation": "promote",
      "evidenceCount": 7,
      "evidence": [
        "phase-12 apply round-trip clean (preview-token honored, post-apply compile_check green)",
        "phase-17 negative probe — stale token rejected with actionable error",
        "phase-8 test_discover finds new test, test_run green",
        "p50 elapsedMs=1240ms (within writer budget 30s)",
        "schema accurate vs. live behavior",
        "no entries in phase-1 debug log",
        "no relevant backlog rows"
      ],
      "blockers": []
    },
    {
      "kind": "tool",
      "name": "split_class_preview",
      "category": "refactoring",
      "currentTier": "experimental",
      "recommendation": "needs-more-evidence",
      "evidenceCount": 2,
      "evidence": [
        "phase-10 preview emitted valid partial classes",
        "p50 elapsedMs=480ms"
      ],
      "blockers": [
        "no apply round-trip exercised (apply sibling skipped-safety per --no-worktree)",
        "no negative-probe evidence on shared-state across the split"
      ]
    }
  ],
  "summary": {
    "promote": 1,
    "keep-experimental": 0,
    "needs-more-evidence": 1,
    "deprecate": 0,
    "blocked": 0
  }
}
```

**Field rules:**

- `recommendation` ∈ `"promote" | "keep-experimental" | "needs-more-evidence" | "deprecate"`. Mirror the per-call recommendation captured in the `.md` report's *Experimental promotion scorecard* section. Do not invent recommendations beyond what the human-readable scorecard contains.
- `currentTier` is the live value from `roslyn://server/catalog` per entry. Do **not** infer it from the prompt.
- `evidence` is a flat string array — one short sentence per supporting probe. Match the per-call promotion-signal lines captured in the draft (principle #14).
- `blockers` is empty when `recommendation == "promote"`; populated with concrete missing evidence otherwise.
- Skip rows for entries marked `blocked` in the coverage ledger — they cannot be scored. Track them in `summary.blocked` only.

**Why this exists.** Upstream maintainers use the per-repo scorecard plus a quorum rule (≥2 workspaces with `recommendation: "promote"`, no `keep-experimental` or `deprecate` blockers) before flipping a tool's experimental → stable tier. Without the JSON the audit's promotion lane has no operational signal. With per-repo scorecards plus quorum, single-workspace anomalies no longer drive tier decisions.

**Staleness contract.** The JSON is a snapshot, not a journal. Treat it as warn-after-30-days, ignore-after-90-days; on staleness, run `/mcp-server-surface-test` again to refresh the artifact.

**`--no-worktree` runs still emit the scorecard.** Apply round-trips that depended on the disposable worktree are recorded as `skipped-safety — --no-worktree` in the coverage ledger and the affected writer tools' `recommendation` will typically default to `needs-more-evidence`, with `blockers` citing the missing worktree. The scorecard JSON is still written so consumers see a fresh artifact and can decide for themselves whether the missing-worktree evidence matters for their gate.

### Naming scheme (`<timestamp>_<repo-id>`)

- `<timestamp>`: current UTC `yyyyMMddTHHmmssZ`.
- `<repo-id>`: audited solution/repo name — strip `.sln` / `.slnx` / `.csproj`; lowercase; replace spaces and dots with hyphens. Examples: `20260422T154500Z_itchatbot_mcp-server-surface-test.md`.

### Report contents (required sections)

The report is consumed by downstream agents. Dense tables, fixed schemas, one-line entries. Narrative paragraphs only when the issue genuinely requires one.

**Mandatory sections (always in full):**

| # | Section | Purpose |
|---|---|---|
| 1 | Header | Server, client, scale, mode, debug-log channel |
| 2 | Coverage summary | Grouped by live `Kind` + `Category` |
| 3 | Coverage ledger | One row per live tool/resource/prompt |
| 4 | Verified tools | Tested-and-working list |
| 5 | Phase 6 refactor summary | Applied product changes (or N/A + reason) |
| 6 | Performance baseline | `_meta.elapsedMs` per exercised tool |
| 7 | Schema vs behaviour drift | Principle #2 output |
| 8 | Error message quality | Principle #4 output |
| 9 | Parameter-path coverage | Principle #6 output |
| 10 | Prompt verification | Phase 16 per-prompt table |
| 11 | Experimental promotion scorecard | Per-entry recommendation |
| 12 | Debug log capture | Phase 0 channel output |
| 13 | MCP server issues (bugs) | Per-issue detail |
| 14 | Improvement suggestions | Actionable UX / output enrichment |

**Conditional sections (populate when data exists, otherwise `**N/A — <reason>**`):**

| # | Section | Populate when |
|---|---|---|
| 15 | Concurrency matrix | Phase 8b ran with at least sequential baselines |
| 16 | Writer reclassification verification | Phase 8b.5 exercised writers |
| 17 | Response contract consistency | Principle #5 observed ≥1 inconsistency |
| 18 | Known issue regression check | Prior source existed |
| 19 | Known issue cross-check | New findings matched a backlog/issue id |

Mandatory sections always render in full; conditional sections collapse to a single `**N/A — <reason>**` line when unpopulated.

### Markdown template

```markdown
# MCP Server Audit Report

## 1. Header
- **Date:**
- **Audited solution:**
- **Audited revision:** (commit / branch if available)
- **Entrypoint loaded:**
- **Flags:** (none) / `--no-worktree`
- **Isolation:** (absolute disposable worktree path + branch name, or `degraded — --no-worktree flag, Phase 6 applies skipped`)
- **Teardown:** `clean` / `partial — <what survived>` / `failed — <error>` / `N/A — --no-worktree`
- **Client:** (name/version; note if prompts or resources are client-blocked)
- **Workspace id:**
- **Warm-up:** `yes` / `no` (did you call `workspace_warm` after load?)
- **Server:** (from `server_info`)
- **Catalog version:** (from `server_info.catalogVersion`)
- **Roslyn / .NET:** (if reported)
- **Live surface:** `tools: <stable>/<experimental>`, `resources: <stable>/<experimental>`, `prompts: <stable>/<experimental>` (from `server_info.surface`)
- **Scale:** ~N projects, ~M documents
- **Repo shape:**
- **Prior issue source:**
- **Debug log channel:** `yes` / `partial` / `no`
- **Report path note:** (path under the audited repo's `audit-reports/`; cross-repo handoff is via Phase 19 fragments, not via copying the prose report)

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|

## 3. Coverage ledger
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|

## 4. Verified tools (working)
- `tool_name` — one-line observation (include p50 elapsedMs when available)

## 5. Phase 6 apply-tool exercise summary
- **Disposable worktree path:** absolute path (or `N/A — --no-worktree`)
- **Disposable branch:** `mcp-server-surface-test/<ts>` (or `N/A — --no-worktree`)
- **Scope:** (which 6a–6m sub-phases ran; `**N/A — skipped per --no-worktree**` when degraded mode)
- **Apply-tool calls:** bullets — preview→apply pairs exercised, with tool name + outcome
- **Verification:** `compile_check` / `test_run` / `build_workspace` outcomes after applies
- **Teardown outcome:** see header *Teardown* row; expand here if `partial` / `failed`

## 6. Performance baseline (`_meta.elapsedMs`)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|

## 10. Prompt verification (Phase 16)
| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|

## 11. Experimental promotion scorecard
| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|

## 12. Debug log capture
| timestamp | level | logger | correlationId | eventName | message | Phase | Tool in flight |
|-----------|-------|--------|----------------|-----------|---------|-------|----------------|

## 13. MCP server issues (bugs)

### 13.1 <title or tool name>
| Field | Detail |
|-------|--------|
| Tool | |
| Input | |
| Expected | |
| Actual | |
| Severity | |
| Reproducibility | |

(repeat per issue, or write `**No new issues found**` when none)

## 14. Improvement suggestions
- `tool_name` — suggestion (UX / missing feature / workflow gap / output enrichment / schema mismatch)

## 15. Concurrency matrix (Phase 8b)

### Concurrency probe set
| Slot | Tool | Inputs (concise) | Classification | Notes |
|------|------|------------------|----------------|-------|

### Sequential baseline (single-call wall-clock, ms)
| Slot | Wall-clock (ms) | Notes |
|------|------------------|-------|

### Parallel fan-out and behavioral verification
- **Host logical cores:** _
- **Chosen N:** _N = min(4, max(2, logical_cores))_

| Slot | Parallel wall-clock (ms) | Speedup vs baseline | Expected | Pass / FLAG / FAIL | Notes |
|------|---------------------------|----------------------|----------|---------------------|-------|

### Read/write exclusion behavioral probe
| Probe | Observed | Expected | Pass / FLAG / FAIL | Notes |
|-------|----------|----------|---------------------|-------|

### Lifecycle stress
| Probe | Observed | Reader saw | Reader exception | correlationId | Expected | Pass / FLAG / FAIL | Notes |
|-------|----------|-----------|------------------|---------------|----------|---------------------|-------|

## 16. Writer reclassification verification (Phase 8b.5)
| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|

## 17. Response contract consistency
| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|

## 18. Known issue regression check (Phase 18)
| Source id | Summary | Status |
|-----------|---------|--------|

## 19. Known issue cross-check
- bullet list of newly observed issues that match a prior source
```

### Completion gate

The prose `.md` report must exist at the canonical path above (under the audited repo's `audit-reports/`). Create the directory if missing. Phase 19 must have emitted at least one fragment under `<audited-repo-root>/backlog.d/` OR explicitly recorded `**N/A — no actionable findings**`. The task is **incomplete** without both gates passing.

---

## Appendix — intentionally minimal

The body of this prompt **does not** embed hard-coded tool, resource, prompt, or skill counts, nor per-version change logs. Those always drift. Instead, the live surface comes from three sources the running server emits:

| Source | What it gives you | When captured |
|---|---|---|
| `server_info` | Version, catalog version, tier counts, `surface.registered.parityOk`, connection state | Phase -1 |
| `roslyn://server/catalog` | Per-entry `Kind`, `Category`, `SupportTier`, metadata | Phase -1 / 0 |
| `roslyn://server/resource-templates` | All resource URI templates | Phase 0 |

That is the authoritative surface for any run. Trust those captures over any prose in this prompt.

### Historical notes (kept terse, rotate when obsolete)

- **Script timeout.** `evaluate_csharp` honors `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` (default 10 s). Principle #9 governs multi-minute stalls.
- **Write-lock model.** One per-workspace `AsyncReaderWriterLock` via `WorkspaceExecutionGate`. No dual-lock lane (historical `_rw-lock_` / `_legacy-mutex_` audit filenames are artifacts).
- **v1.28+.** `workspace_warm` is the recommended post-`workspace_load` prime step. `rename_apply.MutatedSymbol` carries a fresh handle for chained calls.
- **Experimental-promotion workflow.** The promotion scorecard (mandatory output of every run) replaces the deprecated standalone experimental-promotion prompt. Scorecard-only runs are no longer a separate mode — the canonical run always exercises the experimental surface and emits the scorecard.
