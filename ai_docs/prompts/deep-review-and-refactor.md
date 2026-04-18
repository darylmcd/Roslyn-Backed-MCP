# Deep Code Review & Refactor Agent Prompt

<!-- purpose: Exhaustive living prompt for MCP audits, refactor exercises, and experimental→stable promotion decisions against the real tool surface. Output is optimized for downstream agentic consumption (machine-parseable tables, fixed schemas, dense per-call evidence, promotion scorecards). -->
<!-- DO NOT DELETE THIS FILE.
     This is a living document that must be maintained and kept in sync
     with the project's actual tool, resource, prompt, and plugin-skill surface at all times.
     When tools, resources, prompts, or skills are added, removed, or renamed,
     update the Tools Reference appendix (tier + category) and re-run `./eng/verify-ai-docs.ps1`.
   Last surface audit: 2026-04-17 (catalog 2026.04; 147 tools = 104 stable / 43 experimental, 13 resources (9 stable / 4 experimental), 20 prompts (all experimental), 22 plugin skills). Post-v1.23.0 — v1.17 added 7 experimental tools; v1.18 added 4 experimental tools + 1 experimental prompt (`refactor_loop`) + 1 skill (`refactor-loop`); v1.19.x–v1.23.x added 2 stable tools (`server_heartbeat`, `find_duplicated_methods`), 3 experimental tools (`change_type_namespace_preview`, `trace_exception_flow`, `validate_recent_git_changes`), 3 experimental resources, and additional skills (`draft-changelog-entry`, `reconcile-backlog-sweep-plan`). -->

> Use this prompt with an AI coding agent that has access to the Roslyn MCP server.
> **Primary purpose:** produce an MCP server audit (bugs, incorrect results, gaps) **plus** an experimental-tier promotion scorecard and a plugin-skill health report. **Mechanism:** real refactoring plus tool calls that exercise the full surface.

---

## Prompt

You are a senior .NET architect. You have **three missions** in priority order:

1. **MCP server audit (primary)** — For every tool, resource, and prompt call, ask whether the result is correct, complete, and consistent with sibling surfaces. Record issues in the mandatory audit file (see Output Format). Also record **tool coverage** (what was exercised vs skipped), **verified working** surfaces, and **per-call `_meta.elapsedMs`** (v1.8+ emits this on every response). Track behaviour, not just failures.
2. **Experimental promotion scorecard (primary)** — Every experimental tool, resource, and prompt you exercise receives a **promotion-readiness rating** (`promote`, `keep-experimental`, `needs-more-evidence`, or `deprecate`) with evidence citations. This feeds `docs/experimental-promotion-analysis.md` and drives release gating. Experimental entries you cannot exercise are marked `needs-more-evidence` with the reason.
3. **Refactor the target codebase (supporting)** — In **Phase 6 only**, apply meaningful improvements (fixes, renames, extractions, dead code removal, format, organize). Verify with `compile_check`, `build_workspace`, and `test_run`. Those changes belong in the target repo's git history. Summarize what you applied in the audit report's **Phase 6 refactor summary** subsection; that summary is part of the single MCP audit file, not a separate deliverable.

A fourth lane, **Plugin skills audit**, runs in **Phase 16b** — verify every `skills/*/SKILL.md` under the Roslyn-Backed-MCP repo references only live tool names, produces sensible output against the loaded workspace, and matches its frontmatter description. Skills are part of the shipped plugin surface and regressions there break every user of the Claude Code plugin.

**Known issues / prior findings:** If you are auditing from within **Roslyn-Backed-MCP** or you otherwise have access to the server backlog, cross-check open MCP issues in [`ai_docs/backlog.md`](../backlog.md) and cite matching ids briefly (for example `semantic-search-async-modifier-doc`). If you are auditing from another repo and do not have that backlog available, use the closest equivalent prior source you do have (previous audit report, issue tracker, saved repro list). If no prior source exists, mark the regression section **N/A** instead of inventing one.

**Phase order note:** Run phases in this order: **0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 8b → 10 → 9 → 11 → 12 → 13 → 14 → 15 → 16 → 16b → 17 → 18**. Phase **8b** (Concurrency / RW lock audit) runs immediately after Phase 8 so it sees the post-refactor workspace state and can compare wall-clock against the Phase 8 baseline. **Phase 9** (Undo) runs after **Phase 10** so `revert_last_apply` does **not** undo Phase 6 refactoring — see Phase 9. **Phase 16b** (Plugin skills audit) runs after Phase 16 because it reuses prompt-like workflows and benefits from the post-Phase-16 ledger state.

**Portability and completeness contract:**

1. This prompt is intended to run against **any loadable C# repo**. Prefer a `.sln` or `.slnx`; if the repo has no solution file, use a `.csproj`.
   - If no entrypoint loads successfully, continue with the workspace-independent families that still apply (`server_info`, server resources, `analyze_snippet`, `evaluate_csharp`, and any live prompts the client can invoke without a workspace) and mark every workspace-scoped entry `blocked` with the concrete load failure.
2. **Default to `full-surface`.** Only opt into `conservative` when you cannot safely use a disposable branch/worktree/clone.
   - **`full-surface` (default):** disposable branch/worktree/clone; drive preview -> apply families end-to-end where safe and clean up or reverse audit-only mutations.
   - **`conservative` (opt-in):** non-disposable repo; keep high-impact families preview-only and mark the skipped apply siblings as `skipped-safety`.
   - Before the first write-capable call, record the disposable branch/worktree/clone path you will mutate. If you cannot do that safely, switch to `conservative` and say why.
3. Treat `server_info`, `roslyn://server/catalog`, and `roslyn://server/resource-templates` as the **authoritative live surface**. If this prompt's appendix disagrees with the running server, the live catalog wins and the mismatch is itself a finding. The live catalog carries `Category` and `SupportTier` for every tool — use those exact values in the coverage summary and promotion scorecard, not the convenience groupings in this prompt's appendix.
4. Build a live **coverage ledger** from the catalog. Every live tool, resource, and prompt must end with exactly one final status: `exercised`, `exercised-apply`, `exercised-preview-only`, `skipped-repo-shape`, `skipped-safety`, or `blocked`. Silent omissions mean the audit is incomplete. The ledger carries a **`tier`** column and a **`lastElapsedMs`** column so downstream agents can sort for promotion candidates without re-running the audit.
5. If the MCP client cannot invoke a live resource or prompt family, mark those catalog entries `blocked` with the client limitation. Do not silently omit them or relabel them as skipped. Blocked experimental entries score `needs-more-evidence` in the promotion scorecard (never `promote`) because the run has no behavioural data for them.
6. Record repo-shape constraints up front: single vs multi-project, tests present, analyzers present, source generators present, DI usage present, `.editorconfig` present, Central Package Management / `Directory.Packages.props`, multi-targeting, and network/package-restore constraints.
7. Do not invent applicability. If the repo has no tests, no DI, no source generators, no multi-targeting, or only one project, record that explicitly and treat the corresponding steps as `skipped-repo-shape`.
8. Build a live **plugin skill inventory** from the Roslyn-Backed-MCP repo (see Phase 16b). When the audited repo is not Roslyn-Backed-MCP, note that skills are audited against the plugin repo, not the target solution, and treat a missing skill directory as `blocked — skills directory not accessible` rather than skipping it silently.

### Execution strategy and context conservation

1. If the client/runtime supports **subagents**, **background agents**, or another background execution facility, you MAY and generally SHOULD use them for long-running or high-output validation work to conserve the primary agent's context window.
2. Prioritize delegation for full-solution build/test/coverage work and other log-heavy operations, especially Phase 8 `test_run` with no filter, `test_coverage`, and any equivalent shell-based validation. Keep short, low-output probes inline unless delegation meaningfully reduces noise.
3. Delegation changes execution ownership, not audit ownership. The primary agent still chooses the scenario, preserves workspace/mode context, evaluates PASS / FLAG / FAIL, updates the coverage ledger, and writes the final report.
4. Require helpers to return a concise structured summary instead of raw logs: tool or command used, scope or filter, pass/fail counts, failing test names, approximate duration, coverage headline, and any anomalies or client/tool errors.
5. Do not delegate preview/apply chains or workspace-version-sensitive mutation sequences unless the helper can operate against the same disposable checkout and workspace state safely. If helper/background execution is unavailable, run the steps directly and continue the audit.

Work through each phase below **in order** (including the Phase 10 -> Phase 9 sequence), then complete the **Final surface closure** step before you write the report. After each tool call, evaluate whether the result is correct and complete. If a tool returns an error, empty results when data was expected, or results that seem wrong, record it for the MCP Server Audit Report.

### Cross-cutting audit principles (apply to every phase)

These standing rules apply to **every** tool call across all phases. Do not wait for a specific phase checkpoint to observe these — capture them in real time. Each rule has a **fixed output slot** in the final report (see Output Format) so observations never get lost.

1. **Inline severity signal:** After each tool call, mentally tag the result: **PASS** (correct and complete), **FLAG** (minor issue or unexpected behavior worth noting), or **FAIL** (incorrect, crash, or missing data). Accumulate FLAGs and FAILs for the report so observations are not lost between tool calls and report-writing time.
2. **Tool description accuracy (Schema vs behaviour):** Compare each tool's actual behavior with its MCP tool schema/description. Did the schema accurately describe required vs optional parameters? Did defaults match the description? Were there undocumented parameters that changed behavior? Was the return shape consistent with what the description promised? Schema-vs-behavior mismatches are high-value findings because the descriptions are the API documentation consumed by every AI client. **Output slot:** report section *Schema vs behaviour drift*.
3. **Performance observation (`_meta.elapsedMs`):** Every tool/resource response in server v1.8+ carries `_meta.elapsedMs` (total wall-clock) and, for workspace-scoped calls, `_meta.queuedMs` / `_meta.heldMs` / `_meta.gateMode`. Record these per call — not just the obviously-slow ones — into the **Performance baseline** table. Flag any simple single-symbol read >5 s, any solution-wide scan >15 s, or any writer >30 s. The promotion scorecard uses the p50 of `elapsedMs` per tool, so paging more than one data point matters for experimental promotion decisions. **Output slot:** report section *Performance baseline (`_meta.elapsedMs`)*.
4. **Error message quality:** When a tool returns an error, evaluate the message itself: Is it **actionable** (tells the caller what went wrong and what to do differently)? **Vague** (generic message with no remediation hint)? Or **unhelpful** (raw exception / stack trace)? Record the rating in the report. **Output slot:** report section *Error message quality*.
5. **Response contract consistency:** Across tool calls, note inconsistencies in response contracts: Are line numbers 0-based or 1-based consistently? Do related tools (`find_consumers`, `find_references`, `find_type_usages`) use the same field names for the same concepts? Are classification values always strings or sometimes numeric codes? Note inconsistencies in the report. **Output slot:** report section *Response contract consistency* (conditional — populate only when observed).
6. **Parameter-path coverage:** Happy-path defaults are not enough. For each major family you exercise, probe at least one non-default path when the live schema exposes it: project/file filters, severity filters, offset/limit pagination, boolean flags, alternate `kind` values, or similar. If the running schema does not expose a parameter mentioned in this prompt, record that as `N/A` rather than inventing it. **Output slot:** report section *Parameter-path coverage*.
7. **Precondition discipline:** Distinguish server defects from repo/environment constraints. If a tool depends on tests, analyzers, network access, source generators, DI registrations, Central Package Management, or a multi-project graph, record whether the precondition is absent in the repo, blocked by the environment, or mishandled by the server. A clear, actionable dependency or not-applicable response is not the same as a server bug.
8. **Debug log capture:** If the MCP client surfaces server-emitted `notifications/message` log entries (the `McpLoggingProvider` forwards .NET `ILogger` events with structured payloads, including a `correlationId`), keep that channel visible for the entire run and record any `Warning`/`Error`/`Critical` entry verbatim in the report. Record `Information` entries that mention workspace lifecycle (`workspace_load`, `workspace_close`, `workspace_reload`), gate acquisition, lock contention, rate-limit hits, or request timeouts. If the client cannot show these notifications, record that as a client limitation in the header rather than silently dropping the channel.
9. **`evaluate_csharp` and "tens of minutes" stalls — neutral diagnosis only:** The server applies a **script timeout** (default 10 seconds via `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS`). A healthy `evaluate_csharp` call should finish or fail within that budget plus small overhead. If a call exceeds budget + watchdog grace **and you have not received any tool result**, treat the cause as **unknown** and stop. Do **not** speculate from inside the session about whether the stall is server-side, client-side, or transport-level — you cannot reliably distinguish these from inside the agent loop, and self-attributed diagnoses bias toward whatever exonerates the tools you're using. Persist findings to the draft, log the workspace state from the last known good tool call (`workspace_list`, last successful tool name and elapsed ms), and surface the stall in the report's **MCP server issues** section as `cause: unknown — operator must triage from MCP stderr + client logs`. Diagnostic attribution is for the operator, not the agent.
10. **Always emit text per turn:** Every assistant turn must emit at least one line of natural-language text alongside any tool calls. A turn that issues only tool calls is a silent stall from the user's perspective even if internal work is in flight. Minimum acceptable text is one sentence describing what is being dispatched, e.g. *"Phase 5 dispatched: `analyze_snippet` ×3, `evaluate_csharp` ×1. Awaiting results."* Empty assistant turns are the visible symptom of context-pressure stalls and have to be treated as a contract violation, not an optimization.
11. **Phases run sequentially — no cross-phase parallelism:** Within a phase, parallel tool calls are encouraged for independent reads. **Never start phase N+1 before phase N's findings are persisted to the draft file** (see Output Format § draft persistence). Cross-phase parallelism is the most common cause of context-pressure stalls — the model fans out, accumulates tool results from multiple phases at once, and then cannot serialize the report at the end. Sequential phasing lets each phase's results drain to the draft before the next phase begins.
12. **Output budget per turn:** After cumulative tool-result size in a single turn exceeds **~250 KB**, persist current findings to the draft and start a new turn before issuing more tool calls. The usual culprits are `compile_check`, `project_diagnostics`, `list_analyzers`, `get_namespace_dependencies`, and `get_msbuild_properties` — each can return 50–700 KB on a real solution. Once a phase's tool results are recorded in the draft, drop them from active reasoning so they don't accumulate. Server v1.7+ exposes pagination (`offset`, `limit`, `severity`, `file`) on `compile_check` — use it. **Server v1.8+:** `workspace_load`, `workspace_status`, `workspace_list`, and the `roslyn://workspaces` resource now default to a lean **summary** payload (counts and load state, no per-project tree). The verbose payload is opt-in via `verbose=true` on the tools, or via the dedicated `roslyn://workspaces/verbose` and `roslyn://workspace/{id}/status/verbose` resources. Use `verbose=true` only when you actually need the per-project tree (e.g. cross-checking against `project_graph` or counting documents); the default summary keeps a status-check at ~500 bytes instead of ~30 KB on large solutions.
13. **Workspace heartbeat between phases:** Before starting a new phase, call `workspace_list`. If the workspace is missing (`count: 0`), the host process likely restarted between turns — reload from the recorded entrypoint, then resume from the last persisted phase in the draft. Do not silently continue against a dead workspace; the cascading `KeyNotFoundException` errors that follow waste a lot of agent context before the agent figures out what happened.
14. **Experimental promotion signal (per call):** For every experimental tool, resource, or prompt you exercise, record a one-line promotion observation in the draft: (a) did it behave correctly, (b) was the schema/description accurate, (c) was the error path actionable on at least one negative probe, (d) did a preview→apply pair round-trip cleanly (if applicable), and (e) did the wall-clock sit within the expected budget for its input scale. These observations feed the **Experimental promotion scorecard** output section and, via the rollup, feed `docs/experimental-promotion-analysis.md`. Do not compute the final recommendation until the Final surface closure step so the scorecard reflects all phases, not just the first exercise of each tool.
15. **Skill inventory parity (plugin surface):** If the audit has access to the Roslyn-Backed-MCP repo root (directly or via a sibling path), enumerate `skills/*/SKILL.md` in Phase 16b and cross-check their tool references against the live catalog. A skill that references a renamed or removed tool is a P2+ ship blocker for the Claude Code plugin and must be filed in the report's *Skills audit* section. This principle is the one exception to "the audit is about the server" — plugin skills ship with the server and are effectively part of the contract.

---

### Phase 0: Setup, live surface baseline, and repo shape

1. Pick the entrypoint you will load: prefer a `.sln` or `.slnx`; fall back to a `.csproj` if needed.
2. Record whether this session is running in the default `full-surface` mode or an explicitly opted-in `conservative` mode.
3. If `full-surface`, record the disposable branch/worktree/clone path you will mutate and how audit-only changes will be cleaned up. If `conservative`, record why safe disposable isolation is unavailable.
4. **Debug log channel check.** Verify that the MCP client is actively surfacing server-emitted `notifications/message` log entries (the `McpLoggingProvider` forwards .NET `ILogger` events with a structured payload that includes `correlationId`, `level`, `logger`, `message`, `eventId`, `eventName`, and any exception). If the client cannot show those notifications, note the limitation in the header and proceed; the audit must still record any structured log entries it can see.
5. Call `server_info` to record the server version, Roslyn version, catalog version, live stable/experimental counts, and `runtime` / `os` strings.
6. Read `roslyn://server/catalog` to capture the authoritative live surface inventory.
7. Read `roslyn://server/resource-templates` to capture all resource URI templates.
8. Call `workspace_load` with the chosen entrypoint. Server v1.8+ returns a lean summary by default; pass `verbose=true` if you need the full per-project tree (typically not needed at load time — `project_graph` covers the same data more efficiently).
9. Call `workspace_list` to confirm the session appears (default summary is fine).
10. Call `workspace_status` to confirm the workspace loaded cleanly. Note any load errors. The default summary surfaces `WorkspaceDiagnosticCount` / `WorkspaceErrorCount` / `WorkspaceWarningCount` so you can detect load failures without paying for the full diagnostic array.
11. Call `project_graph` to understand the project dependency structure.
12. From the loaded repo, record the repo-shape constraints that affect later phases (projects, tests, analyzers, DI, source generators, Central Package Management, multi-targeting, network limits).
13. **Restore precheck (mandatory for any C# repo).** Before running any Phase-1 semantic-analysis tool, run `dotnet restore <entrypoint>` from the host shell. Unrestored package references put `UnresolvedAnalyzerReference` entries into `Project.AnalyzerReferences`, which historically crashed `find_unused_symbols`, `type_hierarchy`, `callers_callees`, and `impact_analysis` (FLAG-A). Server v1.7+ filters that subtype out, but the precheck remains a precondition discipline — without restore, `compile_check` and `project_diagnostics` flood with hundreds of CS0246 errors from missing types in unrestored packages, which alone is enough to push a single-turn tool result over the output budget (cross-cutting principle #12). If the host has no shell access, mark this step `blocked` and continue — note in the report header that semantic-analysis tools may degrade or surface package-not-restored errors.
14. Seed or update the coverage ledger so every live tool/resource/prompt already has a planned phase or a provisional skip reason. Ledger columns: `kind`, `name`, `tier`, `category` (live catalog value), `status`, `phase`, `lastElapsedMs`, `notes`.
15. Seed the **Experimental promotion scorecard** with one row per experimental tool, resource, and prompt. Pre-fill `tier=experimental`; leave `recommendation` blank until the Final surface closure step. Count: 43 experimental tools + 4 experimental resources + 20 experimental prompts = **67 rows** when the live catalog matches the 2026.04 post-v1.23.0 snapshot (prompt count may grow — always use `server_info` / `roslyn://server/catalog` for the exact total).
16. Seed the **Performance baseline** table. Every exercised read/reader surface contributes one row; writer surfaces contribute a row in Phase 8b.5. The table is machine-readable (see Output Format → Performance baseline).

**MCP audit checkpoint:** Did `server_info` and `roslyn://server/catalog` agree on live counts and tiers? Did `workspace_load` succeed on the chosen entrypoint? Does `workspace_list` show the new session? Did `workspace_status` report any stale documents or missing projects? Did `dotnet restore` complete cleanly (or did you mark step 13 `blocked` with a reason)? Did you explicitly record the disposable isolation path / conservative rationale, the repo shape, the **debug log channel availability**, the initial coverage ledger with no silent omissions, the seeded promotion scorecard row count, and the seeded performance baseline table? If every candidate entrypoint failed to load, did you mark workspace-scoped rows `blocked` and continue only with workspace-independent families?

---

### Phase 1: Broad Diagnostics Scan

1. Call `project_diagnostics` (no filters) to get all compiler errors and warnings across the solution.
2. Call `project_diagnostics` again with at least one non-default selector exposed by the live schema (project, file, severity, and/or offset/limit pagination). Verify scoped results and paging boundaries are correct. **Server v1.8+:** `TotalErrors` / `TotalWarnings` / `TotalInfo` are invariant under `severityFilter` — calling with `severity=Error` should report the same counts as calling with no filter, only the returned arrays are narrowed. If filtered totals collapse to zero, that is a regression.
3. Call `compile_check` (default `offset=0, limit=50`) to get a paginated slice of in-memory compilation diagnostics. Compare the page contents with `project_diagnostics` for the same scope — they should be consistent but may differ in count or detail. Use `severity=Error` and `file=<path>` selectors to probe non-default parameter paths (cross-cutting principle #6).
4. Call `compile_check` again with `emitValidation=true` (same pagination). Compare diagnostics, `TotalDiagnostics`, and wall-clock time with the default call. Server v1.8+ passes an explicit `EmitOptions(metadataOnly: false)` to force the full PE-emit path, but the precondition is real: when NuGet packages are unrestored the emit phase short-circuits and the wall-clock cost matches GetDiagnostics. If you observe identical timing between true/false, run `dotnet restore` first before flagging it as a regression (the tool description spells this out).
5. Call `security_diagnostics` to identify security-relevant findings with OWASP categorization.
6. Call `security_analyzer_status` to check which analyzer packages are installed and what's missing.
7. Call `nuget_vulnerability_scan` to scan NuGet references for known CVEs. Compare with `security_diagnostics` — vulnerability scan covers supply-chain CVEs while security diagnostics covers source-level patterns.
8. Call `list_analyzers` to enumerate all loaded analyzers and their rules. Note total rule count.
9. If the live schema exposes a non-default selector or paging option for `list_analyzers`, call it that way too and verify the bounded result is stable.
10. Call `diagnostic_details` on a representative error and a representative warning to inspect full detail.

**MCP audit checkpoint:** Do `project_diagnostics` and `compile_check` agree on error counts? Did the non-default `project_diagnostics` selector behave correctly and preserve stable counts/page boundaries? Does `compile_check` with `emitValidation=true` find additional issues, and does the slower path still return a clear structured result? Does `nuget_vulnerability_scan` find any known CVEs, and does it return structured results with package name, advisory URL, and severity? If the environment blocks network or package metadata access, does `nuget_vulnerability_scan` fail fast with a clear explanation instead of hanging or returning misleading success? Does `list_analyzers` return data for all projects, and do any paging/selector options behave correctly if exposed? Are there any analyzers that fail to load (look for LOAD_ERROR entries)? Does `diagnostic_details` return accurate location and message information?

---

### Phase 2: Code Quality Metrics

1. Call `get_complexity_metrics` with no filters to find the most complex methods across the solution. Note any with cyclomatic complexity > 10 or nesting depth > 4.
2. Call `get_cohesion_metrics` with `minMethods=3` to find types with LCOM4 > 1 (SRP violations). Server v1.8+ ignores `[LoggerMessage]` / `[GeneratedRegex]` source-generator partial methods (they previously inflated the score by appearing as standalone clusters), and `SharedFields` only contains real field/property names — private helper methods used by multiple public methods still merge their callers into one cluster but don't show up as a "shared field".
3. Call `find_unused_symbols` with `includePublic=false` to detect dead code.
4. Call `find_unused_symbols` with `includePublic=true` and compare — are there public APIs with zero internal references?
5. Call `get_namespace_dependencies` to check for circular namespace dependencies.
6. Call `get_nuget_dependencies` to audit package references across projects.
7. Call `suggest_refactorings` to get aggregated ranked suggestions based on the complexity, cohesion, and dead-code data above. Compare the suggestions against your own observations — does the ranking make sense? Do the recommended tool sequences match the actual tools for the suggested category? This exercises the v1.11.0 aggregation tool.

**MCP audit checkpoint:** Do complexity metrics make sense for the methods shown? Are LCOM4 scores reasonable (do types with score=1 actually look cohesive when you read them)? After v1.8+, are source-gen partial methods correctly excluded from the cluster count, and are `SharedFields` lists free of private-helper-method names? Does `find_unused_symbols` miss any obviously dead code or falsely flag used symbols? Does `suggest_refactorings` produce suggestions consistent with the raw metrics? Are severity rankings reasonable (high complexity before medium; dead code as low)?

---

### Phase 3: Deep Symbol Analysis (pick 3-5 key types)

For each key type in the solution:

1. Call `symbol_search` to locate the type by name.
2. Call `symbol_info` to understand the type.
3. Call `document_symbols` on its file to see its member structure.
4. Call `type_hierarchy` to understand inheritance.
5. Call `find_implementations` on any interface or abstract type discovered via `type_hierarchy`. Verify the list is complete.
6. Call `find_references` to see how widely it's used.
7. Call `find_consumers` to classify dependency kinds (constructor, field, parameter, etc.).
8. Call `find_shared_members` to identify private members shared across public methods.
9. Call `find_type_mutations` to find all mutating members and their external callers. Server v1.8+ classifies each mutating member by `MutationScope`: `FieldWrite` (settable property or instance-field reassignment), `CollectionWrite` (Add/Remove/Clear-style call), `IO` (System.IO.File / Directory / FileStream / StreamWriter), `Network` (HttpClient / TcpClient / UdpClient), `Process` (System.Diagnostics.Process), or `Database` (DbCommand.Execute*). A class like `FileSnapshotStore` whose whole purpose is disk IO will now report its `WriteAllText`/`Delete` methods with `MutationScope=IO` even though they don't reassign instance fields.
10. Call `find_type_usages` to see how the type is used (return types, parameters, fields, casts, etc.).
11. Call `callers_callees` on 2-3 of its methods to map call chains.
12. Call `find_property_writes` on any settable properties to classify init vs post-construction writes.
13. Call `member_hierarchy` on overridden/implemented members.
14. Call `symbol_relationships` for a combined view. With server v1.7+, a caret on a method's return-type token is auto-promoted to the enclosing member by default (`preferDeclaringMember=true`). Verify by pointing the locator at the return-type token of a known method and asserting the result describes the method, not the type — this exercises the FLAG-006 fix.
15. Call `symbol_signature_help` on a key method to verify signature documentation. Same auto-promotion applies; explicitly pass `preferDeclaringMember=false` once to confirm the literal type-token resolution still works when requested.
16. Call `impact_analysis` on a symbol you might refactor to estimate change blast radius.

**MCP audit checkpoint:** Does `find_implementations` return all concrete implementations of an interface or abstract type? Are `find_references` and `find_consumers` consistent? Does `find_type_usages` categorize correctly? Do `callers_callees` results match what you'd expect from reading the code? Does `symbol_relationships` combine data correctly from its sub-tools? Does `impact_analysis` produce a reasonable blast radius estimate?

---

### Phase 4: Flow Analysis (pick 3-5 complex methods)

For each method with high complexity:

1. Call `get_source_text` to read the file.
2. Call `analyze_data_flow` on the method body (startLine to endLine). Examine:
   - Variables that flow in but are never read inside (unused parameters?)
   - Variables written inside but never read outside (dead assignments?)
   - Captured variables (closure risks)
   - **Server v1.8+:** Expression-bodied members (`=> expr`) are now supported — point the line range at the arrow or its expression and the resolver will lift the expression for analysis. Previously these returned "No statements found in the line range".
3. Call `analyze_control_flow` on the same range. Examine:
   - Is `EndPointIsReachable` false where it should be true (or vice versa)?
   - Are there unreachable exit points?
   - Do return statements cover all paths?
   - **Server v1.8+:** For expression-bodied members the result is synthesized: `Succeeded=true`, `StartPointIsReachable=true`, `EndPointIsReachable=false`, exactly one synthetic implicit return at the arrow location, no entry/exit points. Roslyn does not expose an `AnalyzeControlFlow(ExpressionSyntax)` overload, so this is the semantically-correct alternative.
4. Call `get_operations` on key expressions within the method (assignments, invocations, conditionals) to get the IOperation tree. Look for:
   - Implicit conversions that may lose data
   - Null-conditional chains that could be simplified
   - Patterns that indicate code smell (long invocation chains, nested conditionals)
5. Call `get_syntax_tree` on the method's line range to compare syntax structure with the IOperation view.

**MCP audit checkpoint:** Does `analyze_data_flow` correctly identify all variables? Are the `Captured` and `CapturedInside` lists accurate for lambdas? Does `analyze_control_flow` correctly detect unreachable code? Does `get_operations` return a sensible tree or does it miss operations? Do line numbers in results map correctly to the source?

---

### Phase 5: Snippet & Script Validation

**`analyze_snippet`** and **`evaluate_csharp`** (steps 4–7) should complete within the **script timeout** unless you raised `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS`. Multi-minute or multi–ten-minute freezes that **only clear when you message the agent** are covered by cross-cutting principle **#9** (usually not the MCP server). For the infinite-loop probe (step 7), expect at most **one** call lasting until the configured script timeout — not an unbounded hang.

1. Call `analyze_snippet` with `kind="expression"` using a simple expression like `1 + 2`. Verify it reports no errors.
2. Call `analyze_snippet` with `kind="program"` using a small class definition. Verify declared symbols are listed.
3. Call `analyze_snippet` with intentionally broken code (e.g. `int x = "hello";` with `kind="statements"`). Verify it reports CS0029 **and** that `StartColumn` points at the literal in user-relative coordinates (~9 for that example) rather than at the wrapper-relative column. Pre-fix this returned column 66 (FLAG-C, fixed in v1.7+).
4. Call `analyze_snippet` with `kind="returnExpression"` using `return 42;`. Verify it reports no errors. The new `returnExpression` kind wraps in `object? Run()` so a value-bearing return is allowed; the older `kind="statements"` still wraps in `void Run()` and rejects returns by design (FLAG-007 documented behavior).
5. Call `evaluate_csharp` with a simple expression like `Enumerable.Range(1, 10).Sum()`. Verify the result is `55`.
6. Call `evaluate_csharp` with a multi-line script. Verify execution.
7. Call `evaluate_csharp` with code that should produce a runtime error (e.g., `int.Parse("abc")`). Verify it reports the error gracefully.
8. Call `evaluate_csharp` with an infinite loop. Verify the timeout fires and doesn't hang the server.

**MCP audit checkpoint:** Does `analyze_snippet` correctly wrap different `kind` values? Are compilation errors accurate and well-formatted? Does `evaluate_csharp` handle runtime errors without crashing? Does the timeout work? Are result types correctly identified?

---

### Phase 6: Refactoring Pass

**Applies for real product changes happen here only.** Phases 10, 12, and 13 are **preview-only** (audit the tools without leaving scaffold, package, or project-mutation junk in the repo). Phase 6 is where you apply fixes, renames, extractions, formatting, and dead-code removal that genuinely improve the solution.

#### 6a. Fix All Diagnostics
1. Pick a diagnostic ID that appears many times (e.g., IDE0005 unused usings, CS8600 nullable).
2. Call `fix_all_preview` with `scope="solution"` and that diagnostic ID.
3. Examine the preview — are all instances found? Is the diff correct?
4. Call `fix_all_apply` with the preview token.
5. Call `compile_check` to verify the fix didn't break anything.

#### 6b. Rename
1. Pick a poorly-named symbol.
2. Call `rename_preview` with a better name.
3. Verify the preview shows all reference updates.
4. Call `rename_apply`.
5. Call `compile_check`.

#### 6c. Extract Interface (if a type with consumers was found in Phase 3)
1. Call `extract_interface_preview` with selected public members.
2. Examine the generated interface file in the preview.
3. Call `extract_interface_apply`.
4. Call `bulk_replace_type_preview` to update consumers from concrete to interface.
5. Call `bulk_replace_type_apply`.
6. Call `compile_check`.

#### 6d. Extract Type (if LCOM4 > 1 types were found in Phase 2)
1. Call `find_shared_members` on the type.
2. Call `extract_type_preview` with the independent cluster's members.
3. Call `extract_type_apply`.
4. Call `compile_check`.

#### 6e. Format & Organize
1. Call `format_range_preview` on a recently-edited region.
2. Call `format_range_apply`.
3. Call `format_document_preview` on an entire file that was heavily modified.
4. Call `format_document_apply`.
5. Call `organize_usings_preview` on files that had code fix changes.
6. Call `organize_usings_apply`.

#### 6f. Curated Code Fixes
1. Call `code_fix_preview` on a specific diagnostic occurrence (e.g., a nullable warning).
2. Examine the preview diff.
3. Call `code_fix_apply`.
4. Call `compile_check`.

#### 6f-ii. Diagnostic Suppression
1. Pick a diagnostic that should be suppressed rather than fixed (e.g., a deliberate pattern that triggers a style warning).
2. Call `set_diagnostic_severity` to change the severity of that diagnostic ID in `.editorconfig` (e.g., downgrade to `suggestion` or `none`).
3. Call `add_pragma_suppression` to insert a `#pragma warning disable` before a specific occurrence in source.
4. Call `compile_check` to verify the suppression took effect and no new errors were introduced.

#### 6g. Code Actions
1. Call `get_code_actions` at a position with available refactorings.
2. Call `preview_code_action` on an interesting action.
3. Call `apply_code_action`.

#### 6h. Direct Text Edits
1. Call `apply_text_edit` to make a small targeted edit to a single file.
2. Call `apply_multi_file_edit` to make coordinated edits across multiple files.
3. Call `compile_check`.

#### 6i. Dead Code Removal
1. From Phase 2's `find_unused_symbols` results, pick symbols confirmed as dead.
2. Call `remove_dead_code_preview` with their symbol handles.
3. Call `remove_dead_code_apply`.
4. Call `compile_check`.

#### 6j. Extract Method
1. Call `suggest_refactorings` to get ranked suggestions. If any complexity suggestions appear, use them to pick an extraction target. If none, pick a method with complexity >= 10 from Phase 2's `get_complexity_metrics` results.
2. Call `analyze_data_flow` on the target statement range to understand parameters and return values.
3. Call `extract_method_preview` with the selected line range and a descriptive method name.
4. Verify the preview shows: extracted method with correct parameters, return type, and call site replacement.
5. Call `extract_method_apply`.
6. Call `compile_check`.

#### 6k. Session Change Tracking
1. After all Phase 6 mutations, call `workspace_changes` to list all changes applied during the session.
2. Verify each applied refactoring appears with correct descriptions, affected files, tool names, and timestamps.
3. Verify ordering matches the sequence of applies.

**MCP audit checkpoint:** Does `fix_all_preview` find ALL instances? Does it crash or timeout on large scopes? Does `rename_preview` catch references in comments or strings? Does `extract_interface_preview` generate valid C# syntax? Does `bulk_replace_type_preview` miss any usages? Does `extract_type_preview` handle shared private members correctly? Does `format_range_preview` format only the specified range or does it bleed? Does `remove_dead_code_preview` correctly remove only the targeted symbols? Does `set_diagnostic_severity` correctly update or create the `.editorconfig` entry? Does `add_pragma_suppression` insert the pragma at the correct line without breaking surrounding code? Does `extract_method_preview` infer correct parameters from DataFlowsIn and return value from DataFlowsOut? Does `suggest_refactorings` produce ranked suggestions that match the metrics data? Does `workspace_changes` accurately reflect all applies with correct ordering? After each apply, does `compile_check` pass?

**Cross-tool chain validation (Phase 6):** After `rename_apply`, call `find_references` on the new name — does the count match the preview? After `extract_interface_apply`, call `type_hierarchy` on the new interface — does it show the implementor? After `fix_all_apply`, call `project_diagnostics` with the same diagnostic ID — did the count drop to zero? After `organize_usings_apply`, call `get_source_text` — are usings actually sorted? After `extract_method_apply`, call `find_references` on the new method name — does it show at least one call site? After all applies, does `workspace_changes` list the correct number of entries? These chains verify that workspace state stays consistent across mutations.

**Mutation-family coverage note:** By the end of Phase 6 plus the later file/scaffolding/project-mutation phases, every write-capable family exposed by the live catalog must be either exercised end-to-end or explicitly marked `skipped-safety` / `skipped-repo-shape`. Do not assume a preview-only call covers an apply sibling unless the catalog exposes no separate apply tool.

---

### Phase 7: EditorConfig & Configuration

1. Pick a source file and call `get_editorconfig_options` on it.
2. Verify the returned options match what you'd expect from the .editorconfig files in the repo.
3. Check if key options like `csharp_style_var_for_built_in_types`, `indent_style`, and `dotnet_sort_system_directives_first` are present and correct.
4. Call `set_editorconfig_option` to set or update a non-destructive key (e.g., `dotnet_sort_system_directives_first = true`). Verify the `.editorconfig` file was created or updated correctly.
5. Call `get_editorconfig_options` again on the same file to confirm the change is reflected.

#### 7b. MSBuild Evaluation
1. Pick a project and call `get_msbuild_properties` to dump its evaluated MSBuild properties. Verify key properties like `TargetFramework`, `RootNamespace`, and `OutputType` are present and correct.
2. Call `evaluate_msbuild_property` on a specific property (e.g., `TargetFramework`) for the same project. Verify the result matches the value from `get_msbuild_properties`.
3. Call `evaluate_msbuild_items` with item type `Compile` to list source files. Verify the count is reasonable and includes files you know exist.

**MCP audit checkpoint:** Does `get_editorconfig_options` return options? Are the values accurate? Does it find the correct .editorconfig file path? Are there options that should appear but don't? Does `set_editorconfig_option` correctly write the key-value pair? Does `get_msbuild_properties` return a complete set of evaluated properties? Does `evaluate_msbuild_property` return the same value as `get_msbuild_properties` for the same key? Does `evaluate_msbuild_items` list items with correct paths and metadata?

---

### Phase 8: Build & Test Validation

**Execution note:** If the client supports subagents or background agents/tasks, prefer keeping test selection (`test_discover`, `test_related_files`, `test_related`) in the primary agent, then delegate the heavy runs (`test_run` with filters, `test_run` for the full suite, `test_coverage`, and any fallback shell test command). Bring back only the structured summary needed for the audit report and coverage ledger.

1. Call `workspace_reload` to refresh the workspace after all refactoring file changes.
2. Call `build_workspace` to do a full MSBuild build after all refactoring.
3. Call `build_project` on individual projects that were modified.
4. Call `test_discover` to find all tests.
5. Call `test_related_files` with the list of files you modified during refactoring.
6. Call `test_related` on a symbol you refactored to find tests targeting it specifically.
7. Call `test_run` with the filter from `test_related_files` to run affected tests.
8. Call `test_run` with no filter to run the full suite.
9. Call `test_coverage` to measure coverage.

If `test_discover` returns zero tests, still record that result. Then explicitly distinguish one of these outcomes: `test_run` returns a clean zero-test result, `test_run` / `test_coverage` are `skipped-repo-shape`, or the server mishandles the no-test case.

**MCP audit checkpoint:** Does `build_workspace` match `compile_check` results (same error count, same diagnostic IDs, same locations)? Does `test_related_files` correctly identify related tests? Does `test_related` find the right tests for the symbol? Does `test_run` produce structured results with pass/fail counts — and do the aggregated counts reflect the full solution or only the last assembly? Does `test_coverage` produce coverage data?

**Do not** call `revert_last_apply` yet — that would undo the most recent Phase 6 apply. Continue to **Phase 8b** (concurrency / RW lock), then **Phase 10**, then **Phase 9** (Undo verification).

---

### Phase 8b: Concurrency Audit (per-workspace RW lock)

**Stability note (repo-matrix audits, 2026-04):** Many AI agent hosts **serialize** MCP tool calls or cannot attribute wall-clock across truly parallel requests. That is expected. When true concurrency is unavailable, mark **8b.2** (parallel fan-out), **8b.3** (read/write interleaving), and timing-sensitive parts of **8b.4** as **`blocked`** — *client cannot issue concurrent tool calls* (or **`skipped-safety`** if parallel dispatch would be unsafe). Record that limitation once in the report header; **do not** force speedup ratios or benchmark comparisons against `tests/RoslynMcp.Tests/Benchmarks/WorkspaceReadConcurrencyBenchmark.cs` in that case. Still run **8b.1** sequential baselines when possible using **`_meta.elapsedMs`** (server v1.8+ — total wall-clock for every tool/resource response, no per-DTO instrumentation needed) and `_meta.heldMs` (per-workspace lock-hold). **8b.5**–**8b.6** add the most value when run in a host that supports parallel reads (benchmark harness, integration driver, or an MCP client with real concurrent requests).

**Single lock model:** The server ships **one** per-workspace concurrency model (`Nito.AsyncEx.AsyncReaderWriterLock` via `WorkspaceExecutionGate`). There is **no** alternate mutex/legacy lock lane — treat `_rw-lock_` / `_legacy-mutex_` segments in old audit filenames as historical artifacts only (`ai_docs/audit-reports/README.md`).

**Purpose:** Exercise the per-workspace lock and produce a machine-readable concurrency matrix that downstream agents can compare across runs. Intended behavior: multiple reads against the same workspace may overlap subject to the global throttle; writes are exclusive against in-flight readers; `workspace_close` / `workspace_reload` acquire the per-workspace write lock and wait for in-flight readers.

#### 8b.0 — Probe set definition (run before any timing)

Pick a stable, repeatable probe set that exercises *reader* code paths and a small number of *writer* code paths against the workspace. Record the chosen probes in the report under **Concurrency probe set** so the matrix is reproducible.

| Slot | Suggested probe (substitute repo-specific equivalents) | Classification |
|------|--------------------------------------------------------|----------------|
| R1 | `find_references` on a hot symbol used 50+ times | reader |
| R2 | `project_diagnostics` (no filters) | reader |
| R3 | `symbol_search` with a broad query that returns >100 hits | reader |
| R4 | `find_unused_symbols` `includePublic=false` | reader |
| R5 | `get_complexity_metrics` (no filters) | reader |
| W1 | `format_document_preview` then `format_document_apply` on a single file already touched in Phase 6 | writer |
| W2 | `set_editorconfig_option` with a benign key (immediately reverted) | writer (reclassified) |

If a probe is not applicable to the repo shape (e.g. no public-only dead-code surface), substitute the closest reader of equivalent cost and record the substitution.

#### 8b.1 — Sequential baseline

1. For each reader R1–R5, call the tool **once sequentially** and record `_meta.elapsedMs` from the response (or external wall-clock if the client strips `_meta`). These are the **single-call baselines**. Server v1.8+ ships `_meta.elapsedMs` on every reader and writer response — no need for external instrumentation.
2. Record any structured log entries received from the MCP server during the calls (correlation IDs, gate-related warnings, rate-limit hits, request timeouts).

#### 8b.2 — Parallel-read fan-out

1. Determine the **fan-out N** = `min(4, max(2, Environment.ProcessorCount))`. The server's global throttle is `max(2, Environment.ProcessorCount)` (`src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:36`), so on a 2-core CI host, parallel speedup is bounded above by 2× even with the per-workspace RW lock. Record the host's logical core count and chosen N in the report so the speedup ratio is interpretable.
2. Issue **R1 N times in parallel** against the same workspace id (N concurrent `find_references` calls). Record the wall-clock from request submission to last response.
3. Compute the **parallel speedup** = `N × baseline(R1) / parallel_wall_clock`. Expected range, given the global throttle ceiling at N: between `0.7 × N` and `N` (≥ 2.5× when N=4; ≥ 1.6× when N=2). Anything below `0.7 × N` is a FLAG.
4. Repeat the fan-out for R2 (`project_diagnostics` ×N) and R3 (`symbol_search` ×N). Record each parallel wall-clock and speedup.
5. If the client cannot issue concurrent tool calls (some clients serialize internally — that is itself a finding), mark this sub-phase `blocked` with the client limitation and continue.

#### 8b.3 — Read/write exclusion behavioral probe

1. Start a long-running reader: call R1 (`find_references` on the hot symbol) and immediately, while it is still in flight, issue W1 (`format_document_preview` followed by `format_document_apply` on a Phase 6-touched file).
2. Expected: W1 **waits** for the reader to release the per-workspace lock before it starts (the per-workspace write lock is exclusive against in-flight readers). Capture the inter-call timing.
3. **Inverse probe.** Start W1 first (begin `format_document_apply`) and immediately issue R1 against the same workspace. Expected: R1 waits for W1 to complete.
4. Tag any deviation from the expected behavior as a FLAG.

#### 8b.4 — Lifecycle stress probe

> Reference for expected behavior: `workspace_reload` and `workspace_close` both wrap a per-workspace **write** lock acquire inside the global load gate (`src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`), so they wait for in-flight readers to release the per-workspace lock before proceeding.

1. Start a long-running reader (R2 — `project_diagnostics`) and, while it is in flight, call `workspace_reload(workspaceId)`. Record the observed behavior with one of these labels:
   - `waits-for-reader` — reload blocks until the reader returns (expected)
   - `runs-concurrently` — reload completes while the reader is still in flight (FLAG)
   - `errors` — reload raises an exception (FLAG candidate)
2. Record whether the in-flight reader returns **stale** results, **fresh post-reload** results, or **errors**. Note the `correlationId` of both calls if available.
3. Start a long-running reader (R3 — `symbol_search`) and call `workspace_close(workspaceId)`. Expected: the reader completes cleanly and the close `waits-for-reader`. The post-acquire `EnsureWorkspaceStillExists` recheck inside `WorkspaceExecutionGate.RunPerWorkspaceAsync` is the backstop if a reader does manage to interleave with a close. Record exactly which exception (if any) the reader raises and whether the message is actionable.
4. After the close, immediately call `workspace_load` again to re-establish a session for the remaining sub-phases. Record any errors.

#### 8b.5 — Writer reclassification verification

These six tools take the per-workspace **writer** gate (they mutate workspace or on-disk state, not the reader gate). When Phase 8b runs, verify each still completes successfully and observably uses the writer path.

| # | Tool | Verification |
|---|------|--------------|
| 1 | `apply_text_edit` | Apply a single trivial text edit on a Phase 6-touched file; verify the file content changes and `compile_check` still passes. |
| 2 | `apply_multi_file_edit` | Apply a coordinated trivial edit across two files; verify both files change and `compile_check` still passes. |
| 3 | `revert_last_apply` | Call after sub-phase 1 or 2 above; verify the prior edit is reverted and the workspace is consistent. (This counts toward the Phase 9 audit-only revert step — record it once and reuse the evidence.) |
| 4 | `set_editorconfig_option` | Set a benign key (e.g. `dotnet_sort_system_directives_first = true`) and verify `get_editorconfig_options` reflects it. |
| 5 | `set_diagnostic_severity` | Set `dotnet_diagnostic.<some-IDE-id>.severity = suggestion` and verify it appears in the relevant `.editorconfig`. |
| 6 | `add_pragma_suppression` | Insert `#pragma warning disable <id>` before a known diagnostic line and verify `get_source_text` shows the pragma. |

For each row, also record whether the tool returned in the same wall-clock budget as the reader baseline (writers should be measurably slower because they actually mutate disk state).

#### 8b.6 — Concurrency matrix output (mandatory)

Write the matrix into the report using the schema in **Output Format → Concurrency matrix** below. Every cell must have a value or `N/A`; do not leave blanks.

**MCP audit checkpoint:** If **8b.2 was client-blocked**, state that explicitly and **skip** parallel speedup, ±15% stability, and benchmark-comparison questions — answer **N/A — client serializes tool calls** for those bullets. Otherwise: did parallel reads measurably overlap (speedup ≥ `0.7 × N` for N concurrent reads, where N is the host-bounded fan-out from sub-phase 8b.2.1)? Did the speedup signature on this host roughly match the 4-slot benchmark in `tests/RoslynMcp.Tests/Benchmarks/WorkspaceReadConcurrencyBenchmark.cs` (recorded against `Math.Max(2, Environment.ProcessorCount)` as the expected ceiling)? Did the read/write exclusion probe (8b.3) match the documented contract? Did the lifecycle stress probe (8b.4) reveal any TOCTOU or stale-result issues? Did all six writer tools (8b.5) complete cleanly? Were there any structured log entries that mentioned gate contention, rate-limit rejections, request timeouts, or deadlocks? When two parallel runs were possible, was `parallel_speedup` stable across them (within ±15%), or did jitter make it unreliable?

---

### Phase 10: File, Cross-Project, and Orchestration Operations

**Default behavior:** inspect previews for every applicable family. In `full-surface` mode on a disposable checkout, you SHOULD also drive at least one safe preview -> apply -> verification -> cleanup chain for the families that expose apply tools here (`move_type_to_file_apply`, `create_file_apply`, `move_file_apply`, `delete_file_apply`, and `apply_composite_preview` when it truly applies a composite result). In `conservative` mode, keep those apply siblings as `skipped-safety`.

1. Call `move_type_to_file_preview` to move a type into its own file.
2. Call `move_file_preview` to move a file to a different directory with namespace update.
3. Call `create_file_preview` to preview creating a new source file in a project.
4. Call `delete_file_preview` to preview deleting an unused source file (pick a path that would be safe if you were applying — do **not** apply).
5. If the workspace has multiple projects, call `extract_interface_cross_project_preview` to extract an interface into a different project.
6. If the workspace has multiple projects, call `dependency_inversion_preview` to extract interface + update constructor dependencies.
7. If the workspace has multiple projects, call `move_type_to_project_preview` to move a type declaration into another project.
8. If DI registrations exist or can be inferred, call `extract_and_wire_interface_preview` to extract an interface and rewrite DI registrations.
9. If a split candidate exists, call `split_class_preview` to split a type into partial class files.
10. If a package migration candidate exists, call `migrate_package_preview` to replace one NuGet package with another across projects.
11. In `full-surface` mode on a disposable checkout, execute one safe file/type preview/apply/cleanup loop using the corresponding apply tools (`move_type_to_file_apply`, `create_file_apply`, `move_file_apply`, or `delete_file_apply`). Verify the workspace remains buildable or is restored to its starting state.
12. If an orchestration preview returns a token that is committed via `apply_composite_preview`, treat `apply_composite_preview` as a **destructive apply tool despite the name**. Only call it in `full-surface` mode on a disposable checkout; otherwise mark it `skipped-safety` and note the naming friction.

**MCP audit checkpoint:** Do file and cross-project previews produce valid diffs? Do namespace and reference updates look correct in the preview? Does `extract_and_wire_interface_preview` correctly identify DI registrations? Does `split_class_preview` produce valid partial classes? Do file creation and deletion previews produce correct diffs? When you used an apply sibling, did the create/move/delete/apply path behave end-to-end and leave the repo in a clean state?

---

### Phase 9: Undo Verification (run after Phase 10)

**Why after Phase 10:** Calling `revert_last_apply` immediately after Phase 8 would undo the **last Phase 6 apply** (e.g. dead-code removal), not “all of Phase 6.” To test undo **without** losing Phase 6 work, add one **audit-only** apply on top, then revert it.

1. Perform **exactly one** low-impact Roslyn apply whose reversal is safe — for example `format_document_preview` then `format_document_apply` on a single file (prefer one already touched in Phase 6), or another minimal apply that only affects one file. This apply must become the **new** top of the undo stack.
2. Call `revert_last_apply`. Confirm it undoes **only** that audit-only apply; Phase 6 changes must remain.
3. Call `compile_check` to verify the workspace is still consistent.
4. Note: only Roslyn solution-level changes can be reverted; document if `revert_last_apply` errors or no-ops.

**MCP audit checkpoint:** Does `revert_last_apply` restore the prior state for that one apply? Does it report what was undone? Does it behave correctly when there is nothing to revert (if tested)?

---

### Phase 11: Semantic Search & Discovery

1. Call `semantic_search` with a repo-relevant natural-language query like `"async methods returning Task<bool>"` or another pattern that should exist in the target repo. Server v1.8+ HTML-decodes the query on ingress, so an over-eager JSON client that double-encodes angle brackets (`Task&lt;bool&gt;`) gets the same results as the unencoded version.
2. Call `semantic_search` with a broader paraphrase of the same idea (for example `"methods returning Task<bool>"`) and compare the delta so modifier-sensitive matching is visible.
3. Call `semantic_search` with `"classes implementing IDisposable"` or another interface-based query you can cross-check against `find_implementations`.
4. Call `find_reflection_usages` to locate reflection-heavy code that may resist refactoring.
5. Call `get_di_registrations` to audit the DI container wiring.
6. Call `source_generated_documents` to list any source-generator output files.

**MCP audit checkpoint:** Do the paired `semantic_search` queries return relevant and explainable differences? Does the broader paraphrase expose modifier-sensitive matching issues? Does `find_reflection_usages` find all reflection patterns (typeof, GetMethod, Activator, etc.)? Does `get_di_registrations` parse all registration styles, and is an empty result legitimate for the repo shape? Are source-generated documents correctly listed when generators are present?

---

### Phase 12: Scaffolding

**Default behavior:** preview-only. In `full-surface` mode on a disposable checkout, apply one scaffold (`scaffold_type_apply` or `scaffold_test_apply`), verify it compiles or is discoverable, then clean it up with file-operation tools or version control. In `conservative` mode, mark the apply siblings `skipped-safety`.

1. Call `scaffold_type_preview` to scaffold a new class in a project. Verify the generated file content and namespace in the preview payload. **Server v1.8+:** the default class scaffold emits `internal sealed class T`, not `public class T` (modern .NET convention). Records, interfaces, and enums stay `public`. The preview file lands at `{projectRoot}/{namespace-folders}/T.cs` even when the namespace does not start with the project name (e.g. `Acme.Domain` → `{project}/Acme/Domain/T.cs`).
2. If the repo has or can support a test project/framework, call `scaffold_test_preview` to scaffold a test file for an existing type. Verify it targets the correct type and uses the right test framework in the preview. **Server v1.8+:** constructor argument expressions for empty collection interfaces (`IEnumerable<T>`, `ICollection<T>`, `IList<T>`, `IReadOnlyList<T>`, etc.) emit `System.Array.Empty<T>()`; dictionaries emit `new Dictionary<K,V>()`; `string` emits `string.Empty`. Previously every parameter was `default(T)`, which threw NRE on the first call when the parameter was a non-null collection interface.
3. In `full-surface` mode on a disposable checkout, call the corresponding apply tool for one preview and verify the result with `compile_check`, `test_discover`, or both.
4. Call `compile_check` if the server requires a fresh compilation for previews; otherwise skip (previews alone do not change disk).

**MCP audit checkpoint:** Does `scaffold_type_preview` infer the correct namespace from the project structure and produce `internal sealed class` (v1.8+ default)? Does it land the file in a folder matching the namespace path? Does `scaffold_test_preview` generate valid test stubs that actually compile **and** run green when a test target exists? When you used an apply sibling, did the scaffolded file compile or show up in test discovery as expected?

---

### Phase 13: Project Mutation

**Default behavior:** preview-only. In `full-surface` mode on a disposable checkout, execute at least one reversible preview -> `apply_project_mutation` -> verification -> inverse-preview -> `apply_project_mutation` loop (for example add then remove a package reference, or set then revert a property). In `conservative` mode, mark `apply_project_mutation` as `skipped-safety`.

1. Call `add_package_reference_preview` to add a NuGet package to a project.
2. Call `remove_package_reference_preview` to remove a NuGet package.
3. Call `add_project_reference_preview` to add a project-to-project reference.
4. Call `remove_project_reference_preview` to remove a project reference.
5. Call `set_project_property_preview` to set a project property (e.g., Nullable, LangVersion).
6. Call `set_conditional_property_preview` to set a conditional project property scoped to a configuration.
7. Call `add_target_framework_preview` to add a target framework to a multi-targeting project.
8. Call `remove_target_framework_preview` to remove a target framework.
9. If the solution uses Central Package Management, call `add_central_package_version_preview` and `remove_central_package_version_preview` (preview only).
10. In `full-surface` mode on a disposable checkout, apply one reversible mutation and then reverse it. Verify with `workspace_reload` plus `build_project` or `compile_check`.

**MCP audit checkpoint:** Do project mutation previews produce correct XML diffs? Do `set_conditional_property_preview` conditions evaluate correctly in the preview? Do framework additions/removals look correct in the preview? When you used `apply_project_mutation`, did the forward and reverse mutations both behave correctly? Are multi-targeting and Central Package Management cases correctly marked `skipped-repo-shape` when the repo does not use them?

---

### Phase 14: Navigation & Completions

1. Call `go_to_definition` on a symbol usage. Verify it navigates to the correct declaration.
2. Call `goto_type_definition` on a variable. Verify it goes to the type, not the variable declaration.
3. Call `enclosing_symbol` at a position inside a method. Verify it returns the method.
4. Call `get_completions` at a position after a dot. Verify IntelliSense-style results. **Server v1.8+:** results are ranked with locals/parameters first, then type members (methods/properties/fields), then types (classes/structs/interfaces/enums), then the long tail (namespaces, external symbols). For a query like `filterText="To"`, in-scope `ToString` should appear before namespace-qualified externals like `ToBase64Transform`.
5. Call `find_references_bulk` with multiple symbol handles. Verify batch results match individual `find_references` calls.
6. Call `find_overrides` on a virtual method. Verify all overriding members are found.
7. Call `find_base_members` on an override. Verify it navigates back to the base declaration.

**MCP audit checkpoint:** Are navigation results accurate? Does `get_completions` return relevant members? Does `find_references_bulk` produce consistent results vs individual calls?

---

### Phase 15: Resource Verification

1. Read `roslyn://server/catalog` to get the machine-readable surface inventory.
2. Read `roslyn://server/resource-templates` to list all resource URI templates and compare with the catalog.
3. Read `roslyn://workspaces` to list active workspace sessions. **Server v1.8+:** this resource defaults to a lean summary (counts and load state per workspace, no per-project tree). For the verbose payload use the new `roslyn://workspaces/verbose` resource.
4. Read `roslyn://workspace/{workspaceId}/status` and compare with `workspace_status` tool output. **Server v1.8+:** also defaults to a summary; use `roslyn://workspace/{workspaceId}/status/verbose` when you need the full project tree, and assert that both summary and verbose responses have matching `WorkspaceVersion` and `SnapshotToken`.
5. Read `roslyn://workspace/{workspaceId}/projects` and compare with `project_graph` tool output.
6. Read `roslyn://workspace/{workspaceId}/diagnostics` and compare with `project_diagnostics` tool output.
7. Read `roslyn://workspace/{workspaceId}/file/{filePath}` for a source file and compare with `get_source_text`.

**MCP audit checkpoint:** Do resources return data consistent with their tool counterparts? Are URI templates resolved correctly? Is the server catalog accurate and up to date? Do resource DTOs use the same field names as their tool counterparts (e.g., `Name` vs `ProjectName`)? Does the catalog tool/resource/prompt count match `server_info`?

---

### Phase 16: Prompt Verification (MCP `prompts/list` + `prompts/get`)

The live catalog is authoritative for prompt count. In `conservative` mode, exercise at least 6 prompts spanning error explanation, refactoring, review, testing, security, and capability discovery (or all live prompts if fewer than 6 exist). In `full-surface` mode, exercise every live prompt from the catalog unless a prompt is truly `skipped-repo-shape` or `blocked` by the client.

**Per-prompt verification checklist (run for every exercised prompt):**

1. **Schema sanity.** Look up the prompt in `roslyn://server/catalog` and verify its argument list against the live `prompts/list` response from the MCP client. Arguments mismatch is a FAIL.
2. **Rendered output correctness.** Invoke the prompt with realistic arguments taken from Phases 1–3 evidence (diagnostic id, symbol handle, file path, etc.). Verify the rendered prompt text references real tool names from the live catalog — a rendered reference to a renamed or removed tool is a FAIL.
3. **Actionability.** Does the rendered text produce actionable guidance (concrete tools to call, concrete preview→apply chains, concrete verification step), or is it generic prose?
4. **Idempotency.** Call the same prompt twice with the same arguments. The rendered text should be stable modulo `_meta.elapsedMs`.

**Minimum exercised set:**

1. Call `explain_error` with a diagnostic from Phase 1. Verify the explanation is accurate and actionable.
2. Call `suggest_refactoring` on a symbol or region you analyzed in Phase 3. Verify the suggestions reference real tools and produce sensible guidance.
3. Call `review_file` on a file modified in Phase 6. Verify the review references actual code and produces useful observations.
4. Call `discover_capabilities` with a task category (e.g. "refactoring", "testing", "security"). Verify it returns relevant tools for the category and **no hallucinated tool names**.
5. Cover the remaining live prompt surface as applicable (current snapshot also includes `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection`, `session_undo`, `refactor_loop`). If the live catalog differs, trust the catalog and record prompt drift in the report's *Improvement suggestions* section.

For every exercised prompt, append one row to the **Prompt verification** table in the report with columns `prompt`, `schema_ok` (yes/no), `actionable` (yes/no/partial), `hallucinated_tools` (count), `idempotent` (yes/no), `elapsedMs`, and `notes`.

**MCP audit checkpoint:** Do prompt argument templates match `prompts/list`? Does every rendered prompt reference only real tools from the live catalog? Does `discover_capabilities` align with the live catalog from Phase 0? Are prompt descriptions accurate? Do any prompts fail, return empty output, or produce hallucinated tool names? Did every exercised prompt end up with a promotion-scorecard row? (All catalog prompts are currently **experimental** until promoted in `ServerSurfaceCatalog` — `server_info.surface.prompts.stable` may be 0 by design.)

---

### Phase 16b: Claude Code plugin skills audit

**Scope:** The Roslyn-Backed-MCP repo ships a Claude Code plugin whose skills under `skills/*/SKILL.md` compose multiple MCP tools into guided workflows. Skills are part of the shipped product surface and this phase verifies they stay in sync with the live catalog.

**Input:** The Roslyn-Backed-MCP repo root. When the audit target is Roslyn-Backed-MCP itself, use the loaded workspace root. When the audit target is another repo, you MUST have filesystem access to a local Roslyn-Backed-MCP checkout; if you do not, mark this phase `blocked — plugin repo not accessible` and continue.

**Expected skill count (2026.04 snapshot, post-v1.18.0):** **20 skills** — `analyze`, `bump`, `code-actions`, `complexity`, `dead-code`, `document`, `explain-error`, `extract-method`, `migrate-package`, `project-inspection`, `publish-preflight`, `refactor`, `refactor-loop`, `review`, `security`, `session-undo`, `snippet-eval`, `test-coverage`, `test-triage`, `update`. If the live `skills/` tree differs, trust the live directory and record skill drift (this list is a convenience snapshot, not a second source of truth).

**Per-skill verification (run for each skill):**

1. **Frontmatter parity.** Read `skills/<name>/SKILL.md` and verify:
   - The frontmatter `name` matches the directory name.
   - The frontmatter `description` is non-empty and accurately describes the skill (not a stub).
2. **Tool reference validity.** Extract every MCP tool name the skill body mentions (typically in `Call \`tool_name\`` form or in workflow tables). Cross-check each against the live catalog's tool list. Any reference to a tool that is **not** in the live catalog (renamed, removed, or never shipped) is a **P2 FAIL** and must be filed in *MCP server issues* with severity `incorrect result` and referenced in *Skills audit*.
3. **Workflow dry-run.** Pick a realistic input for the skill (reuse evidence from earlier phases where possible — e.g. a loaded workspace for `analyze`, a diagnostic id for `explain-error`, a symbol handle for `refactor`) and mentally walk the skill's workflow against the loaded workspace. Does each tool call have the data it needs? Does the sequence terminate cleanly or leak state? Does the output format actually match what the workflow produces? Mark `dry_run` as `pass`, `flag`, or `fail`.
4. **Safety rules.** Skills that mutate workspace or disk (`refactor`, `migrate-package`, `code-actions`, `extract-method`, `session-undo` when applying revert/reload, etc.) should require preview before apply where applicable and should call `compile_check` or equivalent verification after apply. Mark `safety_rules` as `pass`, `flag`, or `fail`.
5. **Doc consistency.** Does the output format in the skill body reference fields that the exercised tools actually return? A skill that claims to surface `cohesion.score` when `get_cohesion_metrics` returns `Score` is a FLAG, not a FAIL, but it should be captured.

For every skill, append one row to the **Skills audit** table in the report with columns `skill`, `frontmatter_ok` (yes/no), `tool_refs_valid` (yes/no + count of invalid refs), `dry_run` (pass/flag/fail), `safety_rules` (pass/flag/fail/na), and `notes`.

**MCP audit checkpoint:** Do all live skills have frontmatter that matches their directory name? Do any skills reference removed or renamed tools? Does each skill's workflow actually produce the output it claims? Do mutation skills enforce preview→apply+verify? Were any new skills added that are not covered by this prompt's appendix? If the plugin repo was not accessible, is this phase correctly `blocked` in the report with a clear reason?

---

### Phase 17: Boundary & Negative Testing

Deliberately probe edge cases and error paths. The goal is to verify that the server validates inputs, returns helpful error messages, and does not crash on bad data.

#### 17a. Invalid Identifiers
1. Call a workspace-scoped tool (e.g. `workspace_status`) with a **non-existent workspace id**. Verify the error is actionable. **Server v1.8+:** the error envelope's `tool` field carries the actual tool name (or resource URI for resource handlers), not `"unknown"`.
2. Call `find_references` (or `symbol_info`) with a **fabricated symbol handle**. Verify it returns a clear error, not a crash or empty success. **Server v1.8+:** a structurally valid but unfindable handle (e.g. base64 of `{"MetadataName":"NonExistentNamespace.NonExistentType"}`) now produces a structured `category: NotFound` envelope. Pre-fix this returned `{count:0, totalCount:0, references:[]}` and callers couldn't tell junk handles from "valid handle, zero references". `find_consumers`, `find_type_usages`, `find_implementations`, `find_overrides`, `find_base_members`, `impact_analysis`, and `find_type_mutations` share the same contract.
3. Call `rename_preview` with the same fabricated handle. Verify error handling.

#### 17b. Out-of-Range Positions
1. Call `go_to_definition` with a **line number beyond the end of the file**. Verify the error.
2. Call `enclosing_symbol` at **line 0, column 0** (potential off-by-one). Verify behavior.
3. Call `analyze_data_flow` with **startLine > endLine**. Verify it rejects or handles gracefully.

#### 17c. Empty and Degenerate Inputs
1. Call `symbol_search` with an **empty string** query. Verify it returns an empty result or a clear error, not a crash.
2. Call `analyze_snippet` with an **empty string** code body. Verify behavior.
3. Call `evaluate_csharp` with an **empty string**. Verify behavior.

#### 17d. Stale and Double-Apply
1. Call any `*_apply` tool with a **preview token from a previous phase** that has already been applied. Verify it rejects the stale token.
2. Create a **fresh preview token**, then advance the workspace version with a separate successful low-impact mutation that the current audit mode allows. Attempt to apply the older token and verify a clear workspace-version/staleness rejection. If this is unsafe in `conservative` mode, mark this sub-step `skipped-safety` rather than faking it.
3. Call `revert_last_apply` **twice** in succession. Verify the second call returns a clear "nothing to revert" response, not an error.

#### 17e. Post-Close Operations
1. Call `workspace_close` to close the session.
2. Call `workspace_status` with the **now-closed workspace id**. Verify the error is clear.
3. Call `workspace_load` to re-open the workspace for subsequent phases (or confirm session is still active for the remaining phases if you defer this sub-phase to the end).

**MCP audit checkpoint:** For every error path tested: Did the server return an actionable error message or did it crash / return a vague MCP error? Did stale-token and workspace-version mismatch cases fail cleanly? Rate each error message as **actionable**, **vague**, or **unhelpful**. Were there any 500-level or unhandled exceptions? Did any bad input cause the server to enter a degraded state affecting subsequent calls?

---

### Phase 18: Regression Verification (re-test known issues or prior findings)

Before writing the report, deliberately re-test 3–5 previously recorded issues. Prefer [`ai_docs/backlog.md`](../backlog.md) when auditing **Roslyn-Backed-MCP** or when that file is available. Otherwise use a previous audit report, open issue tracker, or saved repro list. If no prior source exists, mark this phase `N/A` with that reason.

1. Read the available prior source and select 3–5 items that can be reproduced with the current workspace.
2. For each selected item, reproduce the exact scenario described (same tool, similar inputs). Record the result:
   - **Still reproduces** — confirm with current server version.
   - **Partially fixed** — describe what changed and what remains.
   - **No longer reproduces** — mark as **candidate for closure** in the report.
3. Include findings in the report's **Known issue regression check** section.

**MCP audit checkpoint:** Did any previously reported issues get fixed without being removed from the source tracker? Did any issues change in character (e.g., different error message, partial fix)?

---

### Final surface closure (mandatory before the report)

1. Compare your coverage ledger against the live catalog captured in Phase 0.
2. For every unaccounted tool, resource, or prompt, either call it now or assign a final explicit status (`skipped-repo-shape`, `skipped-safety`, or `blocked`) with a one-line reason.
3. If the live catalog exposed entries that had no natural place in the phased plan, record that as prompt drift in **Improvement suggestions** and still include the entries in the ledger.
4. Confirm that all audit-only mutations from Phases **8b**, 9-13 were reverted or cleaned up. Only intentional Phase 6 product improvements should remain. (Phase 8b's writer-reclassification probes and the W2 `set_editorconfig_option` probe must each be reverted; the W1 `format_document_apply` probe is the audit-only apply that Phase 9 reverts.)
5. Confirm that ledger totals match the live catalog totals and that the catalog summary matches `server_info`.
6. Confirm the **Concurrency matrix** is fully populated when Phase 8b ran, with `N/A` (and a one-line reason) in cells the audit could not fill.
7. Confirm the **Debug log capture** section either has at least one entry or explicitly states `client did not surface MCP log notifications`.
8. **Compute the Experimental promotion scorecard.** For each experimental tool/resource/prompt seeded in Phase 0, compute one of the four recommendations using the rubric below. Do not write the report until every experimental row has a recommendation.
   - **`promote`** (ready for stable tier) — must satisfy ALL of: (a) exercised end-to-end including at least one non-default parameter path, (b) schema matched behaviour on every probe, (c) no FAIL-grade findings in this run or prior backlog, (d) p50 `elapsedMs` within budget (single-symbol reads ≤5 s, solution scans ≤15 s, writers ≤30 s), (e) for preview/apply families, the apply sibling was round-tripped cleanly, (f) error path produced an actionable message on at least one negative probe, (g) catalog description matched actual behaviour.
   - **`keep-experimental`** (working but not yet stable) — exercised with pass signal but missing at least one of the `promote` criteria (typically: writer round-trip not performed this run, or `conservative` mode gated the apply sibling, or a non-default parameter path wasn't probed).
   - **`needs-more-evidence`** — not exercised in this run (`skipped-repo-shape`, `skipped-safety`, or `blocked`) OR the single exercise was too shallow to judge. This is the default for `blocked` entries.
   - **`deprecate`** — exercised and produced FAIL-grade findings that warrant removal rather than stabilization. Pair with a *MCP server issues* entry and, optionally, a backlog draft.
9. Confirm the **Schema vs behaviour drift**, **Error message quality**, **Parameter-path coverage**, and **Performance baseline** tables are populated. Every exercised tool contributes at least one row to Performance baseline; Schema/Error/Parameter tables can be empty-with-reason if nothing was observed.
10. Confirm the **Skills audit** table has one row per skill (or the phase is `blocked` with a clear reason).
11. Confirm the **Prompt verification** table has one row per exercised prompt.

---

When finished with all phases, call `workspace_close` to release the session (if not already closed during Phase 17e).

---

## Output Format — MANDATORY

### What goes in git (no audit file)

Meaningful refactoring from **Phase 6** is committed in the **target solution’s** repository. A short **Phase 6 refactor summary** in the MCP audit report (see Report contents) ties git history to tools exercised; the code changes themselves are not a separate file deliverable.

### What goes in a file (incremental draft + final canonical output)

You **MUST** maintain a draft file at `<canonical-path>.draft.md` and **append findings to it after every phase**. The draft is your audit log — never revisit prior phases by re-running tool calls; read from the draft to recall what you found. When all phases complete, atomically rename the draft to the canonical name (`<canonical-path>`).

The audit is **not finished** until **both** of the following are true:

1. The canonical file exists on disk at `<canonical-path>`.
2. The draft was continuously updated as each phase completed (never a one-shot final flush from working memory at the end of the run).

A one-shot final flush from working memory is the most common symptom of a broken run — the model accumulates ~1–2 MB of tool results in active context, then runs out of room when it tries to serialize the report. Persisting after every phase keeps each turn within the output budget and lets the next turn drop the prior phase's tool-result bulk from active reasoning.

This prompt writes **raw per-run evidence only**. If you are running a multi-repo audit campaign, synthesize the cross-repo findings later into a separate rollup under `ai_docs/reports/`; do **not** write the raw audit file there.

### Where to save the report

Save the file in the **Roslyn-Backed-MCP** repository (the repo that contains this prompt and ships the MCP server), **not** necessarily the target solution’s repo:

**Canonical path:** `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/<timestamp>_<repo-id>_mcp-server-audit.md`

- `<timestamp>` is the current UTC time in `yyyyMMddTHHmmssZ`. This makes raw audit files immutable and easy to sort during later rollups.
- If your workspace **is** Roslyn-Backed-MCP, use `ai_docs/audit-reports/` relative to that repo root.
- If you are auditing another solution in a different workspace and **cannot** write to Roslyn-Backed-MCP, write the file to the **current workspace root** as `ai_docs/audit-reports/<timestamp>_<repo-id>_mcp-server-audit.md` (create `ai_docs/audit-reports/` if needed) **and** state in the report header: *“Intended final path: `<path-to-Roslyn-Backed-MCP>/ai_docs/audit-reports/…` — copy this file there before creating a rollup or backlog update.”*
- When the run happens outside Roslyn-Backed-MCP, import the raw file back into the canonical store with `eng/import-deep-review-audit.ps1 -AuditFiles <path>` before generating any rollup.
- Do **not** place raw audit files under `ai_docs/reports/`; that directory is for synthesized rollups and actioning summaries.

### Naming scheme (`<timestamp>_<repo-id>`)

Derive `<timestamp>` from the current UTC time in `yyyyMMddTHHmmssZ`.

Derive `<repo-id>` from the **audited** solution or repository name:
- Strip the `.sln` / `.slnx` / `.csproj` extension
- Lowercase; replace spaces and dots with hyphens
- Examples: `20260406T154500Z_itchatbot_mcp-server-audit.md`, `20260406T154500Z_firewallanalyzer_mcp-server-audit.md`, `20260406T154500Z_roslyn-backed-mcp_mcp-server-audit.md`

### Report contents (required sections)

The report is consumed by **downstream agents**, not humans browsing prose. Prefer dense tables, fixed schemas, and one-line entries. Avoid narrative paragraphs unless an issue genuinely requires them.

**Mandatory sections (always appear in full):**

| # | Section | Purpose |
|---|---------|---------|
| 1 | Header | Run metadata — server, client, scale, mode, debug log channel |
| 2 | Coverage summary | Group counts by live `Kind` + `Category` |
| 3 | Coverage ledger | One row per live tool/resource/prompt with tier + status + elapsedMs |
| 4 | Verified tools | Tested-and-working list |
| 5 | Phase 6 refactor summary | Applied product changes |
| 6 | Performance baseline | `_meta.elapsedMs` per exercised tool (p50 when paged) |
| 7 | Schema vs behaviour drift | Cross-cutting principle #2 output slot |
| 8 | Error message quality | Cross-cutting principle #4 output slot |
| 9 | Parameter-path coverage | Cross-cutting principle #6 output slot |
| 10 | Prompt verification | Phase 16 per-prompt table |
| 11 | Skills audit | Phase 16b per-skill table |
| 12 | Experimental promotion scorecard | Per-experimental-entry recommendation |
| 13 | Debug log capture | Phase 0 channel output |
| 14 | MCP server issues (bugs) | Per-issue detail |
| 15 | Improvement suggestions | Actionable UX/output enrichment |

**Conditional sections (populate when data exists, otherwise emit `**N/A — <reason>**`):**

| # | Section | Populate when |
|---|---------|---------------|
| 16 | Concurrency matrix (Phase 8b) | Phase 8b ran with at least sequential baselines |
| 17 | Writer reclassification verification | Phase 8b.5 exercised writers |
| 18 | Response contract consistency | Cross-cutting principle #5 observed at least one inconsistency |
| 19 | Known issue regression check (Phase 18) | Prior source existed |
| 20 | Known issue cross-check | New findings matched an existing backlog/issue id |

The historical "audit incomplete if any section is missing" framing forced the agent to materialize 50-row N/A tables at the end of every run, which contributed to the final-flush stall described in cross-cutting principles #10–#12. Conditional sections collapse to a single `**N/A — <reason>**` line when unpopulated; mandatory sections always render in full.

**Per-section detail:**

1. **Header (metadata)** — server version (from `server_info`), Roslyn / .NET versions if available, MCP client name/version if available, **audited** solution path and name, audited repo revision/branch if available, chosen entrypoint (`.sln` / `.slnx` / `.csproj`), audit mode (`full-surface` by default or explicitly opted-in `conservative`), disposable isolation path or conservative rationale, date, workspace id, approximate project count and document count from `workspace_load` / `project_graph`, repo-shape summary, the prior-issue source used for Phase 18, **debug log channel availability** (`yes` / `no` / `partial`), and **plugin skills repo path** (used for Phase 16b — or `blocked` with reason).
2. **Coverage summary** — Group by the live catalog's `Kind` + `Category` values from `roslyn://server/catalog` (not the convenience groupings in this prompt's appendix). For each group, report counts for `exercised`, `exercised-apply`, `exercised-preview-only`, `skipped-repo-shape`, `skipped-safety`, and `blocked`.
3. **Coverage ledger (required)** — One row for every live tool, resource, and prompt from `roslyn://server/catalog`. Columns: `kind`, `name`, `tier`, `category`, `status`, `phase`, `lastElapsedMs`, `notes`. The audit is incomplete if the ledger row count does not match the live catalog total or if any entry is omitted. For `blocked` rows, say whether the blocker was a client limitation, workspace-load failure, or server/runtime fault.
4. **Verified tools (working)** — Bullet list: tool name + one-line evidence (e.g. "`compile_check` — 0 errors after Phase 6, p50 elapsedMs=180"). This distinguishes "tested and fine" from "never called."
5. **Phase 6 refactor summary** — Concise record of **applied** Phase 6 work (not preview-only phases). Include: target repo identity (if different from Roslyn-Backed-MCP); **scope** (e.g. fix-all + rename + format — which sub-steps 6a–6i you actually applied); **notable symbols or files** touched; **MCP tools used** for each change (e.g. `rename_apply`, `fix_all_apply`); **verification** (`compile_check` / `test_run` / `build_workspace` outcomes). If Phase 6 had **no** applies (skipped or audit-only), state **N/A** with one line why. Optional: git commit hash or PR link if available.
6. **Performance baseline (`_meta.elapsedMs`)** — Required. One row per exercised tool with columns `tool`, `tier`, `category`, `calls`, `p50_elapsedMs`, `p90_elapsedMs`, `max_elapsedMs`, `input_scale`, `budget_status` (`within` / `warn` / `exceeded`), `notes`. Budget thresholds: single-symbol reads ≤5 s (`within`), ≤15 s (`warn`), >15 s (`exceeded`); solution-wide scans ≤15 s/≤30 s/>30 s; writers ≤30 s/≤60 s/>60 s. If `_meta.elapsedMs` is not available in the response, record `N/A — client strips _meta` once at the top of the table and fall back to external wall-clock where possible.
7. **Schema vs behaviour drift** — Required. One row per observed mismatch with columns `tool`, `mismatch_kind` (missing_param / wrong_default / undocumented_param / return_shape / description_stale), `expected`, `actual`, `severity` (FLAG / FAIL), `notes`. Empty table when no mismatches observed — write a single line `No schema/behaviour drift observed.`
8. **Error message quality** — Required. One row per negative-path probe with columns `tool`, `probe_input`, `rating` (actionable / vague / unhelpful), `suggested_fix`, `notes`. Phase 17 (boundary testing) contributes the bulk of rows; negative probes from other phases also belong here.
9. **Parameter-path coverage** — Required. One row per family with columns `family`, `non_default_path_tested` (parameter + value), `status` (pass / flag / fail / na-schema-does-not-expose), `notes`. Families at minimum: diagnostics (`project_diagnostics` severity + paging), `compile_check` (`emitValidation=true`), analyzer listing (paging), search families, prompt/resource families.
10. **Prompt verification** — Required. One row per exercised prompt from Phase 16 with columns `prompt`, `schema_ok`, `actionable`, `hallucinated_tools` (count), `idempotent`, `elapsedMs`, `recommendation_seed` (`promote` / `keep-experimental` / `needs-more-evidence` / `deprecate`), `notes`. The `recommendation_seed` feeds the promotion scorecard in section 12.
11. **Skills audit** — Required when Phase 16b ran. One row per live skill with columns `skill`, `frontmatter_ok`, `tool_refs_valid` (yes/no + invalid count), `dry_run`, `safety_rules`, `notes`. When Phase 16b was `blocked` because the plugin repo wasn't accessible, write a single line `Phase 16b blocked — <reason>`; do not fake rows.
12. **Experimental promotion scorecard** — Required. One row per experimental tool/resource/prompt with columns `kind`, `name`, `category`, `status_from_ledger`, `p50_elapsedMs`, `schema_ok`, `error_ok`, `round_trip_ok` (applicable to preview/apply families — otherwise `n/a`), `failures` (count of FAIL findings that reference this entry), `recommendation` (`promote` / `keep-experimental` / `needs-more-evidence` / `deprecate`), `evidence` (one-line citation — phase number, tool output snippet, or backlog id). Empty rows are not allowed for experimental entries — `needs-more-evidence` is the default for unexercised ones.
13. **Debug log capture** — Required. Verbatim copy of every `Warning`/`Error`/`Critical` MCP log notification surfaced during the audit, plus any `Information` notification that mentions workspace lifecycle, gate acquisition, lock contention, rate-limit hits, or request timeouts. Each entry on its own line with `correlationId` (if present), `timestamp`, `level`, `logger`, and `message`. If the client did not surface log notifications, state `client did not surface MCP log notifications` here and in the header.
14. **MCP server issues (bugs)** — Required. For each issue: **Tool name**, **inputs**, **expected** vs **actual**, **severity** (crash / incorrect result / missing data / degraded performance / cosmetic / error-message-quality), **reproducibility** (always / sometimes / once). Watch for: crashes, wrong or incomplete results, tool-vs-tool inconsistency, missing functionality, slow tools, bad JSON/truncation, bad line/position mapping, unhelpful error messages. If there are zero new issues, state `**No new issues found**` as the sole entry and confirm the audit ran.
15. **Improvement suggestions** — Required. Things that work correctly but could work better. UX friction (confusing parameter names, unexpected defaults, verbose output). Missing convenience features (e.g., "I wished tool X also returned Y"). Workflow gaps (sequences of 3+ tool calls that could be a single composite tool). Output enrichment (e.g., numeric codes where string labels would be more useful). Schema/description mismatches (tool description says one thing, actual behavior does another). These are not bugs — they are actionable input for the next release.
16. **Concurrency matrix (Phase 8b)** — Conditional. Sub-tables (probe set, sequential baseline, parallel fan-out + behavioral verification) using the schema in the markdown template below. Every cell filled or `N/A`. If Phase 8b was `blocked` for the entire run, write a one-line reason in this section instead of the tables and continue.
17. **Writer reclassification verification (Phase 8b.5)** — Conditional. One-row-per-tool table for the six writers (`apply_text_edit`, `apply_multi_file_edit`, `revert_last_apply`, `set_editorconfig_option`, `set_diagnostic_severity`, `add_pragma_suppression`). Columns: `tool`, `status`, `wall-clock (ms)`, `notes`.
18. **Response contract consistency** — Conditional. One row per observed inconsistency with columns `tools`, `concept`, `inconsistency`, `notes`. Populate only when cross-cutting principle #5 observed at least one inconsistency.
19. **Known issue regression check** — Conditional. For the 3–5 items re-tested in Phase 18, state: **still reproduces**, **partially fixed** (describe), or **candidate for closure** (no longer reproduces). Include the source id / issue id and one-line summary for each. If no prior source existed, collapse to `**N/A — no prior source**`.
20. **Known issue cross-check** — Conditional. List any *newly observed* issues that match the chosen prior source (for example a backlog id, issue number, or previous audit finding) with one line each; put **full write-ups only for new or different** behavior.

### Markdown template (copy, fill in, save)

```markdown
# MCP Server Audit Report

## 1. Header
- **Date:**
- **Audited solution:**
- **Audited revision:** (commit / branch if available)
- **Entrypoint loaded:**
- **Audit mode:** (`full-surface` by default; state reason if `conservative`)
- **Isolation:** (disposable branch/worktree/clone path, or conservative rationale)
- **Client:** (name/version; say if prompt or resource invocation was client-blocked)
- **Workspace id:**
- **Server:** (from `server_info`)
- **Catalog version:** (from `server_info.catalogVersion` — e.g. `2026.04`)
- **Roslyn / .NET:** (if reported)
- **Scale:** ~N projects, ~M documents
- **Repo shape:**
- **Prior issue source:**
- **Debug log channel:** (`yes` — MCP `notifications/message` surfaced; `partial` — only some levels; `no` — client did not surface log notifications)
- **Plugin skills repo path:** (absolute or relative path used for Phase 16b, or `blocked — <reason>`)
- **Report path note:** (if not saved under Roslyn-Backed-MCP, state intended copy destination)

## 2. Coverage summary
| Kind | Category | Tier-stable | Tier-experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-------------|-------------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | workspace | | | | | | | | | |
| tool | refactoring | | | | | | | | | |
| tool | advanced-analysis | | | | | | | | | |
| tool | project-mutation | | | | | | | | | |
| tool | … (one row per live catalog category) | | | | | | | | | |
| resource | workspace | | | | n/a | n/a | | | | |
| prompt | prompts | | | | n/a | n/a | | | | |

## 3. Coverage ledger
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | `workspace_load` | stable | workspace | exercised | 0 | 2250 | |
| tool | `create_file_apply` | experimental | file-operations | skipped-safety | 10 | — | conservative audit on non-disposable repo |
| resource | `server_catalog` | stable | server | exercised | 0, 15 | 6 | |
| resource | `workspace_diagnostics` | stable | workspace | blocked | 15 | — | client cannot read workspace-scoped resources directly |
| prompt | `discover_capabilities` | experimental | prompts | exercised | 16 | 12 | |
| prompt | `security_review` | experimental | prompts | blocked | 16 | — | client does not expose prompt invocation |

## 4. Verified tools (working)
- `tool_name` — one-line observation (include p50 elapsedMs when available)
- …

## 5. Phase 6 refactor summary
- **Target repo:** (name / path; same as audited solution if in-repo)
- **Scope:** (which of 6a–6i applied; or **N/A** — no applies, with reason)
- **Changes:** bullets — symbol/file, tool used (`rename_apply`, etc.)
- **Verification:** `compile_check` / `test_run` / `build_workspace` — outcome
- **Optional:** commit / PR reference

## 6. Performance baseline (`_meta.elapsedMs`)

> One row per exercised tool. If the client strips `_meta`, record `client strips _meta` here and fall back to external wall-clock.

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| `workspace_load` | stable | workspace | 1 | 2250 | 2250 | 2250 | 4 projects, 315 docs | within | |
| | | | | | | | | | |

## 7. Schema vs behaviour drift

> Cross-cutting principle #2. Empty table → write `No schema/behaviour drift observed.`

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| | | | | | |

## 8. Error message quality

> Cross-cutting principle #4. Populated primarily from Phase 17 negative probes; empty table → write `No negative-path probes produced unhelpful errors.`

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| | | | | |

## 9. Parameter-path coverage

> Cross-cutting principle #6. At least one non-default path per major family when the schema exposes it.

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `project_diagnostics` | project / file / severity / paging | | |
| `compile_check` | `emitValidation=true` | | |
| Analyzer listing | paging / project filter | | |
| Search families | alternate `kind` / filter | | |
| Prompts / resources | client-blocked vs exercised | | |

## 10. Prompt verification (Phase 16)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | | | | | | | |
| `suggest_refactoring` | | | | | | | |
| `review_file` | | | | | | | |
| `discover_capabilities` | | | | | | | |
| `analyze_dependencies` | | | | | | | |
| `debug_test_failure` | | | | | | | |
| `refactor_and_validate` | | | | | | | |
| `fix_all_diagnostics` | | | | | | | |
| `guided_package_migration` | | | | | | | |
| `guided_extract_interface` | | | | | | | |
| `security_review` | | | | | | | |
| `dead_code_audit` | | | | | | | |
| `review_test_coverage` | | | | | | | |
| `review_complexity` | | | | | | | |
| `cohesion_analysis` | | | | | | | |
| `consumer_impact` | | | | | | | |
| `guided_extract_method` | | | | | | | |
| `msbuild_inspection` | | | | | | | |
| `session_undo` | | | | | | | |
| `refactor_loop` | | | | | | | |

## 11. Skills audit (Phase 16b)

> Required when Phase 16b ran. If the plugin repo was not accessible, replace the table with a single line: `Phase 16b blocked — <reason>`.

| Skill | frontmatter_ok | tool_refs_valid (invalid_count) | dry_run | safety_rules | Notes |
|-------|----------------|----------------------------------|---------|--------------|-------|
| `analyze` | | | | na | |
| `complexity` | | | | na | |
| `dead-code` | | | | na | |
| `document` | | | | na | |
| `explain-error` | | | | na | |
| `migrate-package` | | | | | |
| `refactor` | | | | | |
| `refactor-loop` | | | | | |
| `review` | | | | na | |
| `security` | | | | na | |
| `test-coverage` | | | | na | |

## 12. Experimental promotion scorecard

> One row per experimental entry. `needs-more-evidence` is the default for blocked/unexercised rows. Rubric in `ai_docs/prompts/deep-review-and-refactor.md` → Final surface closure step 8.

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `semantic_search` | advanced-analysis | exercised | | | | n/a | | | |
| tool | `apply_text_edit` | editing | exercised-apply | | | | | | | |
| tool | `create_file_apply` | file-operations | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | conservative audit — no disposable checkout |
| prompt | `discover_capabilities` | prompts | exercised | | | n/a | n/a | | | |
| … | | | | | | | | | | |

## 13. Debug log capture

> Verbatim copy of structured `notifications/message` log entries surfaced by the MCP client during the audit. Filter: every `Warning`/`Error`/`Critical` entry + any `Information` entry that mentions workspace lifecycle, gate acquisition, lock contention, rate-limit hits, or request timeouts. If the client did not surface log notifications, write: `client did not surface MCP log notifications` and skip the table.

| `timestamp` | `level` | `logger` | `correlationId` | `eventName` | `message` | Phase | Tool in flight |
|-------------|---------|----------|-----------------|-------------|-----------|-------|----------------|
| | | | | | | | |

## 14. MCP server issues (bugs)

### 14.1 (title or tool name)
| Field | Detail |
|--------|--------|
| Tool | |
| Input | |
| Expected | |
| Actual | |
| Severity | |
| Reproducibility | |

(repeat per issue, or write `**No new issues found**` when none)

## 15. Improvement suggestions
- `tool_name` — suggestion (UX friction / missing feature / workflow gap / output enrichment / schema mismatch)
- …

## 16. Concurrency matrix (Phase 8b)

> Conditional. Required when Phase 8b ran. If Phase 8b was `blocked` for the entire run, replace these tables with one line: `Phase 8b blocked — <reason>`.

### Concurrency probe set
| Slot | Tool | Inputs (concise) | Classification | Notes |
|------|------|------------------|----------------|-------|
| R1 | `find_references` | symbol=…, file=…, line=…, col=… | reader | hot symbol used N times |
| R2 | `project_diagnostics` | no filters | reader | |
| R3 | `symbol_search` | query=…, limit=… | reader | |
| R4 | `find_unused_symbols` | includePublic=false | reader | |
| R5 | `get_complexity_metrics` | no filters | reader | |
| W1 | `format_document_preview` → `format_document_apply` | filePath=… | writer | Phase 6-touched file |
| W2 | `set_editorconfig_option` (then revert) | key=…, value=… | writer (reclassified) | |

### Sequential baseline (single call wall-clock, ms)
| Slot | Wall-clock (ms) | Notes |
|------|------------------|-------|
| R1 | | |
| R2 | | |
| R3 | | |
| R4 | | |
| R5 | | |
| W1 | | |
| W2 | | |

### Parallel fan-out and behavioral verification
- **Host logical cores:** _(record before filling the table — drives the chosen N)_
- **Chosen N (fan-out):** _N = `min(4, max(2, logical_cores))`_

| Slot | Parallel wall-clock (ms) | Speedup vs baseline | Expected (≥`0.7 × N`) | Pass / FLAG / FAIL | Notes |
|------|---------------------------|----------------------|------------------------|---------------------|-------|
| R1 ×N | | | ≥`0.7 × N` | | |
| R2 ×N | | | ≥`0.7 × N` | | |
| R3 ×N | | | ≥`0.7 × N` | | |

### Read/write exclusion behavioral probe (Phase 8b.3)
| Probe | Observed (`waits` / `does-not-wait`) | Expected | Pass / FLAG / FAIL | Notes |
|-------|---------------------------------------|----------|---------------------|-------|
| Reader → Writer (start reader, then writer) | | both `waits` (per-workspace `AsyncReaderWriterLock`) | | |
| Writer → Reader (start writer, then reader) | | both `waits` (per-workspace `AsyncReaderWriterLock`) | | |

### Lifecycle stress (Phase 8b.4)
| Probe | Observed (`waits-for-reader` / `runs-concurrently` / `errors`) | Reader saw (`stale` / `fresh` / `error`) | Reader exception (if any) | `correlationId` | Expected | Pass / FLAG / FAIL | Notes |
|-------|---------|---------|----------|-----------------|----------|---------------------|-------|
| reader + `workspace_reload` | | | | | `waits-for-reader` (per-workspace write lock) | | |
| reader + `workspace_close` | | | | | `waits-for-reader`; reader completes cleanly | | |

## 17. Writer reclassification verification (Phase 8b.5)

> Conditional. One row per writer. If Phase 8b.5 was blocked, collapse to `**N/A — <reason>**`.

| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|
| 1 | `apply_text_edit` | | | |
| 2 | `apply_multi_file_edit` | | | |
| 3 | `revert_last_apply` | | | shared with Phase 9 evidence |
| 4 | `set_editorconfig_option` | | | |
| 5 | `set_diagnostic_severity` | | | |
| 6 | `add_pragma_suppression` | | | |

## 18. Response contract consistency

> Conditional. Populate only when cross-cutting principle #5 observed at least one inconsistency; otherwise collapse to `**N/A — no response-contract inconsistencies observed**`.

| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|
| | | | |

## 19. Known issue regression check (Phase 18)

> Conditional. `**N/A — no prior source**` when no prior backlog/audit/issue source existed.

| Source id | Summary | Status |
|-----------|---------|--------|
| issue-or-backlog-id | one-line summary | still reproduces / partially fixed / candidate for closure |

## 20. Known issue cross-check

> Conditional. Bullet list of *newly observed* issues that match an existing backlog id / issue id. Full write-ups go in section 14.
- …
```

### Completion gate

The file must exist at the canonical path above (or the documented fallback). Create `ai_docs/audit-reports/` if missing. The task is **incomplete** without this file.

---

## Appendix — Live surface reference (2026.04)

> **Maintenance note:** This appendix **mirrors** the live catalog categories (`Category`) and tier (`SupportTier`) values emitted by `roslyn://server/catalog`. The live catalog from Phase 0 is authoritative. Treat this appendix as a convenience snapshot; if it drifts from the running server, record prompt drift in *Improvement suggestions* and trust the live catalog.
> When tools, resources, prompts, or skills change, update this section **and** `eng/verify-ai-docs.ps1` passes.
> Client limitations do **not** change appendix counts. If a client cannot invoke prompts or resources, record those entries as `blocked` in the audit ledger rather than editing them out.
> Last verified: 2026-04-17 against catalog version 2026.04 (post-v1.23.0) — **147 tools (104 stable / 43 experimental) | 13 resources (9 stable / 4 experimental) | 20 prompts (all experimental) | 22 plugin skills** (skills are repo files under `skills/`, not catalog rows).

### Tools by live catalog category (147 total)

The `Category` column below is the exact value emitted by `roslyn://server/catalog` — use these strings in the coverage summary, not the legacy convenience groupings. Tools with a preview/apply pairing are kept together and their pair is noted in the *Pair* column.

**Legend:** `S` = stable, `E` = experimental. `RO` = read-only. `D` = destructive (mutates workspace or on-disk state).

#### `server` (1 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `server_info` | S | RO | Surface inventory, tiers, runtime. |

#### `workspace` (10 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `workspace_load` | S | RO | Lean summary default; `verbose=true` for project tree. |
| `workspace_reload` | S | RO | Takes per-workspace write lock. |
| `workspace_close` | S | RO | Takes per-workspace write lock. |
| `workspace_list` | S | RO | Heartbeat tool between phases. |
| `workspace_status` | S | RO | Lean summary default. |
| `project_graph` | S | RO | Project dependency graph. |
| `source_generated_documents` | S | RO | Lists source-gen outputs. |
| `get_source_text` | S | RO | Read a file from workspace. |
| `workspace_changes` | S | RO | Session mutation history (v1.10.0+). Promoted after 3/4 audit exercises with zero failures. |

#### `symbols` (16 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `symbol_search` | S | RO | Substring match (case-insensitive). |
| `symbol_info` | S | RO | |
| `go_to_definition` | S | RO | |
| `goto_type_definition` | S | RO | |
| `find_references` | S | RO | Paginated. |
| `find_references_bulk` | S | RO | Bulk counterpart. |
| `find_implementations` | S | RO | |
| `find_overrides` | S | RO | |
| `find_base_members` | S | RO | |
| `member_hierarchy` | S | RO | |
| `document_symbols` | S | RO | |
| `enclosing_symbol` | S | RO | |
| `symbol_signature_help` | S | RO | `preferDeclaringMember` auto-promote. |
| `symbol_relationships` | S | RO | `preferDeclaringMember` auto-promote. |
| `find_property_writes` | S | RO | |
| `get_completions` | S | RO | Ranked v1.8+. |

#### `analysis` (12 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `project_diagnostics` | S | RO | Invariant totals under severity filter (v1.8+). |
| `diagnostic_details` | S | RO | |
| `type_hierarchy` | S | RO | |
| `callers_callees` | S | RO | |
| `impact_analysis` | S | RO | Paginated refs + declarations (FLAG-3D). |
| `find_type_mutations` | S | RO | `MutationScope` enrichment (v1.8+). |
| `find_type_usages` | S | RO | |
| `find_consumers` | S | RO | |
| `get_cohesion_metrics` | S | RO | Source-gen partial exclusion (v1.8+). |
| `find_shared_members` | S | RO | |
| `list_analyzers` | S | RO | Paginated rules. |
| `analyze_snippet` | S | RO | Ephemeral workspace; wrapper-relative positions fixed v1.7+ (FLAG-C). |

#### `advanced-analysis` (11 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `find_unused_symbols` | S | RO | |
| `get_di_registrations` | S | RO | |
| `get_complexity_metrics` | S | RO | |
| `find_reflection_usages` | S | RO | |
| `get_namespace_dependencies` | S | RO | Large output — page carefully. |
| `get_nuget_dependencies` | S | RO | |
| `semantic_search` | S | RO | Promoted v1.9.0; verbose-query fallback + exact-match predicate. |
| `analyze_data_flow` | S | RO | Promoted v1.9.0; expression-bodied members supported. |
| `analyze_control_flow` | S | RO | Promoted v1.9.0; expression-body synthesis. |
| `get_operations` | S | RO | Column must point at syntax token (UX-003). Promoted after 3/4 audit exercises with zero failures. |
| `suggest_refactorings` | S | RO | Ranked suggestions from complexity, cohesion (LCOM4), and unused symbols (v1.11.0+). Promoted after 3/4 audit exercises with zero failures. |

#### `security` (3 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `security_diagnostics` | S | RO | OWASP-tagged. |
| `security_analyzer_status` | S | RO | |
| `nuget_vulnerability_scan` | S | RO | Requires .NET 8+ SDK. |

#### `validation` (8 stable, 1 experimental — +1 experimental via v1.18)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `build_workspace` | S | RO | dotnet build wrapper. |
| `build_project` | S | RO | |
| `test_discover` | S | RO | Paginated (BUG-007). |
| `test_run` | S | RO | v1.17+ fast-fails on MSB3027/MSB3021 file lock (opt out via `ROSLYNMCP_FAST_FAIL_FILE_LOCK=false`). Backlog: `test-run-failure-envelope`. |
| `test_related` | S | RO | |
| `test_related_files` | S | RO | |
| `test_coverage` | S | RO | Requires coverlet.collector. |
| `compile_check` | S | RO | Paginated v1.7+; `emitValidation=true` forces PE emit. |
| `validate_workspace` | E | RO | New v1.18.0 — one-call composite: `compile_check` + error-severity diagnostics + `test_related_files` (+ optional `test_run`). Emits `overallStatus = clean / compile-error / analyzer-error / test-failure`. |

#### `refactoring` (13 stable, 14 experimental — +4 experimental via v1.17/v1.18)
| Tool | Tier | RO/D | Pair | Notes |
|------|------|------|------|-------|
| `rename_preview` | S | RO | — | |
| `rename_apply` | S | D | `rename_preview` | Revertible. |
| `organize_usings_preview` | S | RO | — | |
| `organize_usings_apply` | S | D | `organize_usings_preview` | Revertible. |
| `format_document_preview` | S | RO | — | |
| `format_document_apply` | S | D | `format_document_preview` | Revertible. |
| `code_fix_preview` | S | RO | — | |
| `code_fix_apply` | S | D | `code_fix_preview` | Revertible. |
| `fix_all_preview` | E | RO | — | |
| `fix_all_apply` | E | D | `fix_all_preview` | Revertible. |
| `format_range_preview` | S | RO | — | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `format_range_apply` | E | D | `format_range_preview` | Revertible. |
| `extract_interface_preview` | E | RO | — | |
| `extract_interface_apply` | E | D | `extract_interface_preview` | Revertible. |
| `extract_type_preview` | S | RO | — | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `extract_type_apply` | E | D | `extract_type_preview` | Revertible. |
| `move_type_to_file_preview` | S | RO | — | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `move_type_to_file_apply` | E | D | `move_type_to_file_preview` | Revertible. |
| `bulk_replace_type_preview` | S | RO | — | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `bulk_replace_type_apply` | E | D | `bulk_replace_type_preview` | Revertible. |
| `extract_method_preview` | S | RO | — | Promoted v1.16.0; custom DataFlowAnalysis-based extraction (v1.10.0+). |
| `extract_method_apply` | E | D | `extract_method_preview` | Revertible. |
| `format_check` | E | RO | — | New v1.16.0 — solution-wide format verification (no apply). |
| `restructure_preview` | E | RO | — | New v1.17.0 — syntax-tree pattern-based find-and-replace using `__name__` placeholder captures. |
| `replace_string_literals_preview` | E | RO | — | New v1.17.0 — rewrites string literals in argument/initializer/attribute-argument/return positions to a constant; skips XML doc, interpolated-string holes, `nameof()`. |
| `change_signature_preview` | E | RO | — | New v1.18.0 — add/remove/rename parameter with callsite rewrite. Reorder is not supported; use `symbol_refactor_preview` to stage remove + add when you need it. |
| `symbol_refactor_preview` | E | RO | — | New v1.18.0 — composite preview chaining `rename` + `edit` + `restructure` operations in order; max 25 ops / 500 affected files per token. |

#### `cross-project-refactoring` (0 stable, 3 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `move_type_to_project_preview` | E | RO | Preview-only today. |
| `extract_interface_cross_project_preview` | E | RO | Preview-only today. |
| `dependency_inversion_preview` | E | RO | Preview-only today. |

#### `orchestration` (0 stable, 4 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `migrate_package_preview` | E | RO | |
| `split_class_preview` | E | RO | |
| `extract_and_wire_interface_preview` | E | RO | |
| `apply_composite_preview` | E | D | **Destructive despite the name** — driver for composite apply. |

#### `file-operations` (3 stable, 3 experimental)
| Tool | Tier | RO/D | Pair | Notes |
|------|------|------|------|-------|
| `create_file_preview` | S | RO | — | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `create_file_apply` | E | D | `create_file_preview` | Not revertible. |
| `delete_file_preview` | S | RO | — | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `delete_file_apply` | E | D | `delete_file_preview` | Not revertible. |
| `move_file_preview` | S | RO | — | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `move_file_apply` | E | D | `move_file_preview` | Not revertible. |

#### `project-mutation` (12 stable, 2 experimental — includes MSBuild evaluators)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `add_package_reference_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `remove_package_reference_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `add_project_reference_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `remove_project_reference_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `set_project_property_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `set_conditional_property_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `add_target_framework_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `remove_target_framework_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `add_central_package_version_preview` | E | RO | |
| `remove_central_package_version_preview` | S | RO | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `apply_project_mutation` | E | D | Composite apply for project-file previews. |
| `evaluate_msbuild_property` | S | RO | Note: lives under `project-mutation`, not a dedicated `msbuild` category. Promoted after 3/4 audit exercises with zero failures. |
| `evaluate_msbuild_items` | S | RO | Promoted after 3/4 audit exercises with zero failures. |
| `get_msbuild_properties` | S | RO | Promoted v1.16.0; filter with `propertyNameFilter` / `includedNames`. |

#### `scaffolding` (1 stable, 4 experimental — +1 experimental via v1.17)
| Tool | Tier | RO/D | Pair | Notes |
|------|------|------|------|-------|
| `scaffold_type_preview` | E | RO | — | v1.8+ emits `internal sealed class T`. v1.17+ auto-implements interface members as `NotImplementedException` stubs when `baseType`/`interfaces` resolve to an interface (opt out via `implementInterface: false`). |
| `scaffold_type_apply` | E | D | `scaffold_type_preview` | Not revertible. |
| `scaffold_test_preview` | S | RO | — | Promoted v1.16.0; auto-detects test framework. |
| `scaffold_test_apply` | E | D | `scaffold_test_preview` | Not revertible. |
| `scaffold_test_batch_preview` | E | RO | — | New v1.17.0 — batch wrapper around `scaffold_test_preview`, reuses one compilation cache across N targets; one composite preview token covers every generated file. |

#### `dead-code` (1 stable, 2 experimental)
| Tool | Tier | RO/D | Pair | Notes |
|------|------|------|------|-------|
| `remove_dead_code_preview` | S | RO | — | Promoted v1.16.0 (2026-04-14 promotion batch). |
| `remove_dead_code_apply` | E | D | `remove_dead_code_preview` | Revertible. |
| `remove_interface_member_preview` | E | RO | — | Specialized dead-code preview for interface methods. |

#### `editing` (2 stable, 3 experimental — +2 experimental via v1.17)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `apply_text_edit` | S | D | Promoted v1.16.0; revertible via single-slot undo. |
| `apply_multi_file_edit` | E | D | Revertible; batched snapshot. |
| `add_pragma_suppression` | S | D | Promoted v1.16.0; direct write; not a preview-based flow. |
| `preview_multi_file_edit` | E | RO | New v1.17.0 — simulates multi-file edits against a single Solution snapshot, returns per-file unified diffs + preview token. |
| `preview_multi_file_edit_apply` | E | D | New v1.17.0 — redeems the `preview_multi_file_edit` token; rejects stale tokens if workspace moved. |

#### `configuration` (3 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `get_editorconfig_options` | S | RO | Promoted after 4/4 audit exercises with zero failures. |
| `set_editorconfig_option` | S | RO+D | Promoted v1.16.0; revertible via `revert_last_apply` (undo retrofit v1.8.2). |
| `set_diagnostic_severity` | S | RO+D | Promoted v1.16.0; writes `.editorconfig`; not undoable via `revert_last_apply`. |

#### `code-actions` (3 stable, 0 experimental)
| Tool | Tier | RO/D | Pair | Notes |
|------|------|------|------|-------|
| `get_code_actions` | S | RO | — | Promoted v1.11.0; supports selection-range refactorings (introduce parameter, inline temporary). |
| `preview_code_action` | S | RO | — | Promoted v1.11.0. |
| `apply_code_action` | S | D | `preview_code_action` | Promoted v1.11.0; revertible. |

#### `scripting` (1 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `evaluate_csharp` | S | RO | Promoted v1.9.0; hard-budget via `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS`; cross-cutting principle #9 for stalls. |

#### `syntax` (1 stable, 0 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `get_syntax_tree` | S | RO | Promoted after 2/4 audit exercises with zero failures. |

#### `undo` (1 stable, 1 experimental)
| Tool | Tier | RO/D | Notes |
|------|------|------|-------|
| `revert_last_apply` | S | D | Promoted v1.16.0; single-slot per workspace; see backlog `revert-last-apply-disk-consistency`. |
| `apply_with_verify` | E | D | Apply preview + auto-verify via compile_check; auto-revert on new errors. |

### Resources (9 stable, 1 experimental)

If a client cannot list or read one of these resources, keep the resource in the audit ledger with status `blocked` and note the client limitation. Do not treat that as prompt drift.

| URI Template | Tier | Description |
|---|---|---|
| `roslyn://server/catalog` | S | Machine-readable surface inventory and support policy |
| `roslyn://server/resource-templates` | S | All resource URI templates |
| `roslyn://workspaces` | S | Active sessions (lean summary) |
| `roslyn://workspaces/verbose` | S | Active sessions (full per-project tree) |
| `roslyn://workspace/{workspaceId}/status` | S | Workspace status (lean summary) |
| `roslyn://workspace/{workspaceId}/status/verbose` | S | Workspace status (full per-project tree) |
| `roslyn://workspace/{workspaceId}/projects` | S | Project graph metadata |
| `roslyn://workspace/{workspaceId}/diagnostics` | S | Compiler diagnostics |
| `roslyn://workspace/{workspaceId}/file/{filePath}` | S | Source file content |
| `roslyn://workspace/{workspaceId}/file/{filePath}/lines/{N-M}` | E | Line-range slice of a source file (v1.15+) |

### Prompts (19 experimental)

All prompts currently ship as `experimental`. The `server_info.surface.prompts.stable=0` field is by design until individual prompts are promoted in `ServerSurfaceCatalog` (see backlog `server-info-prompts-tier-doc`). **Phase 16** + **Final surface closure §8** + **§12 Experimental promotion scorecard** are the path from exercised prompt → `promote` / `keep-experimental` / `needs-more-evidence` / `deprecate`.

| Prompt | Tier | Description |
|---|---|---|
| `explain_error` | E | Explain a compiler diagnostic |
| `suggest_refactoring` | E | Suggest refactorings for a symbol or region |
| `review_file` | E | Review a source file |
| `analyze_dependencies` | E | Architecture and dependency analysis |
| `debug_test_failure` | E | Debug a failing test |
| `refactor_and_validate` | E | Preview-first refactoring with validation |
| `fix_all_diagnostics` | E | Batched diagnostic cleanup |
| `guided_package_migration` | E | Package migration across projects |
| `guided_extract_interface` | E | Interface extraction and consumer updates |
| `security_review` | E | Comprehensive security review |
| `discover_capabilities` | E | Discover relevant tools for a task category |
| `dead_code_audit` | E | Dead code detection and removal |
| `review_test_coverage` | E | Test coverage review and gap identification |
| `review_complexity` | E | Complexity review and refactoring opportunities |
| `cohesion_analysis` | E | SRP analysis via LCOM4 with guided type extraction |
| `consumer_impact` | E | Consumer/dependency graph analysis for refactoring impact |
| `guided_extract_method` | E | Extract-method workflow with data/control-flow checks |
| `msbuild_inspection` | E | MSBuild property/item inspection checklist |
| `session_undo` | E | Session history and `revert_last_apply` guidance |

### Plugin skills (19 — Roslyn-Backed-MCP repo only)

Plugin skills compose MCP tools into guided workflows. Phase 16b verifies each against the live catalog. Skills are product surface — regressions break every Claude Code plugin user. **Skills are not rows in `roslyn://server/catalog`** and do not use the same `stable` / `experimental` **SupportTier** as tools/prompts; Phase 16b is a **quality / contract** audit (tool names, safety rules), not a tier promotion pipeline. Ship readiness for skills is "invalid tool ref = FAIL" in the Skills audit table, not a scorecard `promote` row.

| Skill | Directory | Mutates? | Primary tools / notes |
|-------|-----------|---------|---------------------------|
| `analyze` | `skills/analyze/` | No | `workspace_load`, `project_graph`, `compile_check`, `project_diagnostics`, `get_complexity_metrics`, `get_cohesion_metrics`, `nuget_vulnerability_scan`, `security_diagnostics`, `workspace_close` |
| `bump` | `skills/bump/` | Yes | Repo version files + `CHANGELOG` (no MCP tools) |
| `code-actions` | `skills/code-actions/` | Yes | `get_code_actions`, `preview_code_action`, `apply_code_action`, `compile_check` |
| `complexity` | `skills/complexity/` | No | `get_complexity_metrics`, `find_shared_members`, `symbol_info` |
| `dead-code` | `skills/dead-code/` | Yes (optional) | `find_unused_symbols`, `find_references`, `remove_dead_code_preview`, `remove_dead_code_apply`, `compile_check` |
| `document` | `skills/document/` | Yes | `document_symbols`, `get_source_text`, `apply_text_edit`, `compile_check` |
| `explain-error` | `skills/explain-error/` | Yes (optional) | `diagnostic_details`, `code_fix_preview`, `code_fix_apply`, `fix_all_preview`, `fix_all_apply`, `compile_check` |
| `extract-method` | `skills/extract-method/` | Yes | `extract_method_preview`, `extract_method_apply`, `analyze_data_flow`, `analyze_control_flow`, `compile_check` |
| `migrate-package` | `skills/migrate-package/` | Yes | `get_nuget_dependencies`, `migrate_package_preview`, `apply_composite_preview`, `compile_check` |
| `project-inspection` | `skills/project-inspection/` | No | `evaluate_msbuild_property`, `evaluate_msbuild_items`, `get_msbuild_properties`, `workspace_reload` |
| `publish-preflight` | `skills/publish-preflight/` | No | `eng/*.ps1`, `dotnet pack` (release gate, not MCP) |
| `refactor` | `skills/refactor/` | Yes | `symbol_search`, `symbol_info`, `find_references`, `impact_analysis`, preview/apply families per workflow table, `compile_check`, `revert_last_apply` |
| `review` | `skills/review/` | No | `project_diagnostics`, `diagnostic_details`, `find_unused_symbols`, `find_references`, `get_complexity_metrics`, `get_cohesion_metrics`, `find_shared_members`, `security_diagnostics`, `code_fix_preview`, `fix_all_preview` |
| `security` | `skills/security/` | No | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`, `find_reflection_usages`, `get_di_registrations` |
| `session-undo` | `skills/session-undo/` | Yes (optional) | `workspace_changes`, `revert_last_apply`, `workspace_reload`, `workspace_close` |
| `snippet-eval` | `skills/snippet-eval/` | No* | `analyze_snippet`, `evaluate_csharp` (* host executes script — trust boundary) |
| `test-coverage` | `skills/test-coverage/` | Yes (optional) | `test_coverage`, `test_discover`, `test_related`, `document_symbols`, `scaffold_test_preview`, `scaffold_test_apply` |
| `test-triage` | `skills/test-triage/` | No | `test_discover`, `test_run`, `test_related`, `test_related_files` |
| `update` | `skills/update/` | Yes (global tool) | `server_info`, `dotnet tool`, plugin install scripts |

When the audit target is not Roslyn-Backed-MCP, the skills audit runs against a local checkout of this repo; mark Phase 16b `blocked` with the reason if that checkout is not available.

### Tool Source Files

| File | Count | Live category |
|---|---|---|
| `ServerTools.cs` | 1 | `server` |
| `WorkspaceTools.cs` | 8 | `workspace` |
| `SymbolTools.cs` | 16 | `symbols` |
| `AnalysisTools.cs` | 7 | `analysis` |
| `AdvancedAnalysisTools.cs` | 7 | `advanced-analysis` (6) + `analysis` (`analyze_snippet`) |
| `ConsumerAnalysisTools.cs` | 1 | `analysis` (`find_consumers`) |
| `CohesionAnalysisTools.cs` | 2 | `analysis` (`get_cohesion_metrics`, `find_shared_members`) |
| `SecurityTools.cs` | 3 | `security` |
| `FlowAnalysisTools.cs` | 2 | `advanced-analysis` (`analyze_data_flow`, `analyze_control_flow`) |
| `SyntaxTools.cs` | 1 | `syntax` |
| `OperationTools.cs` | 1 | `advanced-analysis` (`get_operations`) |
| `CompileCheckTools.cs` | 1 | `validation` (`compile_check`) |
| `AnalyzerInfoTools.cs` | 1 | `analysis` (`list_analyzers`) |
| `SnippetAnalysisTools.cs` | 1 | `analysis` (`analyze_snippet`) |
| `ScriptingTools.cs` | 1 | `scripting` |
| `RefactoringTools.cs` | 8 | `refactoring` |
| `CodeActionTools.cs` | 5 | `refactoring` (stable `code_fix_*`) + `code-actions` (experimental `get_code_actions` / `preview_code_action` / `apply_code_action`) |
| `FixAllTools.cs` | 2 | `refactoring` |
| `InterfaceExtractionTools.cs` | 2 | `refactoring` |
| `TypeExtractionTools.cs` | 2 | `refactoring` |
| `TypeMoveTools.cs` | 2 | `refactoring` |
| `BulkRefactoringTools.cs` | 2 | `refactoring` |
| `CrossProjectRefactoringTools.cs` | 3 | `cross-project-refactoring` |
| `OrchestrationTools.cs` | 4 | `orchestration` |
| `DeadCodeTools.cs` | 2 | `dead-code` |
| `EditTools.cs` | 1 | `editing` (`apply_text_edit`) |
| `MultiFileEditTools.cs` | 3 | `editing` (`apply_multi_file_edit`, `preview_multi_file_edit`, `preview_multi_file_edit_apply`) |
| `FileOperationTools.cs` | 6 | `file-operations` |
| `ProjectMutationTools.cs` | 11 | `project-mutation` |
| `ScaffoldingTools.cs` | 4 | `scaffolding` (including `scaffold_test_batch_preview` v1.17+) |
| `ExtractMethodTools.cs` | 2 | `refactoring` (`extract_method_preview`, `extract_method_apply`) |
| `ValidationTools.cs` | 7 | `validation` (build/test/coverage) |
| `ValidationBundleTools.cs` | 1 | `validation` (`validate_workspace`, v1.18+) |
| `TestReferenceMapTools.cs` | 1 | `validation` (`test_reference_map`, v1.17+) |
| `TestCoverageTools.cs` | — | `validation` (legacy split — see `ValidationTools.cs` for `test_coverage`) |
| `ImpactSweepTools.cs` | 1 | `analysis` (`symbol_impact_sweep`, v1.17+) |
| `RestructureTools.cs` | 2 | `refactoring` (`restructure_preview`, `replace_string_literals_preview`, v1.17+) |
| `ChangeSignatureTools.cs` | 1 | `refactoring` (`change_signature_preview`, v1.18+) |
| `SymbolRefactorTools.cs` | 1 | `refactoring` (`symbol_refactor_preview`, v1.18+) |
| `ApplyWithVerifyTool.cs` | 1 | `undo` (`apply_with_verify`, v1.15+) |
| `RemoveInterfaceMemberTool.cs` | 1 | `dead-code` (`remove_interface_member_preview`, v1.15+) |
| `PromptShimTools.cs` | 1 | `prompts` (`get_prompt_text`, v1.18+) |
| `SuggestionTools.cs` | 1 | `advanced-analysis` (`suggest_refactorings`) |
| `EditorConfigTools.cs` | 2 | `configuration` |
| `MSBuildTools.cs` | 3 | `project-mutation` (`evaluate_msbuild_*`, `get_msbuild_properties`) |
| `SuppressionTools.cs` | 2 | `configuration` (`set_diagnostic_severity`) + `editing` (`add_pragma_suppression`) |
| `UndoTools.cs` | 1 | `undo` |
