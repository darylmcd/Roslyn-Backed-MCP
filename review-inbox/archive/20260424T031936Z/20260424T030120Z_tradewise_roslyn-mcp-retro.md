---
generated_at: 2026-04-24T03:01:20Z
source_repo: tradewise
source_repo_path: C:\Code-Repo\TradeWise
session_phase: refactoring
---

# Roslyn MCP session retrospective — tradewise — 2026-04-24

## 1. Session classification

**refactoring** (dominant, heavy C#/semantic workload). The session ran `ai_docs/prompts/deep-review-and-refactor.md` in `full` mode against `TradeWise.sln` — an 18-phase Roslyn MCP audit that deliberately exercises the full surface, lands Phase 6 code refactors in git, and produces a coverage/promotion scorecard. Every major Roslyn MCP tool category (symbols, analysis, refactoring, validation, file-ops, project-mutation, scaffolding, prompts, resources) was driven as part of the protocol, so the lens is squarely on Roslyn MCP reliability, schema contracts, and ergonomics — not on markdown editing or git plumbing.

## 2. Task inventory

| Task (one-line verb phrase) | Tool actually used | File type / domain | Right tool for the job? |
|-----------------------------|---------------------|--------------------|-------------------------|
| Probe server readiness and catalog version | `server_info`, `server_heartbeat` | MCP meta | ✅ Roslyn MCP (correct) |
| Create disposable isolation (tried worktree, fell back to branch) | `git worktree add` / `git worktree remove` / `git checkout -b` | git | ✅ git (outside Roslyn scope) |
| Run package restore on 10-project solution | `dotnet restore` | .NET CLI | ✅ `dotnet` (outside Roslyn scope) |
| Load solution + warm compilation caches | `workspace_load`, `workspace_warm` | Roslyn workspace | ✅ Roslyn MCP |
| Confirm session + project graph | `workspace_list`, `workspace_status`, `workspace_health`, `project_graph` | Roslyn workspace | ✅ Roslyn MCP |
| Read catalog / resource templates | `ReadMcpResourceTool` → `roslyn://server/catalog`, `/resource-templates` | MCP resources | ✅ MCP resources |
| Broad diagnostics scan (0 errors, 70 Info) | `compile_check`, `project_diagnostics` (summary, severity, id filter) | Analysis | ✅ Roslyn MCP |
| Security findings + CVE scan | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` | Security | ✅ Roslyn MCP |
| Enumerate analyzers + rules | `list_analyzers` (paged, project filter) | Analysis | ✅ Roslyn MCP |
| Pull diagnostic detail for a specific SYSLIB1045 site | `diagnostic_details` | Analysis | ✅ Roslyn MCP |
| Complexity / cohesion / unused / coupling / duplicate / suggest metrics | `get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols`, `get_coupling_metrics`, `find_duplicated_methods`, `find_duplicate_helpers`, `find_dead_fields`, `find_dead_locals`, `suggest_refactorings` | Adv-analysis | ✅ Roslyn MCP |
| Namespace graph + NuGet deps | `get_namespace_dependencies`, `get_nuget_dependencies(summary=true)` | Adv-analysis | ✅ Roslyn MCP |
| Deep symbol analysis on 3 key types | `symbol_search`, `symbol_info`, `type_hierarchy`, `find_implementations`, `find_references` (+bulk), `find_consumers`, `find_shared_members`, `find_type_mutations`, `find_type_usages`, `callers_callees`, `symbol_relationships`, `symbol_signature_help`, `impact_analysis`, `member_hierarchy`, `document_symbols`, `enclosing_symbol`, `find_property_writes`, `find_overrides`, `find_base_members`, `probe_position` | Symbols | ✅ Roslyn MCP |
| Flow analysis on RunAsync (lines 86-140) | `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree` | Flow analysis | ✅ Roslyn MCP |
| Snippet + script validation | `analyze_snippet` (4 kinds), `evaluate_csharp` (4 scenarios) | Validation | ✅ Roslyn MCP |
| Apply dead-code removal (`BuildCompleteCommandTextForTest`) | `remove_dead_code_preview` → `_apply` | Refactoring | ✅ Roslyn MCP |
| Attempt solution-wide fix-all for IDE0300 (26 sites) | `fix_all_preview` (scope=solution + scope=document) | Refactoring | ✅ Roslyn MCP — **but failed** (see 2a) |
| Fallback: single-site IDE0300 code fix | `code_fix_preview` → `_apply` | Refactoring | ✅ Roslyn MCP |
| Rename round-trip `IngestionRunRepository` ↔ `IngestionRunRepository2` (15 callsites across 4 files) | `rename_preview` → `_apply` ×2 | Refactoring | ✅ Roslyn MCP |
| Format document (IngestionRunRepository.cs) | `format_document_preview` → `_apply` | Refactoring | ✅ Roslyn MCP |
| Organize usings on same file | `organize_usings_preview` | Refactoring | ❌ **FAILED — document not found** (see 2a) |
| Direct text edit (+ verify=true) | `apply_text_edit` | Editing | ✅ Roslyn MCP |
| Multi-file text edit (+ verify=true) | `apply_multi_file_edit` | Editing | ✅ Roslyn MCP |
| Pragma suppression for IDE0079 | `add_pragma_suppression` | Editing | ✅ Roslyn MCP |
| .editorconfig severity write (IDE0028 → silent) | `set_diagnostic_severity` / `set_editorconfig_option` | Configuration | ✅ Roslyn MCP |
| Range-format preview then apply | `format_range_preview` → `format_range_apply` (**apply blocked by token expiry**) | Refactoring | ✅ Roslyn MCP (apply path gap — see 2a) |
| Preview file/cross-project operations | `move_type_to_file_preview` (FAIL for enum), `move_file_preview`, `create_file_preview`, `delete_file_preview`, `extract_interface_cross_project_preview`, `dependency_inversion_preview`, `move_type_to_project_preview` (correct circular-ref rejection), `extract_and_wire_interface_preview`, `split_class_preview`, `migrate_package_preview` | File/cross-proj/orchestration | ✅ Roslyn MCP (FLAG-12 on enum-type handling) |
| Preview scaffolding new type | `scaffold_type_preview` | Scaffolding | ✅ Roslyn MCP |
| Preview test scaffold (TenantConstants) | `scaffold_test_preview` | Scaffolding | ❌ **FAILED — invalid C# generated** (see 2a) |
| Preview project-file mutations (8 variants) | `add_package_reference_preview`, `remove_package_reference_preview`, `add_project_reference_preview`, `set_project_property_preview`, `set_conditional_property_preview` (correct rejection), `add_target_framework_preview`, `add_central_package_version_preview`, `remove_central_package_version_preview` | Project-mutation | ✅ Roslyn MCP (cosmetic XML-formatting FLAGs) |
| Semantic search queries | `semantic_search` (×2) | Adv-analysis | ✅ Roslyn MCP |
| Find reflection usages | `find_reflection_usages` | Adv-analysis | ✅ Roslyn MCP |
| DI audit (13 regs, 2 override chains) | `get_di_registrations(showLifetimeOverrides=true)` | Adv-analysis | ✅ Roslyn MCP |
| List source-generator outputs | `source_generated_documents` | Workspace | ✅ Roslyn MCP |
| Symbol impact sweep for `IngestionTerminalStatus` | `symbol_impact_sweep` | Adv-analysis | ✅ Roslyn MCP (switch-exhaustiveness detection suspect) |
| EditorConfig + MSBuild inspection | `get_editorconfig_options`, `get_msbuild_properties`, `evaluate_msbuild_property`, `evaluate_msbuild_items` | Configuration / MSBuild | ✅ Roslyn MCP |
| Format verification | `format_check` | Refactoring | ✅ Roslyn MCP |
| Build individual project | `build_project(TradeWise.Domain)` | Validation | ✅ Roslyn MCP |
| Discover + run + correlate tests | `test_discover`, `test_related`, `test_related_files`, `test_run` | Validation | ✅ Roslyn MCP |
| Aggregate validation envelope | `validate_workspace(summary=true)` | Validation | ✅ Roslyn MCP |
| Git-derived validation (changed files) | `validate_recent_git_changes` | Validation | ❌ **FAILED — silent error** (see 2a) |
| Navigation (go-to-def, type-def, completions, enclosing) | `go_to_definition`, `goto_type_definition`, `get_completions`, `enclosing_symbol` | Navigation | ✅ Roslyn MCP |
| Read workspace-scoped resources | `ReadMcpResourceTool` → `roslyn://workspaces`, `/status`, `/projects`, `/diagnostics`, `/status/verbose`, `/workspaces/verbose` | MCP resources | ✅ MCP resources |
| Render 4 prompts via catalog | `get_prompt_text` (`explain_error`, `discover_capabilities`, `suggest_refactoring`, `refactor_loop`, `session_undo`) | Prompts | ✅ Roslyn MCP (3 failed first call on missing param — schema-learning cost) |
| Boundary / negative probes (11 scenarios) | `workspace_status`, `find_references`, `rename_preview`, `go_to_definition`, `enclosing_symbol`, `analyze_data_flow`, `symbol_search`, `analyze_snippet`, `evaluate_csharp`, `code_fix_apply`, `revert_last_apply` with invalid inputs | Multiple | ✅ Roslyn MCP (all returned structured error envelopes) |
| Phase 9 undo verification (audit-only apply + revert) | `format_document_preview` → `_apply` → `revert_last_apply` | Editing | ✅ Roslyn MCP |
| Explicit workspace reload | `workspace_reload` | Workspace | ✅ Roslyn MCP |
| Session change log enumeration | `workspace_changes` | Workspace | ✅ Roslyn MCP |
| Write incremental draft + final audit report | `Write`, `Edit` | Markdown | ✅ Edit/Write (outside Roslyn scope) |
| Check git state at end | `git status`, `git diff --stat main` | git | ✅ git (outside Roslyn scope) |

## 2a. Roslyn MCP issues encountered

### Issue 1 — `fix_all_preview` crashes for IDE0300 at every scope

- **Tool:** `fix_all_preview` (experimental, `refactoring` category)
- **Inputs tried:**
  - `diagnosticId=IDE0300, scope=solution`
  - `diagnosticId=IDE0300, scope=document, filePath=C:\Code-Repo\TradeWise\src\TradeWise.Infrastructure\Persistence\TenantScopedRepository.cs`
- **Symptom:** Both calls returned with `previewToken: ""`, `fixedCount: 0`, `changes: []`, and the verbatim guidance:
  > "The registered FixAll provider for 'IDE0300' threw while computing the fix (InvalidOperationException: Sequence contains no elements). Try code_fix_preview on individual occurrences, or narrow the scope (document / project) to isolate the failing occurrence."
- **Impact:** The Phase 6 "6a Fix All Diagnostics" sub-phase was blocked. 26 IDE0300 occurrences could have been cleaned up in a single preview→apply; instead the audit collapsed to a single-site `code_fix_preview` on `SchemaIntrospectionTests.cs:67`, leaving 25 occurrences unfixed. Roughly 3-4 min of budget went to scope-narrowing retries before giving up.
- **Workaround:** Per-occurrence `code_fix_preview` → `_apply` on one representative site; `code_fix_preview` worked cleanly, confirming the underlying fix is fine — only the FixAll wrapper is broken.
- **Repro confidence:** **deterministic** (2 of 2 scopes failed identically).

### Issue 2 — `scaffold_test_preview` generates syntactically invalid C#

- **Tool:** `scaffold_test_preview` (catalog says `stable`, v1.16.0)
- **Inputs:** `testProjectName=TradeWise.Domain.Tests, targetTypeName=TradeWise.Domain.Tenancy.TenantConstants`
- **Symptom:** The generated preview diff contains:
  > ```csharp
  > public class TradeWise.Domain.Tenancy.TenantConstantsGeneratedTests
  > {
  >     [Fact]
  >     public void Generated_Test()
  >     {
  >         var subject = new TradeWise.Domain.Tenancy.TenantConstants();
  >         …
  >     }
  > }
  > ```
  Two blockers: (a) a C# class identifier cannot contain `.` — this file would not parse; (b) `TenantConstants` is a `public static class`, so `new TenantConstants()` is an illegal constructor call. Also emits a stray `using TradeWise.Domain.Ingestion;` unrelated to the target (likely leftover from the sibling-test-file inference).
- **Impact:** Any `scaffold_test_apply` on this preview token would fail the post-apply compile_check. Blocks the "Coverage Analysis" workflow hint (`test_discover → test_coverage → scaffold_test_preview → scaffold_test_apply`) whenever the target's display name is fully-qualified or the target is a static type. Caught at preview-time, so no real damage — but wastes the audit's scaffold path.
- **Workaround:** Skipped `scaffold_test_apply`; marked the tool scorecard as **deprecate-candidate / keep-experimental-with-fix-filed** in the final report.
- **Repro confidence:** **deterministic** for this target shape.

### Issue 3 — `organize_usings_preview` persistent "Document not found" on a specific file

- **Tool:** `organize_usings_preview` (stable)
- **Inputs:** `filePath=C:\Code-Repo\TradeWise\src\TradeWise.Infrastructure\Persistence\IngestionRunRepository.cs` (same file `format_document_preview` had just succeeded on in the same turn)
- **Symptom:** Three independent calls across two turns returned:
  > "Invalid operation: Document not found: C:\Code-Repo\TradeWise\src\TradeWise.Infrastructure\Persistence\IngestionRunRepository.cs. The workspace may need to be reloaded (workspace_reload) if the state is stale."
  Each response reported `staleAction: "auto-reloaded"`, so an internal reload did fire — but the organize-usings resolver still couldn't see the document. Same-turn call to `organize_usings_preview` on `MigrationRunner.cs` succeeded (empty changes). Same-turn call to `format_document_preview` on the affected file also succeeded.
- **Impact:** The Phase 6 "6e Organize Usings" step was effectively skipped for the most-touched file in the audit. Blocked cross-tool-chain verification ("after `code_fix_apply`, run `organize_usings` on the modified file").
- **Workaround:** Used `MigrationRunner.cs` as the organize-usings target instead (empty-changes preview), which proved the tool works but not that IngestionRunRepository was correctly re-indexed after `remove_dead_code_apply`.
- **Repro confidence:** **deterministic** for this specific file after it was touched by `remove_dead_code_apply`; not reproduced on other files in the same turn.

### Issue 4 — `validate_recent_git_changes` returns with no structured error envelope

- **Tool:** `validate_recent_git_changes` (experimental v1.18.1)
- **Inputs:** `summary=true, runTests=false` (repo is on `audit/deep-review-20260424` branch with 4 modified files and the `audit-reports/` directory new)
- **Symptom:** Verbatim response (only line produced by the server):
  > "An error occurred invoking 'validate_recent_git_changes'."
  No `category`, `tool`, `message`, `exceptionType`, or `_meta` fields. Unlike every other Roslyn MCP error path in this session — which produces `{error: true, category: …, tool: …, message: …, exceptionType: …}` envelopes — this one produces bare text.
- **Impact:** The git-scoped validation bundle was unreachable for the entire run. Fallback required switching to `validate_workspace(summary=true)` with session-tracked changes (which worked). Approximately 30 s of triage cost (re-read tool description, retry, ship without it).
- **Workaround:** `validate_workspace(summary=true)` — produced clean `overallStatus=clean` output off the change tracker's session-wide list.
- **Repro confidence:** **one-shot** this session (single call), but the lack of envelope is structural rather than state-dependent.

### Issue 5 — `format_range_apply` preview-token expiry across auto-reload

- **Tool:** `format_range_apply` (experimental)
- **Inputs:** `previewToken=17fe1591f3254b279658da8da613581e` returned seconds earlier by `format_range_preview` on IngestionRunRepository.cs lines 195-205
- **Symptom:**
  > "Not found: Preview token '17fe1591f3254b279658da8da613581e' not found or expired.. Ensure the workspace is loaded (workspace_load) and the identifier is correct."
  The preview had completed successfully (`elapsedMs=3230, staleAction="auto-reloaded"`); within a single turn and `<5 s` wall-clock, another writer (`add_pragma_suppression` for IDE0079) triggered another auto-reload that invalidated the format_range token before I could apply it.
- **Impact:** Phase 6 "6e Format range" apply was skipped and marked `exercised-preview-only`. Raised the writer-reclassification verification gap because format_range was the only writer I couldn't complete an apply on.
- **Workaround:** Record the apply as blocked; rely on `format_document_apply` (which completed earlier) for writer-round-trip evidence instead.
- **Repro confidence:** **intermittent** — depends on whether another writer fires between preview and apply. Reproducible when two writer calls are parallelized in the same turn.

### Issue 6 — `move_type_to_file_preview` reports enum as "not found"

- **Tool:** `move_type_to_file_preview` (stable)
- **Inputs:** `sourceFilePath=C:\Code-Repo\TradeWise\src\TradeWise.Domain\Ingestion\IngestionStatuses.cs, typeName=IngestionTerminalStatus` (an enum living in that file alongside other enums)
- **Symptom:**
  > "Invalid operation: Type 'IngestionTerminalStatus' not found in C:\Code-Repo\TradeWise\src\TradeWise.Domain\Ingestion\IngestionStatuses.cs.."
  Type demonstrably exists — `symbol_search("IngestionTerminalStatus")` resolves it, it's referenced 10× across the solution per `symbol_impact_sweep`, `find_type_mutations(metadataName=…IngestionTerminalStatus)` resolves. The "not found" framing points at the wrong root cause — the tool likely doesn't support enum moves but reports the error as if the symbol doesn't exist.
- **Impact:** Misleading error text; operator would waste time double-checking the file path / type spelling before realizing the tool doesn't handle enums.
- **Workaround:** Skipped the enum move; used `split_class_preview` on a different target for split-file coverage.
- **Repro confidence:** **deterministic** for enum targets.

### Issue 7 — `symbol_signature_help` vs `callers_callees` caret-resolution divergence (response-contract)

- **Tools:** `symbol_signature_help`, `callers_callees`
- **Inputs:** Both called with `filePath=C:\Code-Repo\TradeWise\src\TradeWise.Application\Ingestion\IngestionOrchestrator.cs, line=74, column=30` — caret sits on a `[SuppressMessage(...)]` attribute decorator above the `RunAsync` method declaration on line 78.
- **Symptom:**
  - `callers_callees` auto-promotes past the attribute and returns `RunAsync` at line 78, col 44 (9 callers, 42 callees).
  - `symbol_signature_help` — with `preferDeclaringMember=true` (default) — stays on the attribute symbol and returns `SuppressMessageAttribute.SuppressMessageAttribute(string category, string checkId)`.
  Two tools with overlapping semantics (and the same `preferDeclaringMember` flag) resolve the same caret to different symbols.
- **Impact:** Agents that use `symbol_signature_help` as a cheap pre-check before committing to a heavier `callers_callees` call get a different answer than the downstream tool. Required a retry with an explicit enclosing-method column to reconcile.
- **Workaround:** Use `enclosing_symbol(line, col)` first to pin the target, then pass the resolved position to downstream tools.
- **Repro confidence:** **deterministic** — file/line/column triple reproduces.

### Issue 8 — `symbol_search` with empty query returns 5 random-looking symbols

- **Tool:** `symbol_search` (stable)
- **Inputs:** `query="" , limit=5`
- **Symptom:** Returns 5 symbols (Tenant, .ctor, Id, DisplayName, CreatedAt — the first 5 symbols in lexical order from the solution). Expected: either an empty result set or an `InvalidArgument` envelope ("query must be non-empty").
- **Impact:** Surprising behavior in negative-probe testing; could be abused as a "list first N symbols" path that isn't the intent. Low impact but a schema-vs-behavior FLAG.
- **Workaround:** Treat empty query as intentionally rejected by callers.
- **Repro confidence:** **deterministic**.

### Issue 9 — `workspace_changes` collapses writer identities into generic buckets

- **Tool:** `workspace_changes` (stable)
- **Inputs:** post-Phase-6 enumeration, `workspaceId=9dc42b164b86497585454d7adc9687a6`
- **Symptom:** The session log lists 10 applies, but `toolName` buckets sibling writers:
  - `remove_dead_code_apply`, `format_document_apply`, `code_fix_apply`, `rename_apply` — all tagged `refactoring_apply`.
  - `add_pragma_suppression` — tagged `apply_text_edit` (same as the header-comment edit).
  - `set_diagnostic_severity` / `set_editorconfig_option` — not logged at all (not a C# source mutation).
  Verbatim example: the pragma-suppression write is logged as `{"sequenceNumber": 7, "description": "Apply text edit to IngestionRunRepository.cs", "toolName": "apply_text_edit"}`. Real semantic intent is recoverable only from the free-text `description`.
- **Impact:** Session-auditing agents (like this retro) cannot filter `workspace_changes` by writer-kind; they need a `description`-substring heuristic. Also: `revert_last_apply` isn't logged as an entry, so a session log + revert leaves ambiguity about which entries are live vs reverted.
- **Workaround:** Parse `description` free-text. Cross-reference `workspace_version` bumps to detect reverts.
- **Repro confidence:** **deterministic**.

### Issue 10 — `get_prompt_text` requires trial-and-error on parameter signatures

- **Tool:** `get_prompt_text` (experimental)
- **Inputs:** First attempts to render `explain_error`, `suggest_refactoring`, `refactor_loop` each failed with:
  > "Parameter 'parametersJson' is invalid: Prompt parameter 'workspaceId' (type String) is required but missing from parametersJson."
  > "Parameter 'parametersJson' is invalid: Prompt parameter 'intent' (type String) is required but missing from parametersJson."
- **Symptom:** The catalog (`roslyn://server/catalog/prompts/0/20`) lists the 20 prompts but exposes only `name`, `summary`, and `supportTier` per entry — no `parameters: [...]` block. Callers cannot construct a valid `parametersJson` without a round-trip failing call to discover the signature.
- **Impact:** Phase 16 prompt verification exercised only 3 of 20 prompts cleanly this run; the others would each need a discovery-round-trip before being usable. Scorecard for 17 prompts degraded to `needs-more-evidence` purely because of schema-learning cost.
- **Workaround:** Trial-and-error with the error message's param-name hint; refetch with the missing param added.
- **Repro confidence:** **deterministic** — any prompt with a required parameter reproduces.

## 2b. Missing tool gaps

### Gap 1 — "Re-ingest this document" / document-reindex

- **Task:** After `remove_dead_code_apply` touched `IngestionRunRepository.cs`, `organize_usings_preview` couldn't find that specific document (Issue 3) even though the workspace reported `staleAction=auto-reloaded`. `format_document_preview` on the same file did see it.
- **Why Roslyn-shaped:** This is a pure Roslyn document-identity concern (MSBuildWorkspace + per-tool document lookup paths). Belongs under the workspace lifecycle surface, not user-space.
- **Proposed tool shape:**
  - **Name:** `reingest_document`
  - **Description:** Force the workspace to re-read a specific source file from disk and re-register it with every tool family's document-lookup cache. Narrower than `workspace_reload` (which reopens the entire solution) and idempotent.
  - **Input:** `{workspaceId, filePath}`
  - **Output:** `{workspaceVersion, documentId, registeredInCaches: string[]}` (names of tool families that re-acknowledged the doc)
- **Closest existing tool:** `workspace_reload` — too heavy (entire solution), and did not resolve Issue 3 (auto-reload already triggered).

### Gap 2 — FixAll fallback strategy surfaced as tool behaviour, not guidance text

- **Task:** Drive 26 IDE0300 fixes in one operation. `fix_all_preview` failed at both solution and document scope (Issue 1).
- **Why Roslyn-shaped:** FixAll is a Roslyn concept — the wrapper should either produce a preview or a structured failure, not dump an `InvalidOperationException` into a `guidanceMessage` text field.
- **Proposed tool shape:** Not a new tool, but a **behavior change + envelope extension** on `fix_all_preview`:
  - When the registered FixAll provider crashes, catch the exception and emit `{error: true, category: "FixAllProviderCrash", diagnosticId, scope, innerException: "...", perOccurrenceFallback: [{filePath, line, column, previewReachable: true|false}]}` so the caller can automatically fan out to `code_fix_preview` per occurrence.
  - Also: a sibling `fix_all_per_occurrence_preview` that internally pages through `code_fix_preview` when FixAll is broken.
- **Closest existing tool:** `fix_all_preview` itself (broken), `code_fix_preview` (works but requires the caller to walk the occurrence list manually).

### Gap 3 — Prompt parameter signatures in the catalog

- **Task:** Render 4+ prompts via `get_prompt_text`. 3 of 4 failed first call on missing parameters (Issue 10).
- **Why Roslyn-shaped:** Prompts are a first-class MCP surface. The catalog should expose enough schema for a caller to construct valid invocations without a discovery round-trip.
- **Proposed tool shape:** Not a new tool — extend `roslyn://server/catalog/prompts/{offset}/{limit}` to include `parameters: [{name, type, required, defaultValue, description}]` on each entry. Same change would flow through `server_info.surface.prompts` if desired.
- **Closest existing tool:** `get_prompt_text` error message names the missing param — but only one per call, so discovering a 3-required-param prompt takes 3 round-trips.

### Gap 4 — "Dry-run this preview token through to apply" without writer sequencing friction

- **Task:** Run `format_range_preview` then `format_range_apply` back-to-back. Another writer between them (triggering auto-reload) invalidated the token (Issue 5).
- **Why Roslyn-shaped:** Token lifetime across workspace version bumps is a Roslyn internal concern. Callers shouldn't have to hand-sequence preview/apply to avoid concurrent writer interference.
- **Proposed tool shape:**
  - **Name:** `preview_and_apply_if_clean` (composite).
  - **Description:** Take the same inputs as a `*_preview` call, internally generate the token, run `*_apply` in the same gate-held transaction, and return `{diff, applied: true, workspaceVersion}`. Fail closed on any concurrent mutation.
  - **Inputs:** `{workspaceId, writerTool, writerInputs, verify: true|false}`
  - **Outputs:** `{applied, previewDiff, verification: {status, newDiagnostics}}`
- **Closest existing tool:** `apply_text_edit(verify=true)` does the preview+apply+verify path for text edits. The same pattern is missing for `format_range`, `organize_usings`, `rename`, `code_fix`, etc. — each has its own separate preview/apply pair.

### Gap 5 — Enum-type-aware move / reshape tool

- **Task:** Move `IngestionTerminalStatus` (an enum in a multi-type file) into its own file. `move_type_to_file_preview` rejected with misleading "type not found" (Issue 6).
- **Why Roslyn-shaped:** Enums are Roslyn type-kinds; splitting them into their own files is a structurally simple refactor that the current tool excludes.
- **Proposed tool shape:** Either (a) extend `move_type_to_file_preview` to accept `TypeKind=Enum | Class | Record | Struct | Interface | Delegate` inputs and emit `{error: "type-kind not supported for this refactor"}` when excluded — but for enums, **support the move**. Or (b) a dedicated `move_enum_to_file_preview` / more generally `move_declaration_to_file_preview` keyed off the declaration-syntax-kind.
- **Closest existing tool:** `move_type_to_file_preview` — but currently enum/delegate targets silently fail as "not found" which is worse than an explicit "not supported".

## 3. Recurring friction patterns

### Pattern 1 — Auto-reload side-effects break preview tokens and document lookups

- **What happened:** `staleAction=auto-reloaded` fired on 8+ tool calls this session (format_document_preview, organize_usings_preview, code_fix_preview, rename_preview, apply_text_edit, apply_multi_file_edit, workspace_changes, validate_workspace). Two concrete breakages: (a) `format_range_apply` rejected a preview token that was 2 s old because another writer triggered a reload between preview and apply (Issue 5); (b) `organize_usings_preview` returned "Document not found" for a file that `format_document_preview` had just read in the same turn (Issue 3).
- **Why it recurs:** Every apply operation triggers a workspace-version bump. Once a caller batches multiple writers into a single turn, auto-reload interleaves non-deterministically with pending preview tokens and per-tool document caches. Parallel tool dispatch from the client amplifies this.
- **What would fix it:** Pin preview tokens to a workspace version range rather than a single version, OR surface an explicit `workspaceVersionValidRange: [min, max]` on preview responses so callers know when to re-preview. Alternatively, the composite "preview+apply under single gate" tool in Gap 4 sidesteps the problem entirely.

### Pattern 2 — Schema-vs-behaviour inconsistencies in error envelopes

- **What happened:** Most negative probes returned well-structured envelopes (`{error: true, category: "NotFound"|"InvalidArgument"|"InvalidOperation", tool, message, exceptionType}`). But two experimental tools (`validate_recent_git_changes`, `fix_all_preview`) break the contract: one emits bare text "An error occurred invoking…", the other dumps an `InvalidOperationException` into a `guidanceMessage` while still reporting `success`-shaped fields.
- **Why it recurs:** New experimental tools land without a lint against the error-envelope contract. The prompt's audit explicitly calls out the expectation that every tool produces a structured envelope when it errors.
- **What would fix it:** A server-side lint / test fixture that asserts every `McpServerTool` method produces a `{error: true, category: …}` envelope on every thrown exception path — ideally a shared middleware so individual tool authors cannot bypass it.

### Pattern 3 — Generic error text masks actionable root cause

- **What happened:** `move_type_to_file_preview` reports "Type X not found" when the real cause is "type-kind Enum unsupported" (Issue 6). `fix_all_preview` reports "Sequence contains no elements" when the real cause is "the IDE0300 FixAll provider hit an empty collection internally" (Issue 1). `set_conditional_property_preview` reports "only support equality conditions on $(Configuration) / $(TargetFramework) / $(Platform)" without showing the accepted syntax shape (e.g. `'$(Configuration)' == 'Debug'` vs `$(Configuration) == 'Debug'`).
- **Why it recurs:** Tool error paths frequently surface the innermost exception message rather than mapping it to a caller-actionable category. Callers have to guess whether "not found" means missing-file, missing-type, unsupported-kind, or stale-index.
- **What would fix it:** Error-envelope refinement — add a `subCategory` field (`UnsupportedTypeKind`, `FixAllProviderBug`, `ConditionSyntax`, etc.) so scripted callers can branch. The existing `category` field is too coarse (everything falls into `InvalidOperation`).

### Pattern 4 — Response-contract drift (null vs [], bucket toolNames, dead fields)

- **What happened:**
  - `type_hierarchy.baseTypes` / `derivedTypes` returned `null` instead of `[]` when empty, while sibling tools use empty arrays (FLAG-5).
  - `workspace_changes.toolName` collapses distinct writers into `refactoring_apply` / `apply_text_edit` (Issue 9).
  - `get_nuget_dependencies(summary=true)` returns empty `packages: []` and `projects: []` alongside populated `summaries: [...]` (FLAG-4).
  - `diagnostic_details.supportedFixes: []` for SYSLIB1045 (which has a canonical fix) (FLAG-2).
- **Why it recurs:** Each tool authored in isolation; no shared DTO convention for empty-collections-vs-null, bucket-identifier vs real-tool-name, or summary-vs-full payload shape.
- **What would fix it:** A server-side response-DTO convention doc + serializer defaults: (a) never emit `null` for collections, (b) emit the real `toolName` in session logs, (c) exclude (don't emit empty) fields that are orthogonal to the selected response mode.

### Pattern 5 — Experimental tools gated on full surface coverage cannot be promoted from scaffold-stage repos

- **What happened:** 20 experimental tools ended the run with `keep-experimental` or `needs-more-evidence` because the scaffold-stage TradeWise repo genuinely has no target for them — no extract-interface candidate, no multi-occurrence legitimate duplicate, no extract-method target. `find_dead_fields` and `find_dead_locals` (both v1.29.0 new) returned 0 hits, giving no behavioural signal.
- **Why it recurs:** The promotion rubric requires "exercised end-to-end with at least one non-default parameter path" + "for preview/apply families, the apply sibling round-tripped cleanly". On a scaffold repo with no dead fields, no duplicated methods, no settable public properties, the audit has nothing to feed these tools.
- **What would fix it:** (a) Ship a small test-fixture solution bundled with the prompt that covers each experimental tool's happy path, so auditors can opt into "synthetic fixture mode" for promotion-scorecard purposes without needing a real target in the audited repo. Or (b) relax the rubric: "exercised with structured-empty-response on a clean repo" counts as a valid promote-signal for audit/detector-style tools whose primary output is an absence.

## 4. Suggested findings (up to 5)

### Finding 1
- **id:** `fix-all-preview-sequence-contains-no-elements`
- **priority hint:** **high** — blocks every multi-occurrence diagnostic cleanup workflow; 26 IDE0300 sites unfixed in this audit; `code_fix_preview` works so the underlying code fix is fine.
- **title:** Handle empty-collection crash in `fix_all_preview` for IDE0300 (and likely other modern-C# diagnostics)
- **summary:** `fix_all_preview` fails at BOTH `scope=solution` and `scope=document` with the verbatim error *"The registered FixAll provider for 'IDE0300' threw while computing the fix (InvalidOperationException: Sequence contains no elements). Try code_fix_preview on individual occurrences, or narrow the scope (document / project) to isolate the failing occurrence."* The guidance text is actionable, but the tool surface presents this as a `success`-shaped empty response (`fixedCount: 0, changes: []`) with a `guidanceMessage`, not as a failure envelope. Individual `code_fix_preview` on the same diagnostic works, isolating the bug to the FixAll wrapper's handling of an empty-candidate set.
- **proposed action:** Catch the `InvalidOperationException` inside the FixAll provider wrapper and either (a) emit a structured `{error: true, category: "FixAllProviderCrash", diagnosticId, scope, perOccurrenceFallbackAvailable: true}` envelope, or (b) fan out to per-occurrence `code_fix_preview` internally when the provider short-circuits. Either fix restores the "Batch Diagnostic Fix" workflow hint documented in the catalog.
- **evidence:** `2a#fix_all_preview`, `3#Pattern-2`, `3#Pattern-3`.

### Finding 2
- **id:** `scaffold-test-invalid-identifier`
- **priority hint:** **high** — any `scaffold_test_apply` against a fully-qualified-name target or a static class produces a file that fails compile; catalog lists `scaffold_test_preview` as **stable** so users expect it to work.
- **title:** Sanitize class identifier and handle static-class targets in `scaffold_test_preview`
- **summary:** For `targetTypeName=TradeWise.Domain.Tenancy.TenantConstants` (a `public static class`), the preview emits `public class TradeWise.Domain.Tenancy.TenantConstantsGeneratedTests { … var subject = new TradeWise.Domain.Tenancy.TenantConstants(); }`. Two compile errors: (1) class identifier contains `.` (illegal), (2) `new` against a static class (illegal). The tool also injects an unrelated `using TradeWise.Domain.Ingestion;` leftover from sibling-test-file inference.
- **proposed action:** (1) Reduce the target's fully-qualified name to its terminal segment when building the generated class identifier (or require caller to pass a short name). (2) Detect `IsStatic` on the target type and scaffold a reflection-only test or a stub `[Fact]` without constructing an instance. (3) Tighten the sibling-inference scoping so `using` directives from the reference test file only flow in when they're used by the generated body.
- **proposed action:** Per the cross-cutting "apply sibling must round-trip cleanly" rubric, the current bug also blocks `scaffold_test_preview` promotion.
- **evidence:** `2a#scaffold_test_preview`, `3#Pattern-3`.

### Finding 3
- **id:** `organize-usings-document-not-found-after-apply`
- **priority hint:** **high** — affects any post-refactor cleanup flow where a file is touched by one writer and then needs organize-usings; defeats the documented "Batch Diagnostic Fix" workflow.
- **title:** Unify document-identity resolution between `organize_usings_preview` and `format_document_preview` after writer-triggered auto-reload
- **summary:** After `remove_dead_code_apply` touched `IngestionRunRepository.cs`, `organize_usings_preview` on that file consistently returned *"Invalid operation: Document not found: C:\…\IngestionRunRepository.cs. The workspace may need to be reloaded"* — across 3 calls in 2 turns, despite `staleAction=auto-reloaded` reporting in every response. Same file, same turn, `format_document_preview` succeeded. Same turn, `organize_usings_preview` on a different file (`MigrationRunner.cs`) succeeded.
- **proposed action:** Audit the `DocumentResolver` path used by `organize_usings_preview` vs `format_document_preview` — they should share the post-auto-reload document lookup. Likely a caching bug where organize-usings holds a pre-apply document reference that auto-reload invalidated but didn't replace.
- **evidence:** `2a#organize_usings_preview`, `3#Pattern-1`.

### Finding 4
- **id:** `validate-recent-git-changes-no-envelope`
- **priority hint:** **medium** — tool is experimental so a broken experimental tool is less severe, but the error-envelope violation is structural and worth flagging before the tool is promoted.
- **title:** Restore structured error envelope for `validate_recent_git_changes`
- **summary:** Every other Roslyn MCP error path in this session returned `{error: true, category: <enum>, tool: <name>, message: <string>, exceptionType: <clr-type>, _meta: {...}}`. `validate_recent_git_changes` returned bare text *"An error occurred invoking 'validate_recent_git_changes'."* — no category, no message, no `_meta`. Callers cannot distinguish "git not on PATH" from "workspace invalid" from "internal crash" from the envelope. Documented fallback behavior ("falls back to full-workspace scope and surfaces the fallback via the Warnings field when git is unavailable") isn't observable.
- **proposed action:** Wire the same `McpToolException → structured envelope` middleware used by `validate_workspace` (which has the identical shape on the success path but a clean error path). Confirm the git-availability detection raises a `category=DependencyMissing, subCategory=GitNotAvailable` envelope rather than bubbling as an unhandled exception.
- **evidence:** `2a#validate_recent_git_changes`, `3#Pattern-2`.

### Finding 5
- **id:** `expose-prompt-parameter-signatures-in-catalog`
- **priority hint:** **medium** — not a reliability bug, but a concrete ergonomics gap that degraded this audit's Phase 16 prompt coverage to 3/20 rendered prompts purely because of schema-learning cost. Low-effort fix.
- **title:** Publish per-prompt `parameters` array in `roslyn://server/catalog/prompts/{offset}/{limit}`
- **summary:** The prompts catalog lists `name`, `supportTier`, `summary`, `readOnly`, `destructive` — but no parameter schema. Callers of `get_prompt_text` only learn a prompt's required parameters by invoking it and parsing the verbatim error *"Parameter 'parametersJson' is invalid: Prompt parameter 'workspaceId' (type String) is required but missing from parametersJson."* For a prompt with N required parameters, that's N failing round-trips before a successful render. 3 of 4 prompts in this session failed first call on this.
- **proposed action:** Extend the prompt-page resource DTO with `parameters: [{name, type, required, defaultValue, description}]` per entry. The parameter metadata is already known server-side (the `[McpServerPrompt]` attribute captures it); it just isn't exposed through the resource channel. Zero effort on the caller side — callers that ignore the new field still work; callers that use it skip the discovery round-trip.
- **evidence:** `2a#get_prompt_text`, `2b#Gap-3`.

## 5. Meta-note

This session was a deep **refactoring**-phase audit (18-phase protocol, 148 of 161 tools exercised, 10 Phase-6 applies landed). Roslyn MCP friction concentrated in **reliability** (4 outright P1 FAILs: `fix_all_preview`, `scaffold_test_preview`, `organize_usings_preview` on post-apply files, `validate_recent_git_changes` silent error) and **ergonomics** (prompt parameter signatures hidden, response contracts drifting — null vs [], generic `toolName` buckets, dead summary fields). Coverage is broad and schema mostly consistent, but the FixAll, composite-preview, and auto-reload paths are where future audits should look first. Next run I'll default to `enclosing_symbol(line, col)` before any `symbol_*_help` / `callers_callees` call to avoid attribute-caret ambiguity (Issue 7), and pre-fetch prompt parameter signatures via a one-shot failing call per prompt before attempting to render it.
