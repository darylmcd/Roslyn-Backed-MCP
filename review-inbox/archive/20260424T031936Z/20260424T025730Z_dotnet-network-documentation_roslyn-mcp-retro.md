---
generated_at: 2026-04-24T02:57:30Z
source_repo: dotnet-network-documentation
source_repo_path: C:\Code-Repo\DotNet-Network-Documentation
session_phase: mixed (Roslyn-MCP audit run — full-surface probe, preview-only Phase 6; zero code commits, one markdown report written)
---

# Roslyn MCP session retrospective — dotnet-network-documentation — 2026-04-24

## 1. Session classification

**Mixed — audit/exercise-surface-and-record.** The session ran `ai_docs/prompts/deep-review-and-refactor.md` in `mode=full` against the NetworkDocumentation.sln workspace. No C# source was mutated (Phase 6 was forced into preview-only because the MCP client's root allow-list blocked the prescribed disposable-worktree workflow). The two file-modifying tasks were both markdown writes — a draft audit report and a final canonical audit report. All substantive work was Roslyn-MCP tool exercise with per-call evidence capture.

## 2. Task inventory

| Task (one-line verb phrase) | Tool actually used | File type / domain | Right tool for the job? |
|---|---|---|---|
| Read the audit prompt in chunks | `Read` (offset/limit) | markdown | yes |
| Find solution entrypoint | `Glob` | filesystem | yes |
| Capture git HEAD/branch/status | `Bash` (git) | git | yes |
| Create disposable isolation worktree | `Bash` (git worktree add) | git | yes (but see 2a — worktree turned out to be unreachable via MCP root allow-list) |
| Restore packages in the audit worktree | `Bash` (dotnet restore, background) | dotnet | yes |
| Load workspace into Roslyn MCP | `mcp__roslyn__workspace_load` | C# solution | yes |
| Warm the workspace | `mcp__roslyn__workspace_warm` | C# solution | yes |
| List workspaces / check readiness | `workspace_list`, `server_heartbeat`, `workspace_status` | server-health | yes |
| Map project graph | `project_graph` | MSBuild graph | yes |
| Seed coverage ledger from live surface | **failed**: `ReadMcpResourceTool(roslyn://server/catalog)` → `Server "roslyn" is not connected`; substituted with `get_prompt_text(discover_capabilities)` | MCP resource | **workaround — resource channel client-blocked, see 2a** |
| Broad diagnostics scan (default + non-default paths) | `project_diagnostics` (summary, severity=Error, projectName+diagnosticId), `compile_check` (default + emitValidation), `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan(includeTransitive=true)`, `list_analyzers` (default + project+offset), `diagnostic_details` | C# diagnostics | yes |
| Code-quality metrics | `get_complexity_metrics`, `get_cohesion_metrics`, `get_coupling_metrics`, `find_unused_symbols` (x2), `find_duplicated_methods`, `find_duplicate_helpers`, `find_dead_locals`, `find_dead_fields(usageKind=never-read)`, `get_namespace_dependencies`, `get_nuget_dependencies`, `suggest_refactorings` | C# semantics | yes |
| Deep symbol analysis on SnapshotStore | `symbol_search`, `symbol_info`, `document_symbols`, `type_hierarchy`, `find_consumers`, `find_type_mutations`, `find_shared_members`, `find_type_usages`, `find_implementations` (negative), `callers_callees`, `symbol_relationships`, `symbol_signature_help`, `probe_position`, `symbol_impact_sweep`, `member_hierarchy`, `find_property_writes`, `find_references` (summary) | C# semantics | yes |
| Impact analysis | `impact_analysis(referencesLimit=5)` | C# semantics | **FAIL — 86 KB response busted the MCP output cap, see 2a** |
| Flow analysis on SnapshotStore.Save | `get_source_text`, `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree(maxTotalBytes=20000)`, `trace_exception_flow` | C# semantics | yes |
| Snippet & script validation | `analyze_snippet` (4 kinds + empty), `evaluate_csharp` (numeric, multi-line, runtime-error, empty) | C# semantics | yes |
| Phase 6 refactor previews | `fix_all_preview`, `rename_preview` (x2), `extract_interface_preview`, `format_check`, `organize_usings_preview`, `format_range_preview`, `code_fix_preview`, `get_code_actions` (x2), `remove_dead_code_preview`, `extract_method_preview`, `preview_multi_file_edit`, `restructure_preview`, `replace_string_literals_preview`, `change_signature_preview` (add/rename/reorder), `symbol_refactor_preview`, `preview_record_field_addition`, `extract_and_wire_interface_preview` | C# refactor | yes (writes gated by worktree-blocker) |
| Phase 6 apply wave | `workspace_changes` | session audit | **FAIL — MCP stdio host crashed, see 2a** |
| Post-crash recovery — workspace reload | `workspace_load` | server recovery | yes |
| EditorConfig / MSBuild inspection | `get_editorconfig_options`, `evaluate_msbuild_property`, `evaluate_msbuild_items`, `get_msbuild_properties(propertyNameFilter)` | MSBuild | yes |
| Build & test validation | `build_project`, `test_discover`, `test_related_files`, `test_related`, `test_run(1-test filter)`, `validate_workspace(summary)`, `validate_recent_git_changes(summary)`, `test_reference_map` | build/test | yes |
| Semantic search / DI / generated | `semantic_search` (x3), `find_reflection_usages`, `get_di_registrations(showLifetimeOverrides=true)`, `source_generated_documents` | C# discovery | yes |
| Navigation / completions | `go_to_definition`, `goto_type_definition`, `enclosing_symbol`, `get_completions`, `find_references_bulk`, `find_overrides`, `find_base_members` | C# navigation | yes |
| File / project / scaffolding previews | `move_type_to_file_preview`, `create_file_preview`, `delete_file_preview`, `split_class_preview`, `scaffold_type_preview`, `scaffold_test_preview`, `scaffold_test_batch_preview`, `scaffold_first_test_file_preview`, `add_package_reference_preview`, `set_project_property_preview` | C# refactor / project | yes |
| Prompt verification | `get_prompt_text` (4 positive, 2 negative, 1 retry) | MCP prompts | yes |
| Resource verification | `ListMcpResourcesTool`, `ReadMcpResourceTool` | MCP resources | **blocked — client not routing `roslyn://` reads, see 2a** |
| Negative probes (Phase 17) | `workspace_status(fabricated id)`, `find_references(fabricated handle)`, `symbol_search("")`, `analyze_snippet("")`, `evaluate_csharp("")` | negative tests | yes |
| Skills audit (Phase 16b) | `Bash` (ls, grep, find, head) | filesystem over skills/*/SKILL.md | yes |
| Draft audit report (per-phase) | `Write` (once) + `Edit` (multiple targeted appends) | markdown | yes |
| Final canonical audit report | `Write` | markdown | yes |
| Close workspace + clean up worktree | `mcp__roslyn__workspace_close`, `git worktree remove` | server lifecycle | yes |

## 2a. Roslyn MCP issues encountered

### 2a#server-stdio-host-crash — `workspace_changes` / auto-reload cascade (P0/P1)
- **Tool:** `mcp__roslyn__workspace_changes` (terminal call) + preceding `preview_record_field_addition` / `extract_and_wire_interface_preview` cascade
- **Inputs:** Phase 6k batch with `restructure_preview`, `change_signature_preview(rename)`, `change_signature_preview(reorder)`, `symbol_refactor_preview(no-op rename)`, `preview_record_field_addition(WebSnapshotStoreContext,IsDefault,bool,false)`, `extract_and_wire_interface_preview(SnapshotStore,NetworkDocumentation.Core)`, `workspace_changes(wsId)` in a single turn
- **Symptom:** The two record-field / wire-interface calls reported `"staleAction":"auto-reloaded","queuedMs":3089` and `4370` with the structured message *"Workspace was auto-reloaded during this call; … Re-resolve the symbol … and retry."* The immediately-following `workspace_changes` returned `MCP error -32000: Connection closed`. `server_heartbeat` afterwards showed **new** `stdioPid 40452 → 21500`, `serverStartedAt 02:25:06Z → 02:39:11Z` — the stdio host had been replaced.
- **Impact:** Killed the Phase 6 cascade. Every in-flight preview token (`338ef2c6…`, `cc7550d0…`, `553423f8…`, `6424bb61…`, `9ef819f2…`, `eae2286d…`, `d34a8ab8…`, `89db30b3…`) invalidated. Required full workspace reload; Phase 6m workspace_changes never produced a result. Lost ~5 min + forced writer reclassification to skipped-safety.
- **Workaround:** Reloaded workspace with a new id (`b97e5b69473a…`); abandoned remaining Phase 6k exotic writers; scored `preview_record_field_addition` / `extract_and_wire_interface_preview` as `needs-more-evidence`.
- **Repro confidence:** one-shot this run, but highly reproducible-looking: the failure mode is "auto-reload cascade → next concurrent write-capable call touches stale state → unhandled exception → stdio death."

### 2a#impact-analysis-paging-broken (P1)
- **Tool:** `mcp__roslyn__impact_analysis`
- **Inputs:** `metadataName="NetworkDocumentation.Core.Snapshots.SnapshotStore", referencesLimit=5`
- **Symptom:** *"Error: result (86,095 characters) exceeds maximum allowed tokens. Output has been saved to …tool-results\mcp-roslyn-impact_analysis-1776998028573.txt."* `referencesLimit=5` only paged the `references` array; the embedded declarations / affected-projects payload stayed uncapped.
- **Impact:** Could not retrieve a usable impact-analysis result for a 51-reference, 5-project symbol. Had to substitute `symbol_impact_sweep(summary=true,maxItemsPerCategory=15)` (which does expose `summary`).
- **Workaround:** Used `symbol_impact_sweep` + `find_references(summary=true)` + `find_consumers`.
- **Repro confidence:** deterministic for any symbol with enough declarations / downstream projects.

### 2a#fix-all-preview-ide0305-provider-throws (P1)
- **Tool:** `mcp__roslyn__fix_all_preview`
- **Inputs:** `diagnosticId="IDE0305", scope="project", projectName="NetworkDocumentation.Core"`
- **Symptom:** `fixedCount=0, changes=[]`, with `guidanceMessage`: *"The registered FixAll provider for 'IDE0305' threw while computing the fix (InvalidOperationException: Sequence contains no elements). Try code_fix_preview on individual occurrences, or narrow the scope (document / project) to isolate the failing occurrence."*
- **Impact:** Phase 6a could not preview the obvious high-volume IDE0305 cleanup (79 documented occurrences). The underlying Roslyn fixer is broken; the MCP wrapper handled it gracefully.
- **Workaround:** None needed for the audit (the wrapper's guidance is correct; this was a known-broken upstream).
- **Repro confidence:** deterministic for IDE0305 in this repo.

### 2a#resource-channel-client-blocked (P1, client-side)
- **Tool:** `ReadMcpResourceTool(server="roslyn", uri="roslyn://server/catalog")`, `ListMcpResourcesTool(server="roslyn")`
- **Inputs:** any `roslyn://` URI
- **Symptom:** `ReadMcpResourceTool` returned *"Server 'roslyn' is not connected"*; `ListMcpResourcesTool` returned `"No resources found. MCP servers may still provide tools even if they have no resources."` — but `mcp__roslyn__*` tool calls worked perfectly throughout. The client registered the server for tool invocation but not for resource reads.
- **Impact:** All 13 catalog resources (9 stable + 4 experimental) blocked for the entire run — Phase 15 became `N/A — blocked`. Coverage-ledger seeding had to fall back to `get_prompt_text(discover_capabilities)`, which is a tool channel.
- **Workaround:** Used `get_prompt_text(discover_capabilities)` as the authoritative catalog substitute.
- **Repro confidence:** deterministic in this Claude Code session.

### 2a#mcp-root-allowlist-blocks-disposable-worktree (P1, mixed)
- **Tool:** `mcp__roslyn__workspace_load`
- **Inputs:** `path="C:\Code-Repo\DotNet-Network-Documentation-audit-run\NetworkDocumentation.sln"` (a fresh worktree)
- **Symptom:** *"Parameter '<unknown>' is invalid: Path '…-audit-run\NetworkDocumentation.sln' is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\DotNet-Network-Documentation."*
- **Impact:** Forced a procedural change — the prompt's disposable-worktree precondition was unenforceable, so all Phase 6 / 9 / 10-apply / 12-apply / 13-apply siblings dropped to `skipped-safety`. Effectively converted `mode=full` to `mode=conservative`.
- **Workaround:** Loaded the main repo's .sln and ran Phase 6 preview-only. Recorded the downgrade up front in the report header.
- **Repro confidence:** deterministic — Claude Code's root allow-list mechanism doesn't know about ad-hoc `git worktree add` paths.

### 2a#change-signature-preview-add-unhelpful-error (P2)
- **Tool:** `mcp__roslyn__change_signature_preview`
- **Inputs:** `op="add", metadataName="…SnapshotStore.Save", name="cancellationToken", parameterType="System.Threading.CancellationToken", defaultValue="default"`
- **Symptom:** *"Parameter 'index' has an out-of-range value: Specified argument was out of the range of valid values. (Parameter 'index')."* — "index" is an internal index, not a schema field the caller supplied.
- **Impact:** Could not add a parameter preview; add-path scored `keep-experimental` rather than `promote`.
- **Workaround:** None — same call with `op="rename"` worked; `op="reorder"` produced an excellent actionable error.
- **Repro confidence:** one-shot this run.

### 2a#get-code-actions-default-range-inverted (P2)
- **Tool:** `mcp__roslyn__get_code_actions`
- **Inputs:** `filePath="…SnapshotStore.cs", startLine=98, startColumn=13` (no endLine/endColumn)
- **Symptom:** *"Parameter 'end' has an out-of-range value: 'end' must not be less than 'start'. start='3930' end='3927' (Parameter 'end')."* — default-end computation produces an end position 3 chars **before** the start.
- **Impact:** Caret-only calls fail; callers must supply explicit endLine/endColumn.
- **Workaround:** Retried with `endLine=108, endColumn=10` — returned `count=0` with a helpful `hint`.
- **Repro confidence:** deterministic.

### 2a#scaffold-first-test-file-preview-static-class (P2)
- **Tool:** `mcp__roslyn__scaffold_first_test_file_preview`
- **Inputs:** `serviceMetadataName="NetworkDocumentation.Core.Snapshots.SnapshotContentHasher"` (a `static class`)
- **Symptom:** Tool emitted `[Fact]` fixture with `_subject = new SnapshotContentHasher();` — won't compile against a static class. Warning *"Service 'SnapshotContentHasher' has no public/internal instance methods to scaffold smoke tests for — emitted a single placeholder test"* was present but the emitted body is still broken.
- **Impact:** Scored `keep-experimental` rather than `promote`.
- **Workaround:** None exercised — would require manual edit of scaffolded output.
- **Repro confidence:** deterministic for `static class` targets.

### 2a#test-reference-map-scope-contradiction (P2)
- **Tool:** `mcp__roslyn__test_reference_map`
- **Inputs:** `projectName="NetworkDocumentation.Core", limit=5`
- **Symptom:** `inspectedTestProjects: []`, `coveragePercent: 0`, `notes: ["No test projects detected (IsTestProject=true). Coverage defaults to 0%.", …]` — "no test projects detected" but qualifies with "IsTestProject=true" (the sole test project does exist).
- **Impact:** Misleading report — the productive-project filter rules out the sole test project; notes field reads as a bug.
- **Workaround:** Run without `projectName` (not re-tried this session due to budget).
- **Repro confidence:** deterministic with productive projectName.

### 2a#rename-preview-bare-metadata-vague (P3)
- **Tool:** `mcp__roslyn__rename_preview`
- **Inputs:** `metadataName="NetworkDocumentation.Core.Snapshots.SnapshotStore.Delete", newName="DeleteSnapshot"`
- **Symptom:** *"Invalid operation: No symbol found for the provided rename target."* — sibling call on `string` built-in produced an excellent actionable error; bare-member-name call produced a vague one.
- **Impact:** Caller has no hint that adding overload signature or symbolHandle would resolve.
- **Workaround:** Use symbolHandle from prior `symbol_search` call.
- **Repro confidence:** deterministic for any method name without a unique overload match.

### 2a#add-package-reference-preview-duplicate-not-detected (P2)
- **Tool:** `mcp__roslyn__add_package_reference_preview`
- **Inputs:** `projectName="NetworkDocumentation.Core", packageId="Meziantou.Analyzer", version="3.0.50"`
- **Symptom:** Emitted a second `<PackageReference Include="Meziantou.Analyzer" Version="3.0.50" />` in the diff despite the package already being transitively present via `Directory.Build.props` / `Directory.Packages.props`.
- **Impact:** Apply path would produce a duplicate reference.
- **Workaround:** None tested (preview-only).
- **Repro confidence:** deterministic in repos using Directory.Build.props / CPM.

### 2a#replace-string-literals-preview-zero-match (P3)
- **Tool:** `mcp__roslyn__replace_string_literals_preview`
- **Inputs:** `literalValue="UNLIKELY_LITERAL_XYZ_123", replacementExpression="Constants.Placeholder"`
- **Symptom:** *"Invalid operation: replace_string_literals_preview: no matching literals found in scope."* — throws rather than returning `{count:0, changes:[]}`.
- **Impact:** Callers must wrap in try/catch just to detect an empty match set, which is inconsistent with every other preview tool.
- **Workaround:** Treat InvalidOperation as a 0-match signal.
- **Repro confidence:** deterministic.

### 2a#find-base-members-vs-member-hierarchy-drift (P2)
- **Tool:** `mcp__roslyn__find_base_members` / `mcp__roslyn__find_overrides`
- **Inputs:** `metadataName="NetworkDocumentation.Web.WebSnapshotStoreContext.Equals"` / `"System.IEquatable\`1.Equals"`
- **Symptom:** Both returned `count=0, items=[]`. On the same symbols, `mcp__roslyn__member_hierarchy` correctly returned `baseMembers=[{"fullyQualifiedName":"System.IEquatable<NetworkDocumentation.Web.WebSnapshotStoreContext>.Equals(NetworkDocumentation.Web.WebSnapshotStoreContext?)",…}]`.
- **Impact:** Siblings disagree — an agent chaining `find_base_members → rename_preview` gets different results than one chaining `member_hierarchy → rename_preview`.
- **Workaround:** Prefer `member_hierarchy` for metadata-boundary cases.
- **Repro confidence:** deterministic for any symbol whose base/interface resides in an external assembly.

### 2a#find-unused-symbols-xunit-collection-fp (P3)
- **Tool:** `mcp__roslyn__find_unused_symbols` with `includePublic=true` (default `excludeConventionInvoked=true`)
- **Inputs:** default filters on `NetworkDocumentation.Tests`
- **Symptom:** `WebHostSerialCollection` (xUnit `[CollectionDefinition]` attribute holder) returned as unused despite the convention-invoked filter supposedly catching xUnit fixture shapes.
- **Impact:** False positive in the dead-symbol list.
- **Workaround:** None — just tag as a known FP.
- **Repro confidence:** deterministic for any `[CollectionDefinition]` holder.

### 2a#test-related-response-shape-bare-array (P3)
- **Tool:** `mcp__roslyn__test_related`
- **Inputs:** `metadataName="NetworkDocumentation.Core.Snapshots.SnapshotStore.Save"`
- **Symptom:** Returned a **bare JSON array** of ~100+ entries (heuristic name-overlap dominated), rather than the `{testProjects:[…], returnedCount, totalCount, hasMore}` shape used by `test_discover`. No pagination metadata.
- **Impact:** Clients can't page and can't distinguish exact-match from token-overlap matches.
- **Workaround:** Post-process the array manually.
- **Repro confidence:** deterministic for any common-name symbol.

### 2a#diagnostic-details-supported-fixes-empty (P3)
- **Tool:** `mcp__roslyn__diagnostic_details`
- **Inputs:** `diagnosticId="MA0001", filePath="…QueryLexer.cs", line=62, column=21`
- **Symptom:** `supportedFixes: []`. Same diagnostic subsequently produced *"No code fix provider is loaded for diagnostic 'MA0001'"* from `code_fix_preview`. The two tools agree on behavior but `diagnostic_details` gives no hint to the caller that `code_fix_preview` will also fail.
- **Impact:** An agent reading only `diagnostic_details` proceeds to `code_fix_preview` expecting a fix, then fails.
- **Workaround:** Treat empty `supportedFixes` as "probably no fix available; expect precondition error."
- **Repro confidence:** deterministic for any analyzer diagnostic whose fix-provider isn't resolved (e.g. Meziantou fixes in analyzer-only NuGet package).

### 2a#set-project-property-preview-formatting (P3)
- **Tool:** `mcp__roslyn__set_project_property_preview`
- **Inputs:** `projectName="NetworkDocumentation.Core", propertyName="Nullable", value="enable"`
- **Symptom:** Inserted `<PropertyGroup><Nullable>enable</Nullable></PropertyGroup>` as a single-line element, not properly indented relative to existing project style. Also: the property was already evaluated-active via `Directory.Build.props` — no detection of the no-op.
- **Impact:** Cosmetic drift on apply; redundant property added.
- **Workaround:** Skip the preview if `evaluate_msbuild_property` shows the desired value is already active.
- **Repro confidence:** deterministic.

### 2a#split-class-preview-orphan-indent (P3)
- **Tool:** `mcp__roslyn__split_class_preview`
- **Inputs:** split `FindByHash`, `FindByHashAndInputSignature`, `FindInSnapshots` out of SnapshotStore.cs
- **Symptom:** Source-file diff shows orphan-indentation blank lines where the removed members used to be (e.g. `+    ` on otherwise-empty lines). Cosmetic.
- **Impact:** Post-apply file has some stray whitespace.
- **Workaround:** Run `format_document_preview` after apply.
- **Repro confidence:** deterministic.

### 2a#find-property-writes-positional-record-silent-zero (P3)
- **Tool:** `mcp__roslyn__find_property_writes`
- **Inputs:** `metadataName="NetworkDocumentation.Web.WebSnapshotStoreContext.Store"` (a positional-record property)
- **Symptom:** `count: 0, resolvedSymbolKind: "Property", writes: []` — silent. No bucket / warning for "positional-record primary-constructor binding" even though every consumer constructs the record with the property supplied.
- **Impact:** Caller thinks the property is never written; misleading.
- **Workaround:** Cross-check with `find_consumers` or `find_type_usages(ObjectCreation)`.
- **Repro confidence:** deterministic for positional-record properties.

### 2a#symbol-search-empty-query-degenerate (P3)
- **Tool:** `mcp__roslyn__symbol_search`
- **Inputs:** `query=""`
- **Symptom:** Returned 5 arbitrary matches (`CanonicalValues`, `InterfaceTypeMap`, `InterfaceDisplayMap`, `InterfaceStatusMap`, `AdminStatusMap`) from first-N-alphabetical. Tool accepts the empty string as substring-match against everything.
- **Impact:** Minor; a caller probing with empty input gets noise instead of an error.
- **Workaround:** Reject empty queries client-side.
- **Repro confidence:** deterministic.

### 2a#scaffold-test-preview-sibling-inference-overbroad (P3)
- **Tool:** `mcp__roslyn__scaffold_test_preview`
- **Inputs:** `testProjectName="NetworkDocumentation.Tests", targetTypeName="SnapshotStore", targetMethodName="Resolve"`
- **Symptom:** Scaffolded `SnapshotStoreGeneratedTests.cs` copied `[Trait("Category","Playwright")]` and a dozen unused usings (`Microsoft.Playwright`, `System.Net.Sockets`, `System.Net.Http.Json`, …) from a sibling `*Tests.cs` fixture that happened to be the most-recently-modified in the test project.
- **Impact:** Generated output has stale category tagging and noisy imports.
- **Workaround:** Pass `referenceTestFile=""` to opt out of inference.
- **Repro confidence:** deterministic when MRU sibling is a Playwright fixture.

### 2a#coupling-metrics-tool-backlog-stale (doc/P3)
- **Tool:** `mcp__roslyn__get_coupling_metrics`
- **Inputs:** default
- **Symptom:** Returned clean results (15 types / Martin instability index). The prompt's body documents `coupling-metrics-tool` as an open backlog row ("returns 'No such tool'"); the tool is **live and healthy** in v1.29.0.
- **Impact:** Stale backlog entry — doc drift.
- **Workaround:** N/A.
- **Repro confidence:** deterministic.

## 2b. Missing tool gaps

### 2b#disposable-workspace-sandbox
- **Task:** Load a throwaway workspace so Phase 6 applies are reversible without touching the user's working tree.
- **Why Roslyn-shaped:** The entire audit prompt is predicated on disposable-worktree isolation; the current workflow requires an external `git worktree add`, but the MCP client's root allow-list then blocks the worktree path. This is a semantic request ("give me a sandbox workspace at the same revision"), not a repo-shape accident.
- **Proposed tool shape:**
  ```
  workspace_fork(source_workspace_id | path, into_temp=true)
    → { workspaceId, sandboxPath, cleanupToken }
  workspace_fork_cleanup(cleanupToken) → { removed: bool, orphanedFiles?: string[] }
  ```
  Where `sandboxPath` is created in the server-process scratch dir (or a negotiated allow-listed path) rather than requiring the client to pre-sanction it.
- **Closest existing tool:** `workspace_load` plus manual `git worktree add`. Closeness: low — it only works when the client's root list happens to include the worktree parent.

### 2b#impact-analysis-summary-mode
- **Task:** Fetch an impact-analysis summary for a wide-radius symbol.
- **Why Roslyn-shaped:** The tool is explicitly about "change impact", which is by definition broad for widely-consumed types. A `summary=true` or `maxItemsPerCategory` parameter — the exact shape already present on `find_references`, `symbol_impact_sweep`, `validate_workspace`, `rename_preview` — is a natural fit.
- **Proposed tool shape:** Add `summary: boolean = false` and `declarationsLimit: integer = 100` to `impact_analysis`. In summary mode, drop `references[].previewText` and collapse `affectedProjects[].declarations[]` to per-project counts.
- **Closest existing tool:** `symbol_impact_sweep(summary=true,maxItemsPerCategory=N)` — worked perfectly on the same symbol; the gap is why the same contract isn't on `impact_analysis`.

### 2b#restore-project-preflight
- **Task:** Confirm NuGet restore state for the currently-loaded workspace without shelling to `dotnet restore`.
- **Why Roslyn-shaped:** The audit prompt Phase 0 already couples `workspace_load(autoRestore=true)` with a separate `dotnet restore` shell call. The server has enough information (it already reports `restoreRequired`) to expose a direct `restore_workspace` tool that doesn't require a shell.
- **Proposed tool shape:**
  ```
  restore_workspace(workspaceId, verbosity="quiet"|"normal")
    → { success, restoredProjects, durationMs, stdOutTail, stdErrTail }
  ```
- **Closest existing tool:** `workspace_load(autoRestore=true)` only restores on load; no standalone restore. `Bash: dotnet restore` works but requires shell access.

### 2b#semantic-search-debug-fallback-signal
- **Task:** Distinguish true structured-semantic matches from name-token-fallback matches in `semantic_search` output.
- **Why Roslyn-shaped:** The tool's own description says: *"Verbose-query fallback: long natural-language queries that fail structured parsing decompose into stopword-filtered tokens and match any symbol name containing a token; the response Debug payload shows the parsed tokens, applied predicates, and fallback strategy."* But the Debug payload is absent from the default response shape. On "classes implementing IDisposable" the tool returned 10 hits where 2 are clearly name-token fallback matches (test-helper `TempDir` classes) — no flag.
- **Proposed tool shape:** Always surface `Debug.fallbackStrategy: "structured"|"name-substring"|"token-or-match"|"none"` and `Debug.matchKind: "structured"|"name-overlap"` per-result.
- **Closest existing tool:** `semantic_search` itself — the fallback data is computed but not returned.

### 2b#static-class-test-scaffold
- **Task:** Scaffold a first test file for a `static class` (no instance methods, no constructor).
- **Why Roslyn-shaped:** `scaffold_first_test_file_preview` already inspects the service's methods; it just needs a static-class branch that emits a static-friendly test body (no field, no `new T()`).
- **Proposed tool shape:** Inside `scaffold_first_test_file_preview`, detect `serviceTypeSymbol.IsStatic` and emit per-public-static-method smoke tests directly (as `TargetType.StaticMethod(…)` calls).
- **Closest existing tool:** `scaffold_first_test_file_preview` — emits a broken body as described in 2a#scaffold-first-test-file-preview-static-class.

### 2b#evaluated-package-duplicate-guard
- **Task:** Add a PackageReference while respecting Directory.Build.props / Directory.Packages.props.
- **Why Roslyn-shaped:** The MCP server already has MSBuild evaluation — `get_nuget_dependencies` and `evaluate_msbuild_items("PackageReference")` both return the **evaluated** graph including Directory.Build.props contributions. `add_package_reference_preview` should consult that graph before writing.
- **Proposed tool shape:** Inside `add_package_reference_preview`, check evaluated PackageReference graph; if the ref is already present (direct or via Directory.Build.props), return a `status: "already-present"` response (no diff) or refuse with actionable guidance.
- **Closest existing tool:** `add_package_reference_preview` — currently works at csproj-text level only.

## 3. Recurring friction patterns

### Pattern 1 — Stale-reload cascade converges on stdio death
- **What happened:** The Phase 6k batch bundled one `restructure_preview` + `symbol_refactor_preview` + `preview_record_field_addition` + `extract_and_wire_interface_preview` + `workspace_changes` in a single message. Mid-batch, **two** tools returned `staleAction=auto-reloaded` (`queuedMs` 3089 and 4370), and `workspace_changes` terminated with `MCP error -32000: Connection closed`. Server stdioPid changed `40452 → 21500`.
- **Why it recurs:** Any session that fans out experimental writers after earlier mutations queues multiple preview tokens against a snapshot that can be auto-reloaded out from under them. The single-connection stdio host has no guard that transitions stale calls to structured errors **before** serializing state-read into an unhandled exception.
- **What would fix it:** Fail fast — if `workspace_changes` (or any state-reader) is invoked while the workspace is in the middle of an auto-reload transition, return a structured `category="StaleWorkspaceTransition"` error with a retry hint, instead of raising the exception that killed the host.

### Pattern 2 — Paging parameters cap the wrong field
- **What happened:** `impact_analysis(referencesLimit=5)` returned 86 KB because the parameter capped only `references[]` and not `declarations[]` or `affectedProjects[]`. Same class of issue as why `diagnostic_details.supportedFixes=[]` carries no hint about missing fix providers and `test_related` returns an un-paged bare array.
- **Why it recurs:** Any audit that touches wide-radius symbols hits the 25k-token MCP cap on tools that only cap one top-level list. Happened for `impact_analysis` on SnapshotStore (51 refs, 5 projects) and implicitly on `test_related` (100+ heuristic matches).
- **What would fix it:** Normalize a `summary: bool = false` parameter across all read tools that can emit >10 KB responses; collapse per-item text fields in summary mode. `find_references` and `symbol_impact_sweep` already do this correctly — make it a server-wide contract.

### Pattern 3 — Metadata-boundary silently degrades sibling tools
- **What happened:** `find_overrides(System.IEquatable\`1.Equals)` → 0 items. `find_base_members(WebSnapshotStoreContext.Equals)` → 0 items. `member_hierarchy(WebSnapshotStoreContext.Equals)` → correctly resolves IEquatable<T>.Equals as base. Same class of drift explains why `find_property_writes` silently returns 0 for positional-record primary-constructor bindings.
- **Why it recurs:** Every session that crosses into metadata-defined bases (records deriving from IEquatable, services implementing framework interfaces, etc.) hits this.
- **What would fix it:** Pick the most-inclusive sibling (currently `member_hierarchy`) and align others with it, OR emit a `Warning: metadata-boundary` field when the resolver consciously stopped at an external-assembly symbol — so callers at least know to cross-check.

### Pattern 4 — Scaffolding/template tools under-specialize by symbol shape
- **What happened:** `scaffold_first_test_file_preview` for a `static class` emitted `_subject = new StaticClass();` (won't compile). `scaffold_test_preview` over-copied `[Trait("Category","Playwright")]` and 10 unused usings from the MRU sibling fixture. `scaffold_type_preview` defaults to `internal sealed class` correctly — but the other two tools don't have comparable shape-detection.
- **Why it recurs:** Every session that scaffolds tests for a mixed-shape codebase (services, static utilities, records, Playwright fixtures) hits one of these.
- **What would fix it:** Dispatch on symbol shape: static class → static tests; regular class → instance tests; record → init-syntax tests. Narrow sibling-inference to safe cross-file attributes (file-scoped namespace, base class name) and exclude category / trait / test-framework attributes.

### Pattern 5 — Preview tools accept inputs that apply tools will then refuse
- **What happened:** `remove_dead_code_preview` on `AppConfig._instance` was refused: *"Symbol '_instance' still has references and cannot be removed safely."* — but `find_dead_fields(usageKind=never-read)` had **just** flagged it as never-read (because writes don't count toward "read"). The dead-field tool advertises the symbol as dead; the removal tool refuses because writes still exist.
- **Why it recurs:** Every session that chains `find_dead_fields → remove_dead_code_preview` hits the contract mismatch on any write-only singleton / sentinel field.
- **What would fix it:** Either (a) align the safety models (both should mean the same thing by "dead") or (b) emit `dead_fields[].removalBlockedBy: ["write-at-line-12"]` so the caller knows in advance that the removal tool will refuse.

## 4. Suggested findings

### Finding 1 — `stdio-host-crashes-on-autoreload-cascade`
- **priority:** high — the server process died mid-session; every in-flight preview token was invalidated.
- **title:** Guard state-readers against stale-workspace transitions to prevent stdio host death
- **summary:** During the Phase 6k batch, `preview_record_field_addition` and `extract_and_wire_interface_preview` both returned `staleAction=auto-reloaded`; the follow-up `workspace_changes` terminated with `MCP error -32000: Connection closed` and `server_heartbeat` showed a new `stdioPid 40452 → 21500`. The server's fallback path for post-auto-reload state reads raises an unhandled exception rather than emitting a structured `StaleWorkspaceTransition` error.
- **proposed action:** Wrap post-auto-reload state readers in a transition check that returns a structured retry-able error. Add a crash-observability log entry with correlationId + previous workspaceVersion so a replay is possible.
- **evidence:** 2a#server-stdio-host-crash, 3#Pattern1

### Finding 2 — `impact-analysis-paging-contract`
- **priority:** high — the tool is unusable on wide-radius symbols without a workaround.
- **title:** Add `summary=true` + `declarationsLimit` to `impact_analysis` to match sibling-tool paging contract
- **summary:** `impact_analysis(referencesLimit=5)` returned 86 095 chars and spilled to the tool-results file because only the `references[]` list is paged. `find_references` / `symbol_impact_sweep` / `rename_preview` all expose `summary=true`; `impact_analysis` should too. Workaround required a separate `symbol_impact_sweep(summary=true)` call.
- **proposed action:** Add `summary: bool = false` and `declarationsLimit: integer = 100` parameters; in summary mode, drop per-ref preview text and collapse declarations to per-project counts.
- **evidence:** 2a#impact-analysis-paging-broken, 2b#impact-analysis-summary-mode, 3#Pattern2

### Finding 3 — `disposable-workspace-sandbox`
- **priority:** high — procedurally blocks `mode=full` audits on any Claude Code workspace.
- **title:** Add `workspace_fork` to create an in-server disposable sandbox
- **summary:** The audit prompt's disposable-worktree precondition is unenforceable when the MCP client's root allow-list contains only the primary repo path. `workspace_load` on `C:\…-audit-run\NetworkDocumentation.sln` was rejected with *"Path 'C:\…-audit-run\…' is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\DotNet-Network-Documentation."* Forced Phase 6 into preview-only and effectively downgraded `mode=full → conservative`.
- **proposed action:** Introduce `workspace_fork` that creates a server-side sandbox at a scratch path (or a path the server negotiates into the client's allow-list). Pair with `workspace_fork_cleanup(cleanupToken)`.
- **evidence:** 2a#mcp-root-allowlist-blocks-disposable-worktree, 2b#disposable-workspace-sandbox

### Finding 4 — `dead-field-vs-remove-dead-code-safety-contract`
- **priority:** medium — affects every chained dead-code workflow.
- **title:** Align `find_dead_fields` "never-read" with `remove_dead_code_preview` safety model
- **summary:** `find_dead_fields(usageKind=never-read)` flagged `AppConfig._instance` (write-only singleton). Immediately chained `remove_dead_code_preview` refused: *"Symbol '_instance' still has references and cannot be removed safely."* The two tools use incompatible safety models.
- **proposed action:** Either (a) align: `find_dead_fields` should treat writes as blocking for never-read removals, or (b) emit `dead_fields[].removalBlockedBy` so callers know the removal tool will refuse before they try.
- **evidence:** 2a (covered in the chained flow during Phase 2/6), 3#Pattern5

### Finding 5 — `scaffold-symbol-shape-awareness`
- **priority:** medium — produces broken output for a common shape.
- **title:** Make `scaffold_first_test_file_preview` static-class aware; narrow `scaffold_test_preview` sibling-inference
- **summary:** `scaffold_first_test_file_preview(SnapshotContentHasher)` (a static class) emitted `_subject = new SnapshotContentHasher();` despite warning *"Service 'SnapshotContentHasher' has no public/internal instance methods…"*. Separately, `scaffold_test_preview(SnapshotStore.Resolve)` copied `[Trait("Category","Playwright")]` and 10 unused usings from an MRU sibling Playwright fixture.
- **proposed action:** Detect `typeSymbol.IsStatic` and switch to a static-test template (no field, no `new T()`, one `[Fact]` per public static method). Narrow sibling-inference to file-scoped namespace + base class; exclude `[Trait]`/`[Category]`-style attributes.
- **evidence:** 2a#scaffold-first-test-file-preview-static-class, 2a#scaffold-test-preview-sibling-inference-overbroad, 2b#static-class-test-scaffold, 3#Pattern4

## 5. Meta-note

This was a full-surface MCP audit session — 88-ish Roslyn MCP tool calls across 17 phases, zero code commits. Friction is concentrated in **reliability** (one stdio crash, several paging-contract misses) and **ergonomics** (duplicate-detection gaps, scaffolding shape-awareness, sibling-contract drift); coverage itself is strong — 161 tools and ~20 prompts is a lot of surface. Next time I'd (a) run `workspace_warm` immediately after every `workspace_load` (even post-crash reloads), (b) default `summary=true` on every read tool that exposes it to insure against the 25 KB cap, and (c) batch writer previews into **separate** turns from state-readers so stale-reload cascades don't converge on a single `workspace_changes` call.
