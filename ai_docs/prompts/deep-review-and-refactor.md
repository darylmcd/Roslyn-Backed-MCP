# Deep Code Review & Refactor Agent Prompt

<!-- purpose: Exhaustive living prompt for MCP audits and refactor exercises against the real tool surface. Output is optimized for downstream agentic consumption (machine-parseable tables, fixed schemas, dense per-call evidence). -->
<!-- DO NOT DELETE THIS FILE.
     This is a living document that must be maintained and kept in sync
     with the project's actual tool, resource, and prompt surface at all times.
     When tools, resources, or prompts are added, removed, or renamed,
     update the Tools Reference appendix accordingly.
   Last surface audit: 2026-04-06 (catalog 2026.03; 123 tools = 56 stable / 67 experimental, 7 resources, 16 prompts).
   Concurrency feature flags exercised: `ROSLYNMCP_WORKSPACE_RW_LOCK` (Phase 8b). -->

> Use this prompt with an AI coding agent that has access to the Roslyn MCP server.
> **Primary purpose:** produce an MCP server audit (bugs, incorrect results, gaps). **Mechanism:** real refactoring plus tool calls that exercise the full surface.

---

## Prompt

You are a senior .NET architect. Your **primary mission** is to **audit the Roslyn MCP server** — capture incorrect results, crashes, timeouts, missing data, inconsistencies between tools, and UX/documentation gaps. Your **secondary mechanism** is to **actually refactor the target solution** (Phases 0–6) so tool calls run against real code, previews have real diffs, and `compile_check` / `test_run` mean something.

1. **MCP server audit (primary)** — For every tool call, ask whether the result is correct, complete, and consistent with sibling tools. Record issues in the mandatory audit file (see Output Format). Also record **tool coverage** (what was exercised vs skipped) and **verified working** tools — not only failures.
2. **Refactor the codebase (supporting)** — In **Phase 6 only**, apply meaningful improvements (fixes, renames, extractions, dead code removal, format, organize). Verify with `compile_check` and `test_run`. Those changes belong in the target repo’s git history. Summarize what you applied in the audit report’s **Phase 6 refactor summary** subsection (see Output Format); that summary is part of the single MCP audit file, not a separate deliverable.

**Known issues / prior findings:** If you are auditing from within **Roslyn-Backed-MCP** or you otherwise have access to the server backlog, cross-check open MCP issues in [`ai_docs/backlog.md`](../backlog.md) and cite matching ids briefly (for example `semantic-search-async-modifier-doc`). If you are auditing from another repo and do not have that backlog available, use the closest equivalent prior source you do have (previous audit report, issue tracker, saved repro list). If no prior source exists, mark the regression section **N/A** instead of inventing one.

**Phase order note:** Run phases in this order: **0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 8b → 10 → 9 → 11 → 12 → 13 → 14 → 15 → 16 → 17 → 18**. Phase **8b** (Concurrency / RW lock audit) runs immediately after Phase 8 so it sees the post-refactor workspace state and can compare wall-clock against the Phase 8 baseline. **Phase 9** (Undo) runs after **Phase 10** so `revert_last_apply` does **not** undo Phase 6 refactoring — see Phase 9.

**Portability and completeness contract:**

1. This prompt is intended to run against **any loadable C# repo**. Prefer a `.sln` or `.slnx`; if the repo has no solution file, use a `.csproj`.
   - If no entrypoint loads successfully, continue with the workspace-independent families that still apply (`server_info`, server resources, `analyze_snippet`, `evaluate_csharp`, and any live prompts the client can invoke without a workspace) and mark every workspace-scoped entry `blocked` with the concrete load failure.
2. **Default to `full-surface`.** Only opt into `conservative` when you cannot safely use a disposable branch/worktree/clone.
   - **`full-surface` (default):** disposable branch/worktree/clone; drive preview -> apply families end-to-end where safe and clean up or reverse audit-only mutations.
   - **`conservative` (opt-in):** non-disposable repo; keep high-impact families preview-only and mark the skipped apply siblings as `skipped-safety`.
   - Before the first write-capable call, record the disposable branch/worktree/clone path you will mutate. If you cannot do that safely, switch to `conservative` and say why.
3. Treat `server_info`, `roslyn://server/catalog`, and `roslyn://server/resource-templates` as the **authoritative live surface**. If this prompt's appendix disagrees with the running server, the live catalog wins and the mismatch is itself a finding.
4. Build a live **coverage ledger** from the catalog. Every live tool, resource, and prompt must end with exactly one final status: `exercised`, `exercised-apply`, `exercised-preview-only`, `skipped-repo-shape`, `skipped-safety`, or `blocked`. Silent omissions mean the audit is incomplete.
5. If the MCP client cannot invoke a live resource or prompt family, mark those catalog entries `blocked` with the client limitation. Do not silently omit them or relabel them as skipped.
6. Record repo-shape constraints up front: single vs multi-project, tests present, analyzers present, source generators present, DI usage present, `.editorconfig` present, Central Package Management / `Directory.Packages.props`, multi-targeting, and network/package-restore constraints.
7. Do not invent applicability. If the repo has no tests, no DI, no source generators, no multi-targeting, or only one project, record that explicitly and treat the corresponding steps as `skipped-repo-shape`.

### Execution strategy and context conservation

1. If the client/runtime supports **subagents**, **background agents**, or another background execution facility, you MAY and generally SHOULD use them for long-running or high-output validation work to conserve the primary agent's context window.
2. Prioritize delegation for full-solution build/test/coverage work and other log-heavy operations, especially Phase 8 `test_run` with no filter, `test_coverage`, and any equivalent shell-based validation. Keep short, low-output probes inline unless delegation meaningfully reduces noise.
3. Delegation changes execution ownership, not audit ownership. The primary agent still chooses the scenario, preserves workspace/mode context, evaluates PASS / FLAG / FAIL, updates the coverage ledger, and writes the final report.
4. Require helpers to return a concise structured summary instead of raw logs: tool or command used, scope or filter, pass/fail counts, failing test names, approximate duration, coverage headline, and any anomalies or client/tool errors.
5. Do not delegate preview/apply chains or workspace-version-sensitive mutation sequences unless the helper can operate against the same disposable checkout and workspace state safely. If helper/background execution is unavailable, run the steps directly and continue the audit.

Work through each phase below **in order** (including the Phase 10 -> Phase 9 sequence), then complete the **Final surface closure** step before you write the report. After each tool call, evaluate whether the result is correct and complete. If a tool returns an error, empty results when data was expected, or results that seem wrong, record it for the MCP Server Audit Report.

### Cross-cutting audit principles (apply to every phase)

These standing rules apply to **every** tool call across all phases. Do not wait for a specific phase checkpoint to observe these — capture them in real time.

1. **Inline severity signal:** After each tool call, mentally tag the result: **PASS** (correct and complete), **FLAG** (minor issue or unexpected behavior worth noting), or **FAIL** (incorrect, crash, or missing data). Accumulate FLAGs and FAILs for the report so observations are not lost between tool calls and report-writing time.
2. **Tool description accuracy:** Compare each tool's actual behavior with its MCP tool schema/description. Did the schema accurately describe required vs optional parameters? Did defaults match the description? Were there undocumented parameters that changed behavior? Was the return shape consistent with what the description promised? Schema-vs-behavior mismatches are high-value findings because the descriptions are the API documentation consumed by every AI client.
3. **Performance observation:** Note any tool call that is notably slow (>5 s for a simple single-symbol operation, >15 s for a solution-wide scan). Record the tool name, input scale, and approximate wall-clock time for the report's **Performance observations** subsection.
4. **Error message quality:** When a tool returns an error, evaluate the message itself: Is it **actionable** (tells the caller what went wrong and what to do differently)? **Vague** (generic message with no remediation hint)? Or **unhelpful** (raw exception / stack trace)? Record the rating in the report.
5. **Response contract consistency:** Across tool calls, note inconsistencies in response contracts: Are line numbers 0-based or 1-based consistently? Do related tools (`find_consumers`, `find_references`, `find_type_usages`) use the same field names for the same concepts? Are classification values always strings or sometimes numeric codes? Note inconsistencies in the report.
6. **Parameter-path coverage:** Happy-path defaults are not enough. For each major family you exercise, probe at least one non-default path when the live schema exposes it: project/file filters, severity filters, offset/limit pagination, boolean flags, alternate `kind` values, or similar. If the running schema does not expose a parameter mentioned in this prompt, record that as `N/A` rather than inventing it.
7. **Precondition discipline:** Distinguish server defects from repo/environment constraints. If a tool depends on tests, analyzers, network access, source generators, DI registrations, Central Package Management, or a multi-project graph, record whether the precondition is absent in the repo, blocked by the environment, or mishandled by the server. A clear, actionable dependency or not-applicable response is not the same as a server bug.
8. **Lock mode tagging:** Every tool call must be associated with the **lock mode** the server was launched in for that call (`legacy-mutex` when `ROSLYNMCP_WORKSPACE_RW_LOCK` is unset/`false`, `rw-lock` when set to `true`). The flag is bound at process startup, so a single server session always has one mode. If the audit includes a dual-mode lane (Phase 8b), record the mode in every per-call evidence row so wall-clock and behavioral differences can be attributed correctly. `server_info` does **not** report this flag — track it via the launch config the operator used and confirm via the Phase 8b behavioral probes.
9. **Debug log capture:** If the MCP client surfaces server-emitted `notifications/message` log entries (the `McpLoggingProvider` forwards .NET `ILogger` events with structured payloads, including a `correlationId`), keep that channel visible for the entire run and record any `Warning`/`Error`/`Critical` entry verbatim in the report. Record `Information` entries that mention workspace lifecycle (`workspace_load`, `workspace_close`, `workspace_reload`), gate acquisition, lock contention, rate-limit hits, or request timeouts. If the client cannot show these notifications, record that as a client limitation in the header rather than silently dropping the channel.
10. **`evaluate_csharp`, client UI, and “tens of minutes” stalls:** The server applies a **script timeout** (default 10 seconds via `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS`; raise it only if you intentionally need longer scripts). A healthy `evaluate_csharp` call should **finish or fail within that budget** (plus small overhead). **Tens of minutes** frozen on a label such as **Evaluating C#**, with the agent **only moving after you send another message**, is **not** explained by normal Roslyn work on this tool — it usually means **(a)** the **IDE/agent** is stalled (model not finishing a turn, orchestrator not advancing, or a client bug), **(b)** **stdio/MCP transport** not delivering the next chunk, or **(c)** in rare cases Roslyn **not honoring cancellation** (the server logs `evaluate_csharp WATCHDOG` at **Critical** on a timer after budget + grace; if stderr is **silent** for the whole stall, suspect **(a)/(b)** first). **Diagnosis:** While stalled, check MCP **stderr** for `evaluate_csharp` lines — heartbeats every `ROSLYNMCP_SCRIPT_HEARTBEAT_MS`, then optional warnings, then **WATCHDOG** if the call exceeds budget + grace. **No server log activity at all** for tens of minutes strongly suggests the problem is **not** the Roslyn MCP host completing a long script. For short stalls (seconds to a minute), MCP `progress`, `ProgressHeartbeatCount` in the JSON result, and **Performance observations** still apply.

---

### Phase 0: Setup, live surface baseline, and repo shape

1. Pick the entrypoint you will load: prefer a `.sln` or `.slnx`; fall back to a `.csproj` if needed.
2. Record whether this session is running in the default `full-surface` mode or an explicitly opted-in `conservative` mode.
3. If `full-surface`, record the disposable branch/worktree/clone path you will mutate and how audit-only changes will be cleaned up. If `conservative`, record why safe disposable isolation is unavailable.
4. **Lock-mode capture via `evaluate_csharp` (source of truth, mandatory).** The server reads `ROSLYNMCP_WORKSPACE_RW_LOCK` once at process startup. The active value is **not** exposed by `server_info` or any other tool, but `evaluate_csharp` runs C# inside the running server process so it can read the server's own environment block — that read is the canonical source of truth. If the UI sits on “Evaluating C#” for **tens of minutes** until you nudge the chat, see cross-cutting principle **#10** — that pattern is usually **agent/client**, not this script completing. Call `evaluate_csharp` with this script and record the returned `LockMode` literally:

   ```csharp
   var raw = System.Environment.GetEnvironmentVariable("ROSLYNMCP_WORKSPACE_RW_LOCK");
   var parsed = false;
   var isOn = bool.TryParse(raw, out parsed) && parsed;
   new {
       Raw = raw ?? "(unset)",
       LockMode = isOn ? "rw-lock" : "legacy-mutex"
   }
   ```

   Record the result as the **canonical lock mode** in the report header. Do **not** infer the mode from launch-config inspection or behavioral fingerprints when this call works — it is the authoritative answer. If `evaluate_csharp` is itself unavailable (server defect, scripting disabled, sandbox restriction), fall back to launch-config inspection and record that the audit was forced to use a less reliable source.
5. **Debug log channel check.** Verify that the MCP client is actively surfacing server-emitted `notifications/message` log entries (the `McpLoggingProvider` forwards .NET `ILogger` events with a structured payload that includes `correlationId`, `level`, `logger`, `message`, `eventId`, `eventName`, and any exception). If the client cannot show those notifications, note the limitation in the header and proceed; the audit must still record any structured log entries it can see.
6. Call `server_info` to record the server version, Roslyn version, catalog version, live stable/experimental counts, and `runtime` / `os` strings.
7. Read `roslyn://server/catalog` to capture the authoritative live surface inventory.
8. Read `roslyn://server/resource-templates` to capture all resource URI templates.
9. Call `workspace_load` with the chosen entrypoint.
10. Call `workspace_list` to confirm the session appears.
11. Call `workspace_status` to confirm the workspace loaded cleanly. Note any load errors.
12. Call `project_graph` to understand the project dependency structure.
13. From the loaded repo, record the repo-shape constraints that affect later phases (projects, tests, analyzers, DI, source generators, Central Package Management, multi-targeting, network limits).
14. Seed or update the coverage ledger so every live tool/resource/prompt already has a planned phase or a provisional skip reason.
15. **Auto-pair detection (run-1 vs run-2 of a dual-mode pair).** Before doing any other work, glob `ai_docs/audit-reports/*_<repo-id>_<oppositeMode>_mcp-server-audit.md` (where `<oppositeMode>` is the mode opposite to the one captured in step 4). If you find a recent partial (within the last 24 hours) whose **Concurrency mode matrix** has `skipped-pending-second-run` markers, this prompt invocation is **run 2 of a dual-mode pair**. Record:
    - The path of the matched run-1 partial (will be reused in Phase 8b.6).
    - The fact that this run is the "second half" (so the agent fills Session B columns when it reaches Phase 8b).
    - If multiple partials match, pick the most recent one and note the others as `superseded` in the report header.

    If no matching partial exists, this prompt invocation is **run 1** (or a single-mode lane). The audit file produced at the end will be saved with the lock mode embedded in the filename (`<timestamp>_<repo-id>_<lockMode>_mcp-server-audit.md`); the agent does this automatically based on step 4's result. No operator-supplied filename is needed.

**MCP audit checkpoint:** Did `evaluate_csharp` return a clean `LockMode` value, and does it match what you would expect from the operator's launch config (if known)? Did `server_info` and `roslyn://server/catalog` agree on live counts and tiers? Did `workspace_load` succeed on the chosen entrypoint? Does `workspace_list` show the new session? Did `workspace_status` report any stale documents or missing projects? Did you explicitly record the disposable isolation path / conservative rationale, the repo shape, the **lock mode from `evaluate_csharp`**, the **debug log channel availability**, the **auto-pair detection result** (run 1 / run 2 / single-mode), and an initial coverage ledger with no silent omissions? If every candidate entrypoint failed to load, did you mark workspace-scoped rows `blocked` and continue only with workspace-independent families?

---

### Phase 1: Broad Diagnostics Scan

1. Call `project_diagnostics` (no filters) to get all compiler errors and warnings across the solution.
2. Call `project_diagnostics` again with at least one non-default selector exposed by the live schema (project, file, severity, and/or offset/limit pagination). Verify scoped results and paging boundaries are correct.
3. Call `compile_check` (no filters) to get in-memory compilation diagnostics. Compare the results with `project_diagnostics` — they should be consistent but may differ in count or detail.
4. Call `compile_check` again with `emitValidation=true`. Compare diagnostics and wall-clock time with the default call.
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
2. Call `get_cohesion_metrics` with `minMethods=3` to find types with LCOM4 > 1 (SRP violations).
3. Call `find_unused_symbols` with `includePublic=false` to detect dead code.
4. Call `find_unused_symbols` with `includePublic=true` and compare — are there public APIs with zero internal references?
5. Call `get_namespace_dependencies` to check for circular namespace dependencies.
6. Call `get_nuget_dependencies` to audit package references across projects.

**MCP audit checkpoint:** Do complexity metrics make sense for the methods shown? Are LCOM4 scores reasonable (do types with score=1 actually look cohesive when you read them)? Does `find_unused_symbols` miss any obviously dead code or falsely flag used symbols?

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
9. Call `find_type_mutations` to find all mutating members and their external callers.
10. Call `find_type_usages` to see how the type is used (return types, parameters, fields, casts, etc.).
11. Call `callers_callees` on 2-3 of its methods to map call chains.
12. Call `find_property_writes` on any settable properties to classify init vs post-construction writes.
13. Call `member_hierarchy` on overridden/implemented members.
14. Call `symbol_relationships` for a combined view.
15. Call `symbol_signature_help` on a key method to verify signature documentation.
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
3. Call `analyze_control_flow` on the same range. Examine:
   - Is `EndPointIsReachable` false where it should be true (or vice versa)?
   - Are there unreachable exit points?
   - Do return statements cover all paths?
4. Call `get_operations` on key expressions within the method (assignments, invocations, conditionals) to get the IOperation tree. Look for:
   - Implicit conversions that may lose data
   - Null-conditional chains that could be simplified
   - Patterns that indicate code smell (long invocation chains, nested conditionals)
5. Call `get_syntax_tree` on the method's line range to compare syntax structure with the IOperation view.

**MCP audit checkpoint:** Does `analyze_data_flow` correctly identify all variables? Are the `Captured` and `CapturedInside` lists accurate for lambdas? Does `analyze_control_flow` correctly detect unreachable code? Does `get_operations` return a sensible tree or does it miss operations? Do line numbers in results map correctly to the source?

---

### Phase 5: Snippet & Script Validation

This phase is another hotspot for the same **Evaluating C#** label as Phase 0 step 4. **`analyze_snippet`** and **`evaluate_csharp`** (steps 4–7) should still complete within the **script timeout** unless you raised `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS`. Multi-minute or multi–ten-minute freezes that **only clear when you message the agent** are covered by cross-cutting principle **#10** (usually not the MCP server). For the infinite-loop probe (step 7), expect at most **one** call lasting until the configured script timeout — not an unbounded hang.

1. Call `analyze_snippet` with `kind="expression"` using a simple expression like `1 + 2`. Verify it reports no errors.
2. Call `analyze_snippet` with `kind="program"` using a small class definition. Verify declared symbols are listed.
3. Call `analyze_snippet` with intentionally broken code. Verify it reports the expected compilation error.
4. Call `evaluate_csharp` with a simple expression like `Enumerable.Range(1, 10).Sum()`. Verify the result is `55`.
5. Call `evaluate_csharp` with a multi-line script. Verify execution.
6. Call `evaluate_csharp` with code that should produce a runtime error (e.g., `int.Parse("abc")`). Verify it reports the error gracefully.
7. Call `evaluate_csharp` with an infinite loop. Verify the timeout fires and doesn't hang the server.

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

**MCP audit checkpoint:** Does `fix_all_preview` find ALL instances? Does it crash or timeout on large scopes? Does `rename_preview` catch references in comments or strings? Does `extract_interface_preview` generate valid C# syntax? Does `bulk_replace_type_preview` miss any usages? Does `extract_type_preview` handle shared private members correctly? Does `format_range_preview` format only the specified range or does it bleed? Does `remove_dead_code_preview` correctly remove only the targeted symbols? Does `set_diagnostic_severity` correctly update or create the `.editorconfig` entry? Does `add_pragma_suppression` insert the pragma at the correct line without breaking surrounding code? After each apply, does `compile_check` pass?

**Cross-tool chain validation (Phase 6):** After `rename_apply`, call `find_references` on the new name — does the count match the preview? After `extract_interface_apply`, call `type_hierarchy` on the new interface — does it show the implementor? After `fix_all_apply`, call `project_diagnostics` with the same diagnostic ID — did the count drop to zero? After `organize_usings_apply`, call `get_source_text` — are usings actually sorted? These chains verify that workspace state stays consistent across mutations.

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

### Phase 8b: Concurrency / RW Lock Audit (`ROSLYNMCP_WORKSPACE_RW_LOCK`)

**Purpose:** Exercise the per-workspace reader/writer lock feature flag and produce a machine-readable concurrency matrix that downstream agents can compare across runs. The flag (`ROSLYNMCP_WORKSPACE_RW_LOCK`) replaces the legacy per-workspace `SemaphoreSlim` with a per-workspace `Nito.AsyncEx.AsyncReaderWriterLock`. Under the new mode, multiple reads against the same workspace overlap, writes are exclusive against in-flight reads, and `workspace_close` / `workspace_reload` block on the per-workspace write lock so they wait for in-flight readers.

#### Operational model — auto-detected, four-step operator dance

The flag is bound at server startup, so a single MCP server session can only test one lock mode. To populate both halves of the matrix the operator runs the prompt **twice**, with the lock mode flipped between runs by `eng/flip-rw-lock.ps1`. Everything else is auto-detected: the agent reads the actual lock mode from inside the running server via `evaluate_csharp` (Phase 0 step 4), names the audit file with the mode in it automatically, and decides whether this is run 1 or run 2 by globbing for an opposite-mode partial of the same `<repo-id>` (Phase 0 step 15).

**The full operator dance is exactly four steps:**

| # | Operator action |
|---|-----------------|
| 1 | Invoke the deep-review prompt against the target repo. The agent runs Phases 0–8 → 8b → 10 → 9 → 11 → 18 in whichever mode the server is currently in, auto-names the audit file `<timestamp>_<repo-id>_<lockMode>_mcp-server-audit.md`, and marks the Session B columns of the concurrency matrix `skipped-pending-second-run`. |
| 2 | Run `./eng/flip-rw-lock.ps1`. The script reads the current User-scope env var, kills any running `roslynmcp.exe` / `RoslynMcp.Host.Stdio.exe` process, and writes the inverse value. No operator decision about which mode to set — the script flips. |
| 3 | Fully close and reopen the MCP client so it spawns a fresh server subprocess that inherits the flipped env var. (Restarting just the chat tab is not enough; the client process must exit.) |
| 4 | Invoke the deep-review prompt a second time against the **same disposable checkout** used in step 1. The agent again calls `evaluate_csharp` to detect the new mode, globs the audit-reports directory to find the run-1 partial of the opposite mode, recognises that this is run 2 of a dual-mode pair, re-runs Phase 8b sub-phases 8b.1, 8b.2, 8b.3, 8b.5 against the same probe slot ids it inherited from the run-1 partial, fills the Session B columns of the matrix, and writes the resulting audit as `<timestamp>_<repo-id>_<oppositeLockMode>_mcp-server-audit.md`. The run-2 audit file is self-contained and has both Session A (copied from the run-1 partial) and Session B (measured this run) columns populated — it is the canonical dual-mode raw audit. |

That is the entire sequence. There is no operator decision about "which mode is this run", no manual filename, no manual matrix merge, no manual flag management. The only operator inputs are *invoke prompt*, *run script*, *restart client*, *invoke prompt*.

**Single-mode lane** is what happens when the operator runs the prompt only once (skipping steps 2–4). The Session B columns remain `skipped-pending-second-run` in the saved file, and a future second run of the prompt (with the lock mode flipped) will pair with it via the auto-pair logic in Phase 0 step 15 to produce a complete dual-mode audit. Single-mode is acceptable for routine smoke runs; dual-mode is required for release candidates per `ai_docs/procedures/deep-review-program.md` → *Concurrency lock-mode lane*.

**Mid-session server restart (formerly Path B) is not supported.** It was unreliable on every stdio MCP client (Claude Code, Cursor, Continue, etc.) because killing the server subprocess broke the client's stdio handshake. The four-step flow above is the only supported dance.

#### Sessions vocabulary used in the sub-phases below
- **Session A** = the run-1 server session (first prompt invocation, whichever mode the operator started with).
- **Session B** = the run-2 server session (second prompt invocation, opposite mode after `flip-rw-lock.ps1`).

In a single-mode lane, Session B is empty (`skipped-pending-second-run`) until a future run-2 fills it via auto-pair. In a complete dual-mode lane, the run-2 audit file holds both sessions' data.

#### 8b.0 — Probe set definition (run before any timing)

Pick a stable, repeatable probe set that exercises *reader* code paths and a small number of *writer* code paths against the workspace. The probes must be re-runnable byte-for-byte across both sessions. Record the chosen probes in the report under **Concurrency probe set** so the matrix is reproducible.

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

#### 8b.1 — Sequential baseline (Session A, current mode)

1. Record `Session A` lock mode (`legacy-mutex` or `rw-lock`) from Phase 0 capture.
2. For each reader R1–R5, call the tool **once sequentially** and record wall-clock in milliseconds. These are the **single-call baselines**.
3. Record any structured log entries received from the MCP server during the calls (correlation IDs, gate-related warnings, rate-limit hits, request timeouts).

#### 8b.2 — Parallel-read fan-out (Session A)

1. Determine the **fan-out N** = `min(4, max(2, Environment.ProcessorCount))`. The server's global throttle is `max(2, Environment.ProcessorCount)` (`src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:35`), so on a 2-core CI host, parallel speedup is bounded above by 2× even under `rw-lock`. Record the host's logical core count and chosen N in the report so the speedup ratio is interpretable.
2. Issue **R1 N times in parallel** against the same workspace id (N concurrent `find_references` calls). Record the wall-clock from request submission to last response.
3. Compute the **parallel speedup** = `N × baseline(R1) / parallel_wall_clock`. Expected ranges, given the global throttle ceiling at N:
   - `legacy-mutex`: ~1.0× (≤ 1.3×) — calls serialize behind the per-workspace `SemaphoreSlim`.
   - `rw-lock`: between `0.7 × N` and `N` (≥ 2.5× when N=4; ≥ 1.6× when N=2). Anything below `0.7 × N` is a FLAG.
4. Repeat the fan-out for R2 (`project_diagnostics` ×N) and R3 (`symbol_search` ×N). Record each parallel wall-clock and speedup.
5. If the client cannot issue concurrent tool calls (some clients serialize internally — that is itself a finding), mark this sub-phase `blocked` with the client limitation and continue.

#### 8b.3 — Read/write exclusion behavioral probe (Session A)

1. Start a long-running reader: call R1 (`find_references` on the hot symbol) and immediately, while it is still in flight, issue W1 (`format_document_preview` followed by `format_document_apply` on a Phase 6-touched file).
2. Record whether W1 **starts immediately** (legacy mode: writer is on a separate `__apply__:<ws>` semaphore today, so it overlaps with the reader) or **waits** for the reader (rw-lock mode: writer blocks until the reader releases). Capture the inter-call timing.
3. **Inverse probe.** Start W1 first (begin `format_document_apply`) and immediately issue R1 against the same workspace. Under rw-lock, R1 must wait for W1 to complete; under legacy, it queues against the reader semaphore separately and does not wait for W1.
4. Tag any deviation from the expected behavior as a FLAG (the lock-mode contract is the most observable correctness signal of this phase).

#### 8b.4 — Lifecycle stress probe (Session A)

> Reference for expected behavior: under `rw-lock`, `workspace_reload` and `workspace_close` both wrap a per-workspace **write** lock acquire inside the global `__load__` gate (`src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:46-73`), so they wait for in-flight readers to release the per-workspace lock before proceeding. Under `legacy-mutex`, the writer is routed to a **separate** `__apply__:<wsId>` semaphore that is independent from the reader's `<wsId>` semaphore, so the writer and reader run concurrently.

1. Start a long-running reader (R2 — `project_diagnostics`) and, while it is in flight, call `workspace_reload(workspaceId)`. Record the observed behavior with one of these labels:
   - `waits-for-reader` — reload blocks until the reader returns (expected under `rw-lock`)
   - `runs-concurrently` — reload completes while the reader is still in flight (expected under `legacy-mutex`)
   - `errors` — reload raises an exception (FLAG candidate under either mode)
2. Record whether the in-flight reader returns **stale** results, **fresh post-reload** results, or **errors** (the third is a FLAG candidate). Note the `correlationId` of both calls if available.
3. Start a long-running reader (R3 — `symbol_search`) and call `workspace_close(workspaceId)`. Use the same labels as step 1. Under `rw-lock`, the reader should complete cleanly and the close should `waits-for-reader`. Under `legacy-mutex`, the close may `runs-concurrently` — and the reader may then throw `KeyNotFoundException` because of the post-acquire `EnsureWorkspaceStillExists` recheck inside `WorkspaceExecutionGate.RunPerWorkspaceAsync` (`src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:244-251`). Record exactly which exception (if any) the reader raises and whether the message is actionable.
4. After the close, immediately call `workspace_load` again to re-establish a session for the remaining sub-phases. Record any errors.

#### 8b.5 — Writer reclassification verification (Session A)

The `perf/workspace-rw-lock` change reclassified six tools that were silently using the read gate while mutating workspace state. Verify each one still completes successfully and observably takes the writer code path. The point of this sub-phase is to catch a regression where one of these tools accidentally drops back to reader semantics (which would be a correctness FLAG under rw-lock).

| # | Tool | Verification |
|---|------|--------------|
| 1 | `apply_text_edit` | Apply a single trivial text edit on a Phase 6-touched file; verify the file content changes and `compile_check` still passes. |
| 2 | `apply_multi_file_edit` | Apply a coordinated trivial edit across two files; verify both files change and `compile_check` still passes. |
| 3 | `revert_last_apply` | Call after sub-phase 1 or 2 above; verify the prior edit is reverted and the workspace is consistent. (This counts toward the Phase 9 audit-only revert step — record it once and reuse the evidence.) |
| 4 | `set_editorconfig_option` | Set a benign key (e.g. `dotnet_sort_system_directives_first = true`) and verify `get_editorconfig_options` reflects it. |
| 5 | `set_diagnostic_severity` | Set `dotnet_diagnostic.<some-IDE-id>.severity = suggestion` and verify it appears in the relevant `.editorconfig`. |
| 6 | `add_pragma_suppression` | Insert `#pragma warning disable <id>` before a known diagnostic line and verify `get_source_text` shows the pragma. |

For each row, also record whether the tool returned in the same wall-clock budget as Session A's reader baseline (writers should be measurably slower because they actually mutate disk state).

#### 8b.6 — Auto-detect run-1 vs run-2 and act accordingly

This sub-phase branches on the **auto-pair detection result** that Phase 0 step 15 already recorded. The agent does not ask the operator anything — Phase 0's glob result is the answer.

**Case A — This is run 1 (no opposite-mode partial found in audit-reports):**

1. Mark **all Session B columns** of the concurrency matrix with the literal string `skipped-pending-second-run` and a one-line note: `paired second run will fill these columns`.
2. **Do not stop and wait.** Continue with the rest of this run (Phases 10 → 9 → 11 → 18) so this run produces a complete mode-1 raw audit. The audit file will be saved at the end of the prompt as `<timestamp>_<repo-id>_<currentLockMode>_mcp-server-audit.md`.
3. At the end of the report, in **Report path note**, write: `This is the mode-1 partial of a deep-review pair. Run \`./eng/flip-rw-lock.ps1\`, restart the MCP client, and re-invoke the deep-review prompt to populate the Session B columns.` This gives the operator a single, unambiguous next instruction.

**Case B — This is run 2 (Phase 0 found a recent opposite-mode partial for the same `<repo-id>`):**

1. Read the run-1 partial file path that Phase 0 captured. Open it and extract:
   - The **Concurrency probe set** table (so this run uses the exact same probe slot ids).
   - The **Sequential baseline** Session A column values (so this run can carry them forward into the run-2 file's matrix).
   - The **Parallel fan-out** Session A rows.
   - The **Lifecycle stress** Session A rows.
   - The **Writer reclassification verification** Session A column.
   - The **Debug log capture** entries from run 1 (to be merged with run 2's debug entries in the run-2 file).
2. Re-load the same workspace via `workspace_load` against the same disposable checkout used in Phase 0. Run sub-phase 8b.0 to confirm the inherited probe slot ids still resolve to valid symbols/files in this workspace; substitute and note any drift.
3. Re-run sub-phases 8b.1 (sequential baseline), 8b.2 (parallel fan-out), 8b.3 (read/write exclusion), and 8b.5 (writer reclassification) against the **same probe slot ids** so the matrix can be diffed mechanically. Record the new wall-clocks and behavioral observations under the **Session B columns** of the matrix.
4. Repeat 8b.4 (lifecycle stress) **only** if the disposable checkout is still clean and re-runnable. Otherwise carry forward run 1's lifecycle rows and mark the run-2 lifecycle column `inherited-from-run-1`.
5. **Carry forward** the Session A column values from the run-1 partial into the run-2 matrix so the run-2 file is self-contained — when both Session A and Session B columns are populated in one file, that file is the canonical dual-mode raw audit.
6. At the end of the report, in **Report path note**, write: `This is the canonical dual-mode raw audit. Paired with run-1 partial: <run-1 path>. Phase 8b.6 carried forward run 1's Session A columns and measured Session B in this run.`
7. Continue with Phases 10 → 9 → 11 → 18 normally so the run-2 file also has full per-tool coverage.

**Case C — Single-mode lane (operator does not plan a second run):**

The agent cannot tell the difference between Case A and Case C from the audit-reports glob alone — both have no opposite-mode partial. To distinguish: if the operator told the agent at the start of the prompt invocation that this is a single-mode lane, mark Session B columns `skipped-single-mode-lane` with the operator's reason. Otherwise default to **Case A** (treat as run 1 of a pending pair). Either way, continue with Phases 10–18 normally; the only difference is the **Report path note** wording. Default-to-Case-A is the safer choice because it leaves the door open for a future run-2 to auto-pair.

#### 8b.7 — Concurrency matrix output (mandatory)

Write the matrix into the report using the schema in **Output Format → Concurrency mode matrix** below. Every cell must have a value or `N/A`; do not leave blanks. Include both Session A and Session B rows even if Session B was `skipped-safety` (with the reason in the cell).

**MCP audit checkpoint:** Did parallel reads under `rw-lock` measurably overlap (speedup ≥ `0.7 × N` for N concurrent reads, where N is the host-bounded fan-out from sub-phase 8b.2.1)? Did parallel reads under `legacy-mutex` measurably serialize (speedup ≤ 1.3×)? Did the speedup signature on this host roughly match the 4-slot benchmark in `tests/RoslynMcp.Tests/Benchmarks/WorkspaceReadConcurrencyBenchmark.cs` (recorded against `Math.Max(2, Environment.ProcessorCount)` as the expected ceiling)? Did the read/write exclusion probe (8b.3) match the documented contract under both modes? Did the lifecycle stress probe (8b.4) reveal any TOCTOU or stale-result issues? Did all six writer-reclassification tools (8b.5) return identical post-state under both modes? Were there any structured log entries that mentioned gate contention, rate-limit rejections, request timeouts, or deadlocks? Was the `parallel_speedup` field stable across two consecutive parallel runs in the same session (within ±15%), or did jitter make it unreliable?

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

1. Call `semantic_search` with a repo-relevant natural-language query like `"async methods returning Task<bool>"` or another pattern that should exist in the target repo.
2. Call `semantic_search` with a broader paraphrase of the same idea (for example `"methods returning Task<bool>"`) and compare the delta so modifier-sensitive matching is visible.
3. Call `semantic_search` with `"classes implementing IDisposable"` or another interface-based query you can cross-check against `find_implementations`.
4. Call `find_reflection_usages` to locate reflection-heavy code that may resist refactoring.
5. Call `get_di_registrations` to audit the DI container wiring.
6. Call `source_generated_documents` to list any source-generator output files.

**MCP audit checkpoint:** Do the paired `semantic_search` queries return relevant and explainable differences? Does the broader paraphrase expose modifier-sensitive matching issues? Does `find_reflection_usages` find all reflection patterns (typeof, GetMethod, Activator, etc.)? Does `get_di_registrations` parse all registration styles, and is an empty result legitimate for the repo shape? Are source-generated documents correctly listed when generators are present?

---

### Phase 12: Scaffolding

**Default behavior:** preview-only. In `full-surface` mode on a disposable checkout, apply one scaffold (`scaffold_type_apply` or `scaffold_test_apply`), verify it compiles or is discoverable, then clean it up with file-operation tools or version control. In `conservative` mode, mark the apply siblings `skipped-safety`.

1. Call `scaffold_type_preview` to scaffold a new class in a project. Verify the generated file content and namespace in the preview payload.
2. If the repo has or can support a test project/framework, call `scaffold_test_preview` to scaffold a test file for an existing type. Verify it targets the correct type and uses the right test framework in the preview.
3. In `full-surface` mode on a disposable checkout, call the corresponding apply tool for one preview and verify the result with `compile_check`, `test_discover`, or both.
4. Call `compile_check` if the server requires a fresh compilation for previews; otherwise skip (previews alone do not change disk).

**MCP audit checkpoint:** Does `scaffold_type_preview` infer the correct namespace from the project structure? Does `scaffold_test_preview` generate valid test stubs when a test target exists? Are preview payloads complete and apply-able in principle? When you used an apply sibling, did the scaffolded file compile or show up in test discovery as expected?

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
4. Call `get_completions` at a position after a dot. Verify IntelliSense-style results.
5. Call `find_references_bulk` with multiple symbol handles. Verify batch results match individual `find_references` calls.
6. Call `find_overrides` on a virtual method. Verify all overriding members are found.
7. Call `find_base_members` on an override. Verify it navigates back to the base declaration.

**MCP audit checkpoint:** Are navigation results accurate? Does `get_completions` return relevant members? Does `find_references_bulk` produce consistent results vs individual calls?

---

### Phase 15: Resource Verification

1. Read `roslyn://server/catalog` to get the machine-readable surface inventory.
2. Read `roslyn://server/resource-templates` to list all resource URI templates and compare with the catalog.
3. Read `roslyn://workspaces` to list active workspace sessions.
4. Read `roslyn://workspace/{workspaceId}/status` and compare with `workspace_status` tool output.
5. Read `roslyn://workspace/{workspaceId}/projects` and compare with `project_graph` tool output.
6. Read `roslyn://workspace/{workspaceId}/diagnostics` and compare with `project_diagnostics` tool output.
7. Read `roslyn://workspace/{workspaceId}/file/{filePath}` for a source file and compare with `get_source_text`.

**MCP audit checkpoint:** Do resources return data consistent with their tool counterparts? Are URI templates resolved correctly? Is the server catalog accurate and up to date? Do resource DTOs use the same field names as their tool counterparts (e.g., `Name` vs `ProjectName`)? Does the catalog tool/resource/prompt count match `server_info`?

---

### Phase 16: Prompt Verification

The live catalog is authoritative for prompt count. In `conservative` mode, exercise at least 6 prompts spanning error explanation, refactoring, review, testing, security, and capability discovery (or all live prompts if fewer than 6 exist). In `full-surface` mode, exercise every live prompt from the catalog unless a prompt is truly `skipped-repo-shape` or `blocked` by the client.

1. Call `explain_error` with a diagnostic from Phase 1. Verify the explanation is accurate and actionable.
2. Call `suggest_refactoring` on a symbol or region you analyzed in Phase 3. Verify the suggestions reference real tools and produce sensible guidance.
3. Call `review_file` on a file modified in Phase 6. Verify the review references actual code and produces useful observations.
4. Call `discover_capabilities` with a task category (e.g. "refactoring", "testing", "security"). Verify it returns relevant tools for the category.
5. Cover the remaining live prompt surface as applicable (current snapshot: `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`). If the live catalog differs, trust the catalog and record prompt drift.

**MCP audit checkpoint:** Do prompt argument templates make sense? Does the prompt output reference correct tools and produce actionable guidance? Does `discover_capabilities` align with the live catalog from Phase 0? Are prompt descriptions accurate? Do any prompts fail, return empty output, or produce hallucinated tool names?

---

### Phase 17: Boundary & Negative Testing

Deliberately probe edge cases and error paths. The goal is to verify that the server validates inputs, returns helpful error messages, and does not crash on bad data.

#### 17a. Invalid Identifiers
1. Call a workspace-scoped tool (e.g. `workspace_status`) with a **non-existent workspace id**. Verify the error is actionable.
2. Call `find_references` (or `symbol_info`) with a **fabricated symbol handle**. Verify it returns a clear error, not a crash or empty success.
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
6. Confirm the **Concurrency mode matrix** is fully populated when Phase 8b ran in any mode, with `N/A` (and a one-line reason) in cells the audit could not fill.
7. Confirm the **Debug log capture** section either has at least one entry or explicitly states `client did not surface MCP log notifications`.

---

When finished with all phases, call `workspace_close` to release the session (if not already closed during Phase 17e).

---

## Output Format — MANDATORY

### What goes in git (no audit file)

Meaningful refactoring from **Phase 6** is committed in the **target solution’s** repository. A short **Phase 6 refactor summary** in the MCP audit report (see Report contents) ties git history to tools exercised; the code changes themselves are not a separate file deliverable.

### What goes in a file (one mandatory output)

You **MUST** write exactly one file: the **raw MCP Server Audit Report**. The task is **not finished** until this file exists on disk. Do not only paste it in chat.

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

1. **Header (metadata)** — server version (from `server_info`), Roslyn / .NET versions if available, MCP client name/version if available, **audited** solution path and name, audited repo revision/branch if available, chosen entrypoint (`.sln` / `.slnx` / `.csproj`), audit mode (`full-surface` by default or explicitly opted-in `conservative`), disposable isolation path or conservative rationale, date, workspace id, approximate project count and document count from `workspace_load` / `project_graph`, repo-shape summary, the prior-issue source used for Phase 18, **lock mode at audit time** (`legacy-mutex` / `rw-lock` / `dual-mode`), and **debug log channel availability** (`yes` / `no` / `partial`).
2. **Coverage summary** — Group by the live catalog's `Kind` + `Category` values from `roslyn://server/catalog`. For each group, report counts for `exercised`, `exercised-apply`, `exercised-preview-only`, `skipped-repo-shape`, `skipped-safety`, and `blocked`.
3. **Coverage ledger (required)** — One row for every live tool, resource, and prompt from `roslyn://server/catalog`. Columns: `kind`, `name`, `tier`, `status`, `phase`, `notes`. The audit is incomplete if the ledger row count does not match the live catalog total or if any entry is omitted. For `blocked` rows, say whether the blocker was a client limitation, workspace-load failure, or server/runtime fault.
4. **Verified tools (working)** — Bullet list: tool name + one-line evidence (e.g. “`compile_check` — 0 errors after Phase 6”). This distinguishes “tested and fine” from “never called.”
5. **Phase 6 refactor summary** — Concise record of **applied** Phase 6 work (not preview-only phases). Include: target repo identity (if different from Roslyn-Backed-MCP); **scope** (e.g. fix-all + rename + format — which sub-steps 6a–6i you actually applied); **notable symbols or files** touched; **MCP tools used** for each change (e.g. `rename_apply`, `fix_all_apply`); **verification** (`compile_check` / `test_run` / `build_workspace` outcomes). If Phase 6 had **no** applies (skipped or audit-only), state **N/A** with one line why. Optional: git commit hash or PR link if available.
6. **Concurrency mode matrix (Phase 8b)** — Required when Phase 8b ran in either single-mode or dual-mode. Three sub-tables (probe set, sequential baseline, parallel fan-out + behavioral verification) using the schema in the markdown template below. Every cell filled or `N/A`. If Phase 8b was `blocked` for the entire run, write a one-line reason in this section instead of the tables and continue.
7. **Writer reclassification verification (Phase 8b.5)** — One-row-per-tool table for the six writers (`apply_text_edit`, `apply_multi_file_edit`, `revert_last_apply`, `set_editorconfig_option`, `set_diagnostic_severity`, `add_pragma_suppression`). Columns: `tool`, `session-A status`, `session-B status`, `wall-clock A (ms)`, `wall-clock B (ms)`, `notes`. Use `skipped-safety` cells when only one session ran.
8. **Debug log capture** — Verbatim copy of every `Warning`/`Error`/`Critical` MCP log notification surfaced during the audit, plus any `Information` notification that mentions workspace lifecycle, gate acquisition, lock contention, rate-limit hits, or request timeouts. Each entry on its own line with `correlationId` (if present), `timestamp`, `level`, `logger`, and `message`. If the client did not surface log notifications, state `client did not surface MCP log notifications` here and in the header.
9. **MCP server issues (bugs)** — For each issue: **Tool name**, **inputs**, **expected** vs **actual**, **severity** (crash / incorrect result / missing data / degraded performance / cosmetic / error-message-quality), **reproducibility** (always / sometimes / once), **lock mode** (`legacy-mutex` / `rw-lock` / `both`). Watch for: crashes, wrong or incomplete results, tool-vs-tool inconsistency, missing functionality, slow tools, bad JSON/truncation, bad line/position mapping, unhelpful error messages, **lock-mode-specific** misbehavior (different result under one mode but not the other).
10. **Improvement suggestions** — Things that work correctly but could work better. UX friction (confusing parameter names, unexpected defaults, verbose output). Missing convenience features (e.g., “I wished tool X also returned Y”). Workflow gaps (sequences of 3+ tool calls that could be a single composite tool). Output enrichment (e.g., numeric codes where string labels would be more useful). Schema/description mismatches (tool description says one thing, actual behavior does another). These are not bugs — they are actionable input for the next release.
11. **Performance observations** — List any tools that were notably slow, with tool name, input scale, lock mode, and approximate wall-clock time. If all tools responded within expected bounds, state that.
12. **Known issue regression check** — For the 3–5 items re-tested in Phase 18, state: **still reproduces**, **partially fixed** (describe), or **candidate for closure** (no longer reproduces). Include the source id / issue id and one-line summary for each. If no prior source existed, state **N/A**.
13. **Known issue cross-check** — List any *newly observed* issues that match the chosen prior source (for example a backlog id, issue number, or previous audit finding) with one line each; put **full write-ups only for new or different** behavior.

If there are **zero** new issues, still write sections 1–8 and 10–13; in section 9 state **“No new issues found”** and confirm the audit ran.

### Markdown template (copy, fill in, save)

```markdown
# MCP Server Audit Report

## Header
- **Date:**
- **Audited solution:**
- **Audited revision:** (commit / branch if available)
- **Entrypoint loaded:**
- **Audit mode:** (`full-surface` by default; state reason if `conservative`)
- **Isolation:** (disposable branch/worktree/clone path, or conservative rationale)
- **Client:** (name/version; say if prompt or resource invocation was client-blocked)
- **Workspace id:**
- **Server:** (from `server_info`)
- **Roslyn / .NET:** (if reported)
- **Scale:** ~N projects, ~M documents
- **Repo shape:**
- **Prior issue source:**
- **Lock mode at audit time:** (`legacy-mutex` / `rw-lock` / `dual-mode (legacy-mutex → rw-lock)` / `dual-mode (rw-lock → legacy-mutex)`)
- **Lock mode source of truth:** (`launch-config` / `shell-env` / `behavioral-fingerprint`)
- **Debug log channel:** (`yes` — MCP `notifications/message` surfaced; `partial` — only some levels; `no` — client did not surface log notifications)
- **Report path note:** (if not saved under Roslyn-Backed-MCP, state intended copy destination)

## Coverage summary
| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | workspace | | | | | | | |
| tool | refactoring | | | | | | | |
| resource | workspace | | n/a | n/a | | | | |
| prompt | prompts | | n/a | n/a | | | | |

## Coverage ledger
| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| tool | `workspace_load` | stable | exercised | 0 | |
| tool | `create_file_apply` | experimental | skipped-safety | 10 | conservative audit on non-disposable repo |
| resource | `server_catalog` | stable | exercised | 0, 15 | |
| resource | `workspace_diagnostics` | stable | blocked | 15 | client cannot read workspace-scoped resources directly; limitation confirmed from the MCP client surface |
| prompt | `discover_capabilities` | experimental | exercised | 16 | |
| prompt | `security_review` | experimental | blocked | 16 | client does not expose prompt invocation; live catalog still lists the prompt |

## Verified tools (working)
- `tool_name` — one-line observation
- …

## Phase 6 refactor summary
- **Target repo:** (name / path; same as audited solution if in-repo)
- **Scope:** (which of 6a–6i applied; or **N/A** — no applies, with reason)
- **Changes:** bullets — symbol/file, tool used (`rename_apply`, etc.)
- **Verification:** `compile_check` / `test_run` / `build_workspace` — outcome
- **Optional:** commit / PR reference

## Concurrency mode matrix (Phase 8b)

> Required when Phase 8b ran. If Phase 8b was `blocked` for the entire run, replace these tables with one line: `Phase 8b blocked — <reason>`.

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
| Slot | Session A mode | Session A ms | Session B mode | Session B ms | Δ ms (B − A) | Notes |
|------|----------------|---------------|----------------|---------------|---------------|-------|
| R1 | | | | | | |
| R2 | | | | | | |
| R3 | | | | | | |
| R4 | | | | | | |
| R5 | | | | | | |
| W1 | | | | | | |
| W2 | | | | | | |

### Parallel fan-out and behavioral verification
- **Host logical cores:** _(record before filling the table — drives the chosen N)_
- **Chosen N (fan-out):** _N = `min(4, max(2, logical_cores))`_

| Slot | Mode | Parallel wall-clock (ms) | Speedup vs baseline | Expected (legacy ≤1.3× / rw-lock ≥`0.7 × N`) | Pass / FLAG / FAIL | Notes |
|------|------|---------------------------|----------------------|------------------------------------------------|---------------------|-------|
| R1 ×N | legacy-mutex | | | ≤1.3× | | |
| R1 ×N | rw-lock | | | ≥`0.7 × N` | | |
| R2 ×N | legacy-mutex | | | ≤1.3× | | |
| R2 ×N | rw-lock | | | ≥`0.7 × N` | | |
| R3 ×N | legacy-mutex | | | ≤1.3× | | |
| R3 ×N | rw-lock | | | ≥`0.7 × N` | | |

### Read/write exclusion behavioral probe (Phase 8b.3)
| Mode | Reader→Writer (`waits` / `does-not-wait`) | Writer→Reader (`waits` / `does-not-wait`) | Expected | Pass / FLAG / FAIL | Notes |
|------|--------------------------------------------|--------------------------------------------|----------|---------------------|-------|
| legacy-mutex | | | both `does-not-wait` (separate `<wsId>` and `__apply__:<wsId>` semaphores) | | |
| rw-lock | | | both `waits` (single per-workspace `AsyncReaderWriterLock`) | | |

### Lifecycle stress (Phase 8b.4)
| Probe | Mode | Observed (`waits-for-reader` / `runs-concurrently` / `errors`) | Reader saw (`stale` / `fresh` / `error`) | Reader exception (if any) | `correlationId` | Expected | Pass / FLAG / FAIL | Notes |
|-------|------|---------|---------|----------|-----------------|----------|---------------------|-------|
| reader + `workspace_reload` | legacy-mutex | | | | | `runs-concurrently` (separate `__apply__:<wsId>` semaphore) | | |
| reader + `workspace_reload` | rw-lock | | | | | `waits-for-reader` (per-workspace write lock) | | |
| reader + `workspace_close` | legacy-mutex | | | | | `runs-concurrently`; reader may surface `KeyNotFoundException` via post-acquire recheck | | |
| reader + `workspace_close` | rw-lock | | | | | `waits-for-reader`; reader completes cleanly | | |

## Writer reclassification verification (Phase 8b.5)
| # | Tool | Session A status | Session B status | Wall-clock A (ms) | Wall-clock B (ms) | Notes |
|---|------|-------------------|-------------------|--------------------|--------------------|-------|
| 1 | `apply_text_edit` | | | | | |
| 2 | `apply_multi_file_edit` | | | | | |
| 3 | `revert_last_apply` | | | | | shared with Phase 9 evidence |
| 4 | `set_editorconfig_option` | | | | | |
| 5 | `set_diagnostic_severity` | | | | | |
| 6 | `add_pragma_suppression` | | | | | |

## Debug log capture
> Verbatim copy of structured `notifications/message` log entries surfaced by the MCP client during the audit. Filter to: every `Warning`/`Error`/`Critical` entry, plus any `Information` entry that mentions workspace lifecycle, gate acquisition, lock contention, rate-limit hits, or request timeouts. If the client did not surface log notifications, write: `client did not surface MCP log notifications` and skip the table.

| `timestamp` | `level` | `logger` | `correlationId` | `eventName` | `message` | Phase | Tool in flight |
|-------------|---------|----------|-----------------|-------------|-----------|-------|----------------|
| | | | | | | | |

## MCP server issues (bugs)
### 1. (title or tool name)
| Field | Detail |
|--------|--------|
| Tool | |
| Input | |
| Expected | |
| Actual | |
| Severity | |
| Reproducibility | |
| Lock mode (legacy-mutex / rw-lock / both) | |

## Improvement suggestions
- `tool_name` — suggestion (UX friction / missing feature / workflow gap / output enrichment / schema mismatch)
- …

## Performance observations
| Tool | Input scale | Lock mode | Wall-clock (ms) | Notes |
|------|-------------|-----------|------------------|-------|
| | | | | |

(or state "All tools responded within expected bounds")

## Known issue regression check
| Source id | Summary | Status |
|-----------|---------|--------|
| issue-or-backlog-id | one-line summary | still reproduces / partially fixed / candidate for closure |

## Known issue cross-check
- …
```

### Completion gate

The file must exist at the canonical path above (or the documented fallback). Create `ai_docs/audit-reports/` if missing. The task is **incomplete** without this file.

---

## Tools Reference — Complete Surface Inventory

> **Maintenance note:** This appendix must be kept in sync with the actual server surface.
> The live catalog from Phase 0 is authoritative. Treat this appendix as a convenience snapshot; if it drifts from the running server, record prompt drift and trust the live catalog.
> When tools, resources, or prompts change, update this section.
> Client limitations do **not** change the appendix counts. If a client cannot invoke prompts or resources, record those entries as `blocked` in the audit ledger rather than editing them out of the documented surface.
> Last verified: 2026-04-06 against catalog version 2026.03 — **123 tools (56 stable / 67 experimental) | 7 resources (7 stable) | 16 prompts (16 experimental)**

### Tools by Category (123 total)

#### Server (1 tool)
`server_info`

#### Workspace (8 tools)
`workspace_load` `workspace_reload` `workspace_close` `workspace_list` `workspace_status` `project_graph` `source_generated_documents` `get_source_text`

#### Symbols (16 tools)
`symbol_search` `symbol_info` `go_to_definition` `goto_type_definition` `find_references` `find_references_bulk` `find_implementations` `find_overrides` `find_base_members` `member_hierarchy` `document_symbols` `enclosing_symbol` `symbol_signature_help` `symbol_relationships` `find_property_writes` `get_completions`

#### Analysis (7 tools)
`project_diagnostics` `diagnostic_details` `type_hierarchy` `callers_callees` `impact_analysis` `find_type_mutations` `find_type_usages`

#### Advanced Analysis (7 tools)
`find_unused_symbols` `get_di_registrations` `get_complexity_metrics` `find_reflection_usages` `get_namespace_dependencies` `get_nuget_dependencies` `semantic_search`

#### Consumer & Cohesion Analysis (3 tools)
`find_consumers` `get_cohesion_metrics` `find_shared_members`

#### Security (3 tools)
`security_diagnostics` `security_analyzer_status` `nuget_vulnerability_scan`

#### Flow Analysis (2 tools)
`analyze_data_flow` `analyze_control_flow`

#### Syntax & Operations (2 tools)
`get_syntax_tree` `get_operations`

#### Compilation (1 tool)
`compile_check`

#### Analyzer Info (1 tool)
`list_analyzers`

#### Snippet & Scripting (2 tools)
`analyze_snippet` `evaluate_csharp`

#### Refactoring — Rename, Format, Organize (8 tools)
`rename_preview` `rename_apply` `organize_usings_preview` `organize_usings_apply` `format_document_preview` `format_document_apply` `format_range_preview` `format_range_apply`

#### Code Actions & Fixes (5 tools)
`code_fix_preview` `code_fix_apply` `get_code_actions` `preview_code_action` `apply_code_action`

#### Fix All (2 tools)
`fix_all_preview` `fix_all_apply`

#### Interface Extraction (2 tools)
`extract_interface_preview` `extract_interface_apply`

#### Type Extraction (2 tools)
`extract_type_preview` `extract_type_apply`

#### Type Movement (2 tools)
`move_type_to_file_preview` `move_type_to_file_apply`

#### Bulk Refactoring (2 tools)
`bulk_replace_type_preview` `bulk_replace_type_apply`

#### Cross-Project Refactoring (3 tools)
`move_type_to_project_preview` `extract_interface_cross_project_preview` `dependency_inversion_preview`

#### Orchestration (4 tools)
`migrate_package_preview` `split_class_preview` `extract_and_wire_interface_preview` `apply_composite_preview`

#### Dead Code Removal (2 tools)
`remove_dead_code_preview` `remove_dead_code_apply`

#### Text Editing (2 tools)
`apply_text_edit` `apply_multi_file_edit`

#### File Operations (6 tools)
`create_file_preview` `create_file_apply` `delete_file_preview` `delete_file_apply` `move_file_preview` `move_file_apply`

#### Project Mutation (11 tools)
`add_package_reference_preview` `remove_package_reference_preview` `add_project_reference_preview` `remove_project_reference_preview` `set_project_property_preview` `set_conditional_property_preview` `add_target_framework_preview` `remove_target_framework_preview` `add_central_package_version_preview` `remove_central_package_version_preview` `apply_project_mutation`

#### Scaffolding (4 tools)
`scaffold_type_preview` `scaffold_type_apply` `scaffold_test_preview` `scaffold_test_apply`

#### Validation — Build & Test (7 tools)
`build_workspace` `build_project` `test_discover` `test_run` `test_related` `test_related_files` `test_coverage`

#### EditorConfig (2 tools)
`get_editorconfig_options` `set_editorconfig_option`

#### MSBuild evaluation (3 tools)
`evaluate_msbuild_property` `evaluate_msbuild_items` `get_msbuild_properties`

#### Suppression and severity (2 tools)
`set_diagnostic_severity` `add_pragma_suppression`

#### Undo (1 tool)
`revert_last_apply`

### Resources (2026.03 snapshot: 7 total)

If a client cannot list or read one of these resources, keep the resource in the audit ledger with status `blocked` and note the client limitation explicitly. Do not treat that as prompt drift or as a missing server surface entry.

| URI Template | Description | MIME Type |
|---|---|---|
| `roslyn://server/catalog` | Machine-readable surface inventory and support policy | `application/json` |
| `roslyn://server/resource-templates` | Lists all resource URI templates, including workspace-scoped templates | `application/json` |
| `roslyn://workspaces` | List active workspace sessions | `application/json` |
| `roslyn://workspace/{workspaceId}/status` | Workspace status and diagnostics | `application/json` |
| `roslyn://workspace/{workspaceId}/projects` | Project graph metadata | `application/json` |
| `roslyn://workspace/{workspaceId}/diagnostics` | Compiler diagnostics | `application/json` |
| `roslyn://workspace/{workspaceId}/file/{filePath}` | Source file content | `text/x-csharp` |

### Prompts (2026.03 snapshot: 16 total)

If a client cannot enumerate or invoke prompts, keep the live prompt rows in the audit ledger with status `blocked` and name the client limitation. The live catalog, not the client UI, determines prompt presence.

| Prompt | Description |
|---|---|
| `explain_error` | Explain a compiler diagnostic |
| `suggest_refactoring` | Suggest refactorings for a symbol or region |
| `review_file` | Review a source file |
| `analyze_dependencies` | Architecture and dependency analysis |
| `debug_test_failure` | Debug a failing test |
| `refactor_and_validate` | Preview-first refactoring with validation |
| `fix_all_diagnostics` | Batched diagnostic cleanup |
| `guided_package_migration` | Package migration across projects |
| `guided_extract_interface` | Interface extraction and consumer updates |
| `security_review` | Comprehensive security review |
| `discover_capabilities` | Discover relevant tools for a task category |
| `dead_code_audit` | Dead code detection and removal |
| `review_test_coverage` | Test coverage review and gap identification |
| `review_complexity` | Complexity review and refactoring opportunities |
| `cohesion_analysis` | SRP analysis via LCOM4 with guided type extraction |
| `consumer_impact` | Consumer/dependency graph analysis for refactoring impact |

### Tool Source Files

| File | Count | Categories |
|---|---|---|
| `ServerTools.cs` | 1 | Server |
| `WorkspaceTools.cs` | 8 | Workspace |
| `SymbolTools.cs` | 16 | Symbols |
| `AnalysisTools.cs` | 7 | Analysis |
| `AdvancedAnalysisTools.cs` | 7 | Advanced Analysis |
| `ConsumerAnalysisTools.cs` | 1 | Consumer Analysis |
| `CohesionAnalysisTools.cs` | 2 | Cohesion Analysis |
| `SecurityTools.cs` | 3 | Security |
| `FlowAnalysisTools.cs` | 2 | Flow Analysis |
| `SyntaxTools.cs` | 1 | Syntax |
| `OperationTools.cs` | 1 | Operations |
| `CompileCheckTools.cs` | 1 | Compilation |
| `AnalyzerInfoTools.cs` | 1 | Analyzer Info |
| `SnippetAnalysisTools.cs` | 1 | Snippet Analysis |
| `ScriptingTools.cs` | 1 | Scripting |
| `RefactoringTools.cs` | 8 | Rename, Format, Organize |
| `CodeActionTools.cs` | 5 | Code Actions & Fixes |
| `FixAllTools.cs` | 2 | Fix All |
| `InterfaceExtractionTools.cs` | 2 | Interface Extraction |
| `TypeExtractionTools.cs` | 2 | Type Extraction |
| `TypeMoveTools.cs` | 2 | Type Movement |
| `BulkRefactoringTools.cs` | 2 | Bulk Refactoring |
| `CrossProjectRefactoringTools.cs` | 3 | Cross-Project Refactoring |
| `OrchestrationTools.cs` | 4 | Orchestration |
| `DeadCodeTools.cs` | 2 | Dead Code Removal |
| `EditTools.cs` | 1 | Text Editing |
| `MultiFileEditTools.cs` | 1 | Text Editing |
| `FileOperationTools.cs` | 6 | File Operations |
| `ProjectMutationTools.cs` | 11 | Project Mutation |
| `ScaffoldingTools.cs` | 4 | Scaffolding |
| `ValidationTools.cs` | 7 | Build, Test, Coverage |
| `EditorConfigTools.cs` | 2 | EditorConfig |
| `MSBuildTools.cs` | 3 | MSBuild evaluation |
| `SuppressionTools.cs` | 2 | Suppression and severity |
| `UndoTools.cs` | 1 | Undo |
