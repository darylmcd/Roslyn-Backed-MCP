---
generated_at: 2026-04-24T02:57:40Z
source_repo: dotnet-firewall-analyzer
source_repo_path: C:\Code-Repo\DotNet-Firewall-Analyzer
session_phase: mixed
---

# Roslyn MCP session retrospective — dotnet-firewall-analyzer — 2026-04-24

## 1. Session classification

**mixed** — the session was a full-surface Roslyn MCP audit run (the dominant activity, ~95% of tool calls) with two real Phase-6 code mutations committed to a throwaway branch (the remaining ~5% was C# semantic refactoring: one `organize_usings_apply`, one `rename_apply`). It was not purely refactoring, not release/operational, not planning-docs. The lens below therefore covers both refactoring-adjacent friction (symbol_search, rename, extract_type, etc.) and the broader coverage/contract/ergonomics surface (metrics, diagnostics, prompts, resources).

## 2. Task inventory

Only non-trivial file-modifying or semantically-notable tasks are listed — pure read probes are in the audit report.

| Task (one-line verb phrase) | Tool actually used | File type / domain | Right tool for the job? |
|---|---|---|---|
| Create disposable isolation target (worktree) | `git worktree add` | git | ✓ right tool |
| Pivot to throwaway branch after MCP `AllowedRoots` rejected worktree path | `git checkout -b audit-run-20260424` (after `git stash`) | git | ✓ right tool (MCP sandbox forces this) |
| Restore NuGet packages before semantic tools | `dotnet restore` | build | ✓ right tool (prompt precondition) |
| Load workspace (repeated 3× after host restarts) | `mcp__roslyn__workspace_load` | Roslyn/semantic | ✓ right tool |
| Warm compilation cache | `mcp__roslyn__workspace_warm` | Roslyn/semantic | ✓ right tool |
| Broad diagnostics scan | `project_diagnostics`, `compile_check`, `security_diagnostics`, `nuget_vulnerability_scan`, `list_analyzers`, `diagnostic_details` | Roslyn/semantic | ✓ right tool |
| Code-quality metrics | `get_complexity_metrics`, `get_cohesion_metrics`, `get_coupling_metrics`, `find_unused_symbols`, `find_duplicated_methods`, `find_duplicate_helpers`, `find_dead_locals`, `find_dead_fields`, `get_namespace_dependencies`, `get_nuget_dependencies`, `suggest_refactorings` | Roslyn/semantic | ✓ right tool |
| Deep symbol analysis on 5 key types | `symbol_search`, `symbol_info`, `document_symbols`, `type_hierarchy`, `find_implementations`, `find_references`, `find_consumers`, `find_shared_members`, `find_type_mutations`, `find_type_usages`, `callers_callees`, `find_property_writes`, `member_hierarchy`, `symbol_relationships`, `symbol_signature_help`, `impact_analysis`, `probe_position`, `symbol_impact_sweep` | Roslyn/semantic | ✓ right tool |
| Flow analysis on complex methods | `get_source_text`, `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree`, `trace_exception_flow` | Roslyn/semantic | ✓ right tool |
| Snippet/script validation | `analyze_snippet`, `evaluate_csharp` | Roslyn/semantic | ✓ right tool |
| Remove unused using from `SettingsEndpoints.cs` | `organize_usings_preview` → `apply_with_verify` | C# refactor | ✓ right tool (landed commit) |
| Rename `DriftDetector.ListsEqual` → `StringListsEqual` | `rename_preview` → `apply_with_verify` | C# refactor | ✓ right tool (9 callsites updated) |
| Preview extract type for `OfflineImporter.ImportApiResponsesAsync` | `extract_type_preview` | C# refactor | ✓ right tool (correctly refused — external consumers) |
| Preview extract method on `AreRulesEqual` | `extract_method_preview` | C# refactor | ✓ right tool (actionable error on expression-body selection) |
| Preview remove dead field `_factory` | `remove_dead_code_preview` | C# refactor | ✓ right tool (cross-tool safety refusal) |
| EditorConfig severity override + revert | `set_diagnostic_severity` → `revert_last_apply` | editorconfig | ✓ right tool |
| MSBuild property/item inspection | `get_msbuild_properties`, `evaluate_msbuild_property`, `evaluate_msbuild_items` | MSBuild | ✓ right tool (partial — see 2a) |
| Test discovery + related-test mapping | `test_discover`, `test_related_files`, `test_related`, `test_reference_map`, `validate_workspace`, `validate_recent_git_changes` | test | ✓ right tool (partial — see 2a) |
| DI / reflection / source-gen audit | `get_di_registrations`, `find_reflection_usages`, `source_generated_documents` | Roslyn/semantic | ✓ right tool |
| Scaffolding previews | `scaffold_type_preview`, `scaffold_test_preview`, `scaffold_test_batch_preview`, `scaffold_first_test_file_preview` | C# scaffold | ✓ right tool |
| Project-mutation previews (CPM/nullable/conditional) | `add_package_reference_preview`, `set_project_property_preview`, `add_central_package_version_preview`, `set_conditional_property_preview` | MSBuild | ✓ right tool (1 vague error — see 2a) |
| File orchestration previews | `move_type_to_file_preview`, `create_file_preview` | file edits | ✓ right tool |
| Semantic search probes | `semantic_search` | search | ✓ right tool (minor dedup quibble) |
| Prompt rendering + negative probes | `get_prompt_text` | prompts | ✓ right tool |
| Commit Phase 6 changes | `git commit` via `Bash` | git | ✓ right tool |
| Write canonical audit report (70 KB) | `Write` | markdown | ✓ right tool |
| Read audit prompt (857 lines) | `Read` | markdown | ✓ right tool |

**No missed opportunities** — every task was routed to the correct surface. All C#/semantic work went through Roslyn MCP; git/build/commit went through Bash; documentation through Write/Edit; the prompt read via Read. Where Roslyn MCP refused a mutation (extract_type, remove_dead_code), the refusal was the right answer; no shadow-Edit workaround was needed.

## 2a. Roslyn MCP issues encountered

### Issue 1 — `symbol_search("")` dumps 70 KB enumerating workspace
- **Tool:** `mcp__roslyn__symbol_search`
- **Inputs:** `query=""` (empty), workspaceId valid
- **Symptom:** response of `70,462 characters` that the client truncated and wrote to disk:
  > `Error: result (70,462 characters) exceeds maximum allowed tokens. Output has been saved to C:\Users\daryl\.claude\projects\...\tool-results\mcp-roslyn-symbol_search-1776998401462.txt.`
- **Impact:** Phase 17c.1 negative probe was expected to return empty result / clear error; instead blew client output budget. Roughly 1 turn lost to classifying.
- **Workaround:** classified as the single P2 finding in the audit report; did not re-query with empty string.
- **Repro confidence:** deterministic

### Issue 2 — `find_implementations(ISnapshotStore)` returns source-generated partials as distinct implementations
- **Tool:** `mcp__roslyn__find_implementations`
- **Inputs:** `metadataName="FirewallAnalyzer.Domain.Interfaces.ISnapshotStore"`
- **Symptom:** 3 hits — 1 real `FileSnapshotStore` at `Storage\FileSnapshotStore.cs:14`, plus 2 `partial class FileSnapshotStore` entries from `obj\Debug\net10.0\Microsoft.Gen.Logging\...\Logging.g.cs:499` and `...\System.Text.RegularExpressions.Generator\...\RegexGenerator.g.cs:7`. Those are additional decl files for the same type, not separate implementations.
- **Impact:** Could mislead a tool-chain building an implementation-count metric or a test-coverage heuristic. No direct blocker in this run.
- **Workaround:** Filtered by eye; noted in §14.4 of the audit report.
- **Repro confidence:** deterministic on any solution with source generators emitting interface-adjacent partials

### Issue 3 — Inconsistent auto-retry across MSBuild-family tools after mutation
- **Tool:** `mcp__roslyn__get_msbuild_properties`, `evaluate_msbuild_property`, `get_editorconfig_options`
- **Inputs:** same batch post-Phase-6-apply; workspace was mid-reload (staleAction expected)
- **Symptom:** three sibling tools errored with:
  > `Invalid operation: Project 'FirewallAnalyzer.Api' not found in workspace '…'. Loaded projects (0): (none — the workspace has no projects loaded). Pass a matching 'project' value (project name or absolute .csproj path).. The workspace may need to be reloaded (workspace_reload) if the state is stale.`
  while **`evaluate_msbuild_items` in the same batch auto-retried successfully** (`staleAction: "auto-reloaded", staleReloadMs: 9416`).
- **Impact:** 1 turn lost to manually issuing `workspace_reload`. Contract drift — siblings in the same family behave differently.
- **Workaround:** `workspace_reload` then retry.
- **Repro confidence:** likely (race with mid-apply state); hard to repro in isolation

### Issue 4 — `test_reference_map` returns misleading "No test projects detected" note when filtered to a productive project
- **Tool:** `mcp__roslyn__test_reference_map`
- **Inputs:** `projectName="FirewallAnalyzer.Application"`, `limit=5`
- **Symptom:**
  > `"coveragePercent": 0, "inspectedTestProjects": [], "notes": ["No test projects detected (IsTestProject=true). Coverage defaults to 0%."], "totalUncoveredCount": 180`
  This is factually wrong — the workspace has 6 test projects (`*.Tests`). The intended semantics appear to be "no test projects matched the filter" but the note reads as "no test projects exist."
- **Impact:** Confusing output; small cognitive tax while classifying.
- **Workaround:** Ignored the note; cross-read against `project_graph` which listed all 6 test projects.
- **Repro confidence:** deterministic

### Issue 5 — `test_related` and `test_related_files` return different response envelopes
- **Tool:** `mcp__roslyn__test_related` vs `mcp__roslyn__test_related_files`
- **Inputs:** same workspace, `test_related(metadataName=DriftDetector)` and `test_related_files(filePaths=[...DriftDetector.cs,...SettingsEndpoints.cs])`
- **Symptom:** `test_related` returned bare array `[{displayName, fullyQualifiedName, filePath, line}, …]`; `test_related_files` returned `{tests: [{displayName, fullyQualifiedName, projectName, filePath, line, triggeredByFiles}, …], dotnetTestFilter: "…"}`. Downstream consumers that grab `dotnetTestFilter` from one have to rebuild it for the other.
- **Impact:** Forces schema-branching in any tool chain that combines both.
- **Workaround:** Used `test_related_files` for filter composition.
- **Repro confidence:** deterministic

### Issue 6 — `diagnostic_details` single-symbol reads took 5.9 s and 7.2 s
- **Tool:** `mcp__roslyn__diagnostic_details`
- **Inputs:** CA1859 at `SettingsEndpoints.cs:42:28` and ASP0015 at `SecurityHeadersMiddleware.cs:21:13`
- **Symptom:** `_meta.elapsedMs: 7193` and `5963`, both exceed the prompt's 5 s budget for single-symbol reads. Content of the response is cheap (description + helpLink + supportedFixes=[]).
- **Impact:** Pacing hit during Phase 1; two back-to-back calls consumed ~13 s of wall-clock.
- **Workaround:** None needed — just slower than budget.
- **Repro confidence:** deterministic

### Issue 7 — `diagnostic_details.supportedFixes` always empty on probed diagnostics
- **Tool:** `mcp__roslyn__diagnostic_details`
- **Inputs:** CA1859, ASP0015
- **Symptom:** Both probes returned `"supportedFixes": [], "guidanceMessage": null`. Tool description advertises "curated fix options".
- **Impact:** Consumers expecting fix-ids to pipe into `code_fix_preview` get nothing; have to fall back to `fix_all_preview` heuristics.
- **Workaround:** Used `fix_all_preview(scope="solution")` which returned excellent actionable `guidanceMessage` instead.
- **Repro confidence:** deterministic on these two ids

### Issue 8 — `set_conditional_property_preview` rejects idiomatic MSBuild condition without showing accepted syntax
- **Tool:** `mcp__roslyn__set_conditional_property_preview`
- **Inputs:** `projectName=FirewallAnalyzer.Domain, propertyName=LangVersion, value=latest, condition="$(Configuration) == 'Debug'"`
- **Symptom:**
  > `Invalid operation: Conditional property updates only support equality conditions on $(Configuration), $(TargetFramework), or $(Platform).`
  The condition passed IS an equality condition on `$(Configuration)`. Either the tool expects wrapped form `"'$(Configuration)' == 'Debug'"` or a bare RHS `"Debug"`.
- **Impact:** Tool unusable without trial-and-error on accepted shape.
- **Workaround:** Marked preview-only with a FLAG; did not chase the exact syntax.
- **Repro confidence:** deterministic

### Issue 9 — `validate_recent_git_changes` surfaced unstructured "An error occurred invoking" error
- **Tool:** `mcp__roslyn__validate_recent_git_changes`
- **Inputs:** `workspaceId=<current>, summary=true`
- **Symptom:**
  > `An error occurred invoking 'validate_recent_git_changes'.`
  No `category`, no `exceptionType`, no `_meta` in the payload — a bare string.
- **Impact:** Cannot triage without additional logs; not surfaced via any debug-log channel in this client.
- **Workaround:** Used `validate_workspace(summary=true)` directly with explicit `changedFilePaths` — worked cleanly (overallStatus=clean, 716ms compile, 0 errors).
- **Repro confidence:** one-shot (may have collided with a system-reminder; worth retrying on a different client)

### Issue 10 — Workspace evicted between parallel tool calls (host transparently restarted stdio server)
- **Tool:** `mcp__roslyn__*` family (affects every workspace-scoped tool)
- **Inputs:** any post-restart call with a pre-restart `workspaceId`
- **Symptom:** first occurrence during Phase 1 negative probes after `ToolSearch` schema fetch —
  > `"Not found: Workspace 'eb6aa99a410540998f9ccfdbb4d7386c' not found or has been closed. Active workspace IDs are listed by workspace_list.."`
  `server_info.connection.stdioPid` changed 40168 → 49920 → 48284 between turns (3 restart events observed during the ~14-minute run).
- **Impact:** Each restart forced a ~4-5 s `workspace_load` + 2 s `workspace_warm` cycle. Maybe 20 s of aggregate wall-clock lost across 3 restarts.
- **Workaround:** Documented in audit-report header as expected upstream behavior per the prompt's host-restart note; `workspace_load` re-issued each time.
- **Repro confidence:** intermittent — client-host-driven, not server-driven. Not a Roslyn MCP bug; still a pacing issue for any multi-turn audit.

### Issue 11 — `get_completions(filterText="To")` missed in-scope `ToString` per v1.8+ ranking contract
- **Tool:** `mcp__roslyn__get_completions`
- **Inputs:** `filePath=DriftDetector.cs, line=37, column=13, filterText="To", maxItems=10`
- **Symptom:** 7 long-tail types returned (`ToBase64Transform`, `TokenAccessLevels`, `ToolboxItemAttribute`, …); no `ToString` — but the prompt's v1.8+ contract says in-scope members should outrank namespace-qualified externals for `filterText="To"`.
- **Impact:** Possibly the position wasn't on a receiver (line 37 col 13 is inside a method body after whitespace, before an identifier token), so the ranking trigger didn't apply. Still, the response is misleading without context.
- **Workaround:** Documented as FLAG-low in §7 of the audit report.
- **Repro confidence:** deterministic at this position

### Issue 12 — `semantic_search("classes implementing IDisposable")` returned duplicate `JobProcessor` entries
- **Tool:** `mcp__roslyn__semantic_search`
- **Inputs:** `query="classes implementing IDisposable", limit=10`
- **Symptom:** `JobProcessor` appears twice consecutively in `results[]` with identical `fullyQualifiedName` and `symbolHandle`. Two rows out of 10 wasted on the duplicate.
- **Impact:** Minor — reduces effective `limit` by 1.
- **Workaround:** None.
- **Repro confidence:** deterministic

### Issue 13 — MSBuild probe batch lost 3 tools during `staleAction=auto-reloaded` state, while a 4th retried cleanly
- **Tool:** `get_msbuild_properties`, `evaluate_msbuild_property`, `get_editorconfig_options` vs `evaluate_msbuild_items`
- (dup of Issue 3 in narrative terms; kept separately because it also shows `get_editorconfig_options` errored with `FileNotFound` during the same race, not `InvalidOperation` — different error category for the same root cause.)
- **Symptom:**
  > `get_editorconfig_options: "File not found: Document not found in workspace: …SettingsEndpoints.cs. Verify the file path is absolute and the file exists on disk. If the workspace was recently reloaded, the file may have been removed."` (staleAction=auto-reloaded, staleReloadMs=3088)
- **Impact:** Same root cause as Issue 3; surfaces in `get_editorconfig_options` as `FileNotFound` which is especially misleading (the file very much exists on disk — the workspace document list was transiently empty).
- **Workaround:** `workspace_reload`.
- **Repro confidence:** likely

**No blocker errors, no corruption, no silent wrong results on any stable-surface read.** Most of the issues are contract-consistency / ergonomics / timing on the experimental tier or on narrow negative paths.

## 2b. Missing tool gaps

Very few gaps — the 1.29.0 surface genuinely covers C#/semantic work for this repo shape. The ones I did hit:

### Gap 1 — No semantic "dead branch in override chain" detector distinct from `remove_dead_code`
- **Task:** confirm whether `_factory` field in `ValidationFilterIntegrationTests` was safe to remove after `find_dead_fields(usageKind=never-read)` flagged it.
- **Why Roslyn-shaped:** the flagged field had `writeReferenceCount=1` (ctor assignment). `remove_dead_code_preview` correctly refused — but there's no tool that follows up with *"here's the ctor line that writes to it; remove that line too and the field becomes removable"* in one pass.
- **Proposed tool shape:** `explain_removable_blockers(symbolHandle)` → `{ blockers: [{kind: "ConstructorWrite" | "SerializationAttribute" | "ReflectionUsage", filePath, startLine, suggestedEdit?}], nextStep: "edit this line then retry remove_dead_code_preview" }`. Bundles the intelligence that the user has to triangulate by hand today.
- **Closest existing tool:** `remove_dead_code_preview` error message names the refusal cause but doesn't enumerate the blocker locations or suggest a fix edit.

### Gap 2 — No bulk "audit-only apply + revert round-trip" helper for Phase 9-style undo verification
- **Task:** Exercise `revert_last_apply` on a disposable apply that didn't disturb real Phase 6 commits.
- **Why Roslyn-shaped:** canonical workflow is `format_document_preview → apply → revert_last_apply` (prompt phase 9 step 1 explicitly canonicalizes this). Could be a single composite `probe_undo_stack(filePath)` that does the chain and verifies.
- **Proposed tool shape:** `probe_undo_roundtrip(workspaceId, filePath?)` → `{ prePhase6ApplyCount, auditOnlyApplyOk, revertOk, phase6AppliesIntact: true, elapsedMs }`. Small composite, purely diagnostic.
- **Closest existing tool:** none — you have to orchestrate the 3-call chain by hand (preview → apply_with_verify → revert_last_apply → workspace_changes).

### Gap 3 — No tool to list which prompts require which parameters without trial-and-error
- **Task:** Render the 20 experimental prompts cleanly in Phase 16.
- **Why Roslyn-shaped:** Each prompt has a different required-parameter set (`discover_capabilities` needs `taskCategory`; `explain_error` needs `workspaceId, diagnosticId, filePath, line, column`; etc.). The only way to discover is to call `get_prompt_text` with empty `parametersJson` and read the error message. That's 20 probe-calls just to enumerate the schema.
- **Proposed tool shape:** `prompts/list` channel should include parameter manifests (name, type, required, default, description), or add a companion tool `list_prompt_schemas` that returns the same. The `discover_capabilities` render with `taskCategory=refactoring` lists prompts but not their parameter shapes.
- **Closest existing tool:** `get_prompt_text` with missing-parameter probes leaks the info via errors — OK but not discoverable.

That's the full gap list. Everything else was covered.

## 3. Recurring friction patterns

### Pattern 1 — Stale-state races after `workspace_*` mutations, handled inconsistently across tool families
- **What happened:** After an apply landed, the next parallel batch saw mixed results: `evaluate_msbuild_items` auto-reloaded and succeeded (`staleAction: "auto-reloaded", staleReloadMs: 9417`) while `get_msbuild_properties`, `evaluate_msbuild_property`, and `get_editorconfig_options` in the same batch errored out with either `"Loaded projects (0)"` or `"Document not found in workspace"`. Hit this in Phase 7 (MSBuild) and in Phase 6 (editorconfig) separately.
- **Why it recurs:** Any multi-tool batch issued while the change-tracker is mid-refresh will have some members see a pre-reload snapshot. `evaluate_msbuild_items` has the auto-retry; its siblings don't.
- **What would fix it:** Lift the auto-reload code path up to the shared MSBuild/EditorConfig tool base class so every `project-lookup` tool behaves the same way. Alternatively, emit a consistent `staleAction: "rejected"` with a retry hint so clients can uniformly treat this as a soft error.

### Pattern 2 — `exercised-apply` Phase 6 pipeline works flawlessly; `preview-only` Phase 6 pipeline is rich but safety-stalled
- **What happened:** `organize_usings_preview → apply_with_verify` and `rename_preview → apply_with_verify` both landed clean (`status=applied, preErrorCount=0, postErrorCount=0`). Meanwhile `extract_type_preview`, `remove_dead_code_preview`, `extract_method_preview`, `replace_string_literals_preview`, `set_conditional_property_preview` all triggered correctly-safe refusals — but that left the `*_apply` siblings unreachable in this repo, forcing them into `skipped-safety` on the scorecard.
- **Why it recurs:** Clean, well-tested repos don't present many "obvious safe refactor" targets. The scorecard's promotion rubric penalizes `skipped-safety` as `needs-more-evidence` even when the preview path was fully exercised.
- **What would fix it:** Either (a) loosen the rubric so `exercised-preview-only with actionable refusal` counts as promotion evidence for the preview half while keeping the apply half as `needs-more-evidence`, or (b) provide a companion `synthesize_refactor_target` diagnostic that tells the audit where the "cleanest Phase 6 candidates" are (e.g. "this dead field has no ctor write, try remove_dead_code_preview on it").

### Pattern 3 — Response-shape inconsistency across paired tools
- **What happened:** `test_related` returns `[{…}]` bare; `test_related_files` returns `{tests, dotnetTestFilter}`. `diagnostic_details` returns `supportedFixes: []` while `fix_all_preview` returns `guidanceMessage: "<actionable>"`. `get_namespace_dependencies(circularOnly=true)` returns `nodes: [], edges: []` with no sentinel; `nuget_vulnerability_scan` returns `totalVulnerabilities: 0, criticalCount: 0, …` as explicit zero-fields. Three separate pairings with three separate shape conventions.
- **Why it recurs:** Every tool family was added at a different time. There's no enforced "empty-result sentinel" or "response-envelope" style guide.
- **What would fix it:** A one-page Response Contract Consistency style guide, plus a `verify_response_shapes` internal sweep before any new tool lands. The audit report's §18 cross-refs §7 rows.

### Pattern 4 — `find_dead_fields` and `remove_dead_code_preview` disagree on what "dead" means
- **What happened:** `find_dead_fields(usageKind=never-read)` returned `_factory` with `confidence=high`; `remove_dead_code_preview` on the same symbolHandle responded `"Symbol '_factory' still has references and cannot be removed safely."`. Both are right in their own lens (the field IS never read; it IS still written). The contract divergence makes it hard to auto-compose these tools.
- **Why it recurs:** Every audit that runs `find_dead_fields` → try removal will hit this for any test fixture field pattern (`private readonly T _fixture = factory;`). It's an xUnit IClassFixture shape too.
- **What would fix it:** Either (a) `find_dead_fields` grows a `safelyRemovable: bool` column, or (b) the tool description adds "never-read fields may still have constructor writes that block `remove_dead_code_preview`; combine with `find_property_writes` first" — cheap docs fix.

### Pattern 5 — 4 transparent stdio-server restarts during a 14-minute audit
- **What happened:** `server_info.connection.stdioPid` changed 40168 → 49920 → 48284 (and one intermediate restart). Each restart evicted the loaded workspace; `workspace_load` re-issue cost ~4-5 s of cold start plus ~2 s of warm-up.
- **Why it recurs:** This is a Claude Code CLI (client-host) behavior, not a Roslyn MCP server bug. It's documented in the prompt's own host-process-restart note. But any multi-turn Roslyn MCP session longer than ~5 minutes will hit it.
- **What would fix it:** Server-side, nothing. Client-side, the Claude Code CLI could auto-reissue `workspace_load` for any session workspaceId that gets `NotFound` after a restart. Alternatively, Roslyn MCP could accept `loadedPath` in any workspace-scoped call as a fallback for lost session id (opt-in, controlled by a `recoverFromLoadedPath=true` flag).

## 4. Suggested findings (up to 5)

### Finding 1
- **id:** `symbol-search-empty-query-overflow`
- **priority hint:** **high** — client-observable output overflow; breaks any consumer that defensively probes empty queries
- **title:** Reject `symbol_search(query="")` with structured error instead of enumerating workspace
- **summary:** `symbol_search("")` returned 70,462 characters ("Error: result (70,462 characters) exceeds maximum allowed tokens. Output has been saved to … tool-results/mcp-roslyn-symbol_search-1776998401462.txt"). The prompt's Phase 17c.1 contract says "empty result or clear error, not crash." A single-line guard (`if (string.IsNullOrWhiteSpace(query)) return { count: 0, symbols: [], note: "query must be non-empty" };`) would fix it.
- **proposed action:** server-side input guard + add regression test for empty-query probe
- **evidence:** `2a#symbol_search`, `3#Pattern 3` (empty-result sentinel consistency)

### Finding 2
- **id:** `msbuild-family-auto-retry-parity`
- **priority hint:** **medium** — contract drift bites any multi-tool batch issued after a mutation
- **title:** Lift `staleAction=auto-reloaded` behavior to all MSBuild-family tools
- **summary:** `evaluate_msbuild_items` cleanly auto-retried after a workspace reload (`staleReloadMs: 9417`) while `get_msbuild_properties`, `evaluate_msbuild_property`, and `get_editorconfig_options` in the same batch errored with either "Loaded projects (0)" or "Document not found in workspace". Four siblings, two behaviors. Move the auto-reload guard into the shared base class so every workspace-scoped lookup behaves identically.
- **proposed action:** refactor MSBuild/EditorConfig tool base to share the auto-reload handler; add contract test that issues a forced-stale batch and asserts all tools handle it
- **evidence:** `2a#get_msbuild_properties`, `2a#get_editorconfig_options`, `3#Pattern 1`

### Finding 3
- **id:** `find-implementations-filter-source-gen-partials`
- **priority hint:** **medium** — any implementation-graph consumer over-counts by 2-3× on source-gen-heavy projects
- **title:** Filter source-generated partial-class decls out of `find_implementations` by default
- **summary:** `find_implementations(ISnapshotStore)` returned 3 hits: `FileSnapshotStore` (real), and two `partial class FileSnapshotStore` entries from `Logging.g.cs` and `RegexGenerator.g.cs`. Those are the same type's additional decls, not distinct implementations. Deduplicate by `ISymbol` identity; if consumers need the partial list, add `includeGeneratedPartials=false` default-opt-out.
- **proposed action:** default-deduplicate by containing type; add opt-in flag for the noisy shape
- **evidence:** `2a#find_implementations`

### Finding 4
- **id:** `test-related-response-envelope-parity`
- **priority hint:** **medium** — schema-branching tax on any tool-chain combining both APIs
- **title:** Align `test_related` response shape with `test_related_files`
- **summary:** `test_related` returned a bare array `[{displayName, fullyQualifiedName, filePath, line}, …]`; `test_related_files` returned `{tests: [{…, projectName, triggeredByFiles}], dotnetTestFilter: "FullyQualifiedName~…|…"}`. Downstream consumers have to rebuild the filter string for `test_related`. Wrap `test_related` output in `{tests, dotnetTestFilter}` and add the projectName/triggeredByFiles columns.
- **proposed action:** adjust `test_related` response DTO + deprecate bare-array shape behind version flag
- **evidence:** `2a#test_related`, `3#Pattern 3`

### Finding 5
- **id:** `test-reference-map-filter-note-wording`
- **priority hint:** **low** — cosmetic but high-frequency (every productive-project filter triggers it)
- **title:** Fix misleading "No test projects detected" note in `test_reference_map` when filter narrows out test scope
- **summary:** `test_reference_map(projectName=FirewallAnalyzer.Application, limit=5)` returned `"notes": ["No test projects detected (IsTestProject=true). Coverage defaults to 0%."]` despite the workspace having 6 real test projects. The note is about filter scope, not about project existence. Change to `"No test projects matched the current filter"` or split into two notes: `"Productive-project filter in effect: test projects out of scope"` vs `"This workspace contains no test projects"`.
- **proposed action:** one-line wording change in the notes array + unit test
- **evidence:** `2a#test_reference_map`

## 5. Meta-note

This was a **mixed** session — 95% Roslyn MCP audit coverage with 2 real Phase-6 C# mutations — and friction was dominated by **contract / response-shape inconsistency** (§7 of the audit report logs 11 separate shape/drift items; this retro distills the top 5 as findings). Reliability was excellent: 0 crashes, 0 silent wrong results on stable tools, and every safety-refused mutation was refused for the right reason. Ergonomics have two systemic gaps: empty-result sentinel conventions (Pattern 3), and parity-across-sibling-tools (Pattern 1). Next time I'd probe `test_reference_map` with both a productive-project filter AND a test-project filter in the same turn so the "no test projects" note is classified immediately rather than surfacing as a stray finding.
