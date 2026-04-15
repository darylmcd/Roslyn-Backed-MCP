# Experimental Promotion Exercise Report

> **Status: PARTIAL / PAUSED (2026-04-15).** Phase 0 complete, Phase 1 partial (5 of 14 refactoring tools exercised — `fix_all`, `format_range`, `extract_interface`, `extract_type`, `move_type_to_file`). Phases 1f–1k, 2–11 **not executed**. Paused at user request after substantive server bugs surfaced (9.1 consumer-breakage, 9.2 no-fixers-loaded, 9.8 false-green compile_check). All encountered findings captured below with reproducer evidence. Next agent should triage §9 before continuing the exercise.
>
> **Quick triage index (sorted by severity):**
> - **HIGH** → 9.1 (`extract_type_apply` breaks consumers), 9.8 (`compile_check` false-green after auto-reload)
> - **MEDIUM** → 9.2 (no fixers loaded), 9.10 (load-before-restore ordering), 9.13 (`revert_last_apply` leaves orphan files)
> - **LOW / cosmetic** → 9.3, 9.4, 9.5, 9.6, 9.7, 9.9, 9.11, 9.12

## 1. Header
- **Date:** 2026-04-15
- **Audited solution:** SampleSolution.slnx (SampleApp + SampleLib + SampleLib.Tests)
- **Audited revision:** commit `172d3c5118178eae2664768ced630b38d9418309` on `worktree-experimental-promotion-2026-04-15` (disposable)
- **Entrypoint loaded:** `C:\Code-Repo\Roslyn-Backed-MCP\.claude\worktrees\experimental-promotion-2026-04-15\SampleSolution.slnx`
- **Audit mode:** `full-surface` (disposable worktree available)
- **Isolation:** disposable git worktree at `.claude/worktrees/experimental-promotion-2026-04-15/`
- **Client:** Claude Code (with Roslyn MCP plugin 1.18.0)
- **Workspace id:** `8e1b03c485b942b5b74fd8d9710e601f`
- **Server:** `roslyn-mcp 1.18.0+172d3c5` (.NET 10.0.6, Roslyn 5.3.0.0, Windows 10.0.26200)
- **Catalog version:** `2026.04`
- **Experimental surface:** 40 experimental tools, 20 experimental prompts, 1 experimental resource
- **Scale:** 3 projects, 23 documents
- **Repo shape:** multi-project; test project present (xUnit — to verify); single-target-framework (net10.0); Directory.Packages.props present in main repo (samples inherit); no source generators in samples (SampleLib.Generators is outside SampleSolution); DI usage minimal; `.editorconfig` present at repo root
- **Prior issue source:** `ai_docs/backlog.md` (current HEAD)

## 2. Coverage ledger (experimental surface)

> Seeded from live catalog at Phase 0; final `status`/`recommendation` filled in per phase and at Phase 10.

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | seeded | 1a | | |
| tool | `fix_all_apply` | refactoring | seeded | 1a | | |
| tool | `format_range_apply` | refactoring | seeded | 1b | | |
| tool | `extract_interface_preview` | refactoring | seeded | 1c | | |
| tool | `extract_interface_apply` | refactoring | seeded | 1c | | |
| tool | `extract_type_apply` | refactoring | seeded | 1d | | |
| tool | `move_type_to_file_apply` | refactoring | seeded | 1e | | |
| tool | `bulk_replace_type_apply` | refactoring | seeded | 1f | | |
| tool | `extract_method_apply` | refactoring | seeded | 1g | | |
| tool | `format_check` | refactoring | seeded | 1h | | |
| tool | `restructure_preview` | refactoring | seeded | 1h | | |
| tool | `replace_string_literals_preview` | refactoring | seeded | 1i | | |
| tool | `change_signature_preview` | refactoring | seeded | 1j | | |
| tool | `symbol_refactor_preview` | refactoring | seeded | 1k | | |
| tool | `create_file_apply` | file-operations | seeded | 2a | | |
| tool | `move_file_apply` | file-operations | seeded | 2b | | |
| tool | `delete_file_apply` | file-operations | seeded | 2c | | |
| tool | `add_central_package_version_preview` | project-mutation | seeded | 3e | | |
| tool | `apply_project_mutation` | project-mutation | seeded | 3a-3d | | |
| tool | `scaffold_type_preview` | scaffolding | seeded | 4a | | |
| tool | `scaffold_type_apply` | scaffolding | seeded | 4a | | |
| tool | `scaffold_test_apply` | scaffolding | seeded | 4b | | |
| tool | `scaffold_test_batch_preview` | scaffolding | seeded | 4c | | |
| tool | `remove_dead_code_apply` | dead-code | seeded | 5a | | |
| tool | `remove_interface_member_preview` | dead-code | seeded | 5a | | |
| tool | `apply_multi_file_edit` | editing | seeded | 5b | | |
| tool | `preview_multi_file_edit` | editing | seeded | 5b-i | | |
| tool | `preview_multi_file_edit_apply` | editing | seeded | 5b-i | | |
| tool | `apply_with_verify` | undo | seeded | 5e | | |
| tool | `move_type_to_project_preview` | cross-project-refactoring | seeded | 6 | | |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | seeded | 6 | | |
| tool | `dependency_inversion_preview` | cross-project-refactoring | seeded | 6 | | |
| tool | `migrate_package_preview` | orchestration | seeded | 7 | | |
| tool | `split_class_preview` | orchestration | seeded | 7 | | |
| tool | `extract_and_wire_interface_preview` | orchestration | seeded | 7 | | |
| tool | `apply_composite_preview` | orchestration | seeded | 7 | | |
| tool | `symbol_impact_sweep` | analysis | seeded | 7c.1 | | |
| tool | `test_reference_map` | validation | seeded | 7c.2 | | |
| tool | `validate_workspace` | validation | seeded | 7c.3 | | |
| tool | `get_prompt_text` | prompts | seeded | 7c.4 | | |
| resource | `source_file_lines` | resources | seeded | 7b | | |
| prompt | `explain_error` | prompts | seeded | 8 | | |
| prompt | `suggest_refactoring` | prompts | seeded | 8 | | |
| prompt | `review_file` | prompts | seeded | 8 | | |
| prompt | `analyze_dependencies` | prompts | seeded | 8 | | |
| prompt | `debug_test_failure` | prompts | seeded | 8 | | |
| prompt | `refactor_and_validate` | prompts | seeded | 8 | | |
| prompt | `fix_all_diagnostics` | prompts | seeded | 8 | | |
| prompt | `guided_package_migration` | prompts | seeded | 8 | | |
| prompt | `guided_extract_interface` | prompts | seeded | 8 | | |
| prompt | `security_review` | prompts | seeded | 8 | | |
| prompt | `discover_capabilities` | prompts | seeded | 8 | | |
| prompt | `dead_code_audit` | prompts | seeded | 8 | | |
| prompt | `review_test_coverage` | prompts | seeded | 8 | | |
| prompt | `review_complexity` | prompts | seeded | 8 | | |
| prompt | `cohesion_analysis` | prompts | seeded | 8 | | |
| prompt | `consumer_impact` | prompts | seeded | 8 | | |
| prompt | `guided_extract_method` | prompts | seeded | 8 | | |
| prompt | `msbuild_inspection` | prompts | seeded | 8 | | |
| prompt | `session_undo` | prompts | seeded | 8 | | |
| prompt | `refactor_loop` | prompts | seeded | 8 | | |

## 3. Performance baseline (`_meta.elapsedMs`)

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| *(filled per phase)* | | | | | | | |

## 4. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| *(filled per phase)* | | | | | |

## 5. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| *(filled in Phase 9)* | | | | |

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | scope=project | pending | |
| `scaffold_type_preview` | kind=record / kind=interface | pending | |
| `get_msbuild_properties` | propertyNameFilter | pending | |
| File operations | move with namespace update | pending | |
| Project mutation | conditional property, CPM | pending | |

## 7. Prompt verification (Phase 8)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| *(filled in Phase 8)* | | | | | | | |

## 8. Experimental promotion scorecard

> One row per experimental entry. Rubric in Phase 10.

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| *(filled in Phase 10)* | | | | | | | | | | |

## 9. MCP server issues (bugs)

### 9.1 `extract_type_apply` does not update external consumer call sites

| Field | Detail |
|--------|--------|
| Tool | `extract_type_preview` + `extract_type_apply` |
| Input | workspace=`8e1b03c485b942b5b74fd8d9710e601f`, filePath=`SampleLib/AnimalService.cs`, typeName=`AnimalService`, memberNames=`["MakeThemSpeak"]`, newTypeName=`AnimalSpeaker` |
| Expected | After extracting `MakeThemSpeak` into `AnimalSpeaker`, call sites (e.g. `SampleApp/Program.cs:6 service.MakeThemSpeak(animals)`) are rewritten to go through the new type (either via the injected field delegate OR a direct `AnimalSpeaker` reference). Post-apply `compile_check` is clean. |
| Actual | `SampleApp/Program.cs:6` still calls `service.MakeThemSpeak(animals)` on `AnimalService`, producing CS1061 ("AnimalService does not contain a definition for MakeThemSpeak"). The generated `_animalSpeaker` field in `AnimalService` is uninitialised, producing CS8618 (non-nullable field) and CS0169 (never used). Workspace is left broken until `revert_last_apply` runs. |
| Severity | High — advertised preview/apply contract promises compilation preserved; this breaks it on any `public` member extraction. |
| Reproducibility | 100% on this probe (single attempt). |
| Evidence | Preview token `ea6acf2a1af44a8bbbe23692a0ceda1e`; post-apply compile_check returned `errorCount=1, warningCount=3`; revert restored clean state. |

### 9.2 `fix_all_preview` — no code-fix providers loaded for any exercised diagnostic ID

| Field | Detail |
|--------|--------|
| Tool | `fix_all_preview` |
| Input | Exercised with diagnosticId ∈ {CA1822, CA1861, IDE0005, CS8019, CS0414} against SampleSolution.slnx; both `scope=solution` and `scope=project`. |
| Expected | At least common fixers (IDE0005 unused-using, CA1822 make-static, CS0414 remove-field) should be present given `Microsoft.CodeAnalysis.CSharp.Features` and `Microsoft.CodeAnalysis.NetAnalyzers` are in the package graph. |
| Actual | Every call returns `fixedCount=0, previewToken=""` and `guidanceMessage="No code fix provider is loaded for diagnostic 'X'. Restore analyzer packages (IDE/CA rules). Use list_analyzers to see loaded diagnostic IDs."` (IDE0005 specifically points at `organize_usings_preview` — good!). `list_analyzers` shows 12 analyzer assemblies / 370 rules registered, but no `CodeFixProvider` appears attached to any of them. |
| Severity | Medium — blocks the entire `fix_all` family from being exercised end-to-end; error message is **actionable** (points at alternatives). May be a server-side MEF composition issue rather than a true fixer absence. |
| Reproducibility | 100% across 5 distinct diagnostic IDs. |
| Suggested investigation | Verify `Microsoft.CodeAnalysis.CSharp.Features` is actually loaded into the server's ComponentModel and that CodeFixProviders are exported via `[ExportCodeFixProvider]`. Consider restart/rebuild of Layer 1 global tool. |

### 9.3 `type_hierarchy` rejects `metadataName`-only invocations

| Field | Detail |
|--------|--------|
| Tool | `type_hierarchy` (stable — but surfaced while exercising Phase 1c) |
| Input | `metadataName="SampleLib.IDog"` without `filePath/line/column`. |
| Expected | Schema advertises `metadataName` as an alternative locator (same as `find_references_bulk`). |
| Actual | `"Invalid argument: Provide either filePath with line and column, or symbolHandle."` — 1 ms rejection before resolution. |
| Severity | P3 schema/behaviour drift — same pattern as open backlog item `find-references-metadataname-parameter-rejected`. Broader than that item (now reproduces on `type_hierarchy` too). |
| Reproducibility | 100%. |

### 9.4 PreToolUse hook false-positive on `move_type_to_file_apply`

| Field | Detail |
|--------|--------|
| Tool | `move_type_to_file_apply` (hook gate, not the tool itself) |
| Input | Apply immediately after the matching `move_type_to_file_preview` in the same turn. Preview token `2a0ed002e6a94163a441a6c215c1db3f`. |
| Expected | Hook sees the preview in transcript and allows apply. |
| Actual | Hook returns `"Cannot verify preview was shown without access to transcript"` and blocks the apply. Recovered by re-previewing (token `f21b9bf7ddbc4a3d909d91cd2edcfeeb`), re-applying. |
| Severity | Low — workflow friction; no data loss. Adds ~2 tool calls per apply for any agent that doesn't know to pre-absorb the re-preview cost. |
| Suggested fix | Hook should trust the presence of a matching `previewToken` issued earlier in the session (token lives in `IPreviewStore`) rather than requiring transcript inspection. The server already validates token freshness on apply — the hook is duplicating that check with a weaker signal. |
| File(s) to edit | `hooks/hooks.json` (PreToolUse for `mcp__roslyn__*_apply` family). |

### 9.5 `extract_type_preview` strips the blank line between namespace and class

| Field | Detail |
|--------|--------|
| Tool | `extract_type_preview` |
| Input | workspace=`8e1b03c485b942b5b74fd8d9710e601f`, filePath=`SampleLib/AnimalService.cs`, typeName=`AnimalService`, memberNames=`["MakeThemSpeak"]`, newTypeName=`AnimalSpeaker`. |
| Expected | Preview diff should preserve surrounding trivia — the existing blank line between `namespace SampleLib;` and `public class AnimalService` should remain. |
| Actual | Preview diff showed `@@ -1,9 +1,9 @@ ... namespace SampleLib;\n-\n public class AnimalService` — the blank line was removed. A trailing file-end blank line was also stripped. |
| Severity | Low — purely cosmetic but generates noisy diffs and may fight with `.editorconfig` `insert_final_newline`. |
| Reproducibility | 100% on the one probe attempted. |
| File(s) to edit | `src/RoslynMcp.Roslyn/Services/ExtractTypeService.cs` — verify SyntaxFormatter is preserving trivia on the `ReplaceNode` step that strips the moved member from the source type. |

### 9.6 `extract_interface_preview` emits `base list` continuation on a new line instead of inline

| Field | Detail |
|--------|--------|
| Tool | `extract_interface_preview` |
| Input | filePath=`SampleLib/Dog.cs`, typeName=`Dog`, interfaceName=`IDog`. |
| Expected | `public class Dog : IAnimal, IDog` on one line. |
| Actual | Preview emits `public class Dog : IAnimal\n, IDog` — the new interface is appended on its own line with a leading comma. Compiles correctly, but violates idiomatic C# and the repo's `.editorconfig`. |
| Severity | Low — cosmetic; does not affect semantics. |
| Reproducibility | 100%. |
| File(s) to edit | `src/RoslynMcp.Roslyn/Services/ExtractInterfaceService.cs` — base-list augmentation should collapse to single-line form when the existing list is single-line. |

### 9.7 `format_range_preview` only partially normalizes whitespace

| Field | Detail |
|--------|--------|
| Tool | `format_range_preview` |
| Input | filePath=`SampleLib/AnimalService.cs`, range=`line 16:1-16:60` on `public void MakeThemSpeak(    IEnumerable<IAnimal>     animals   )`. |
| Expected | Full normalization → `public void MakeThemSpeak(IEnumerable<IAnimal> animals)` (all runs of internal whitespace collapsed, trailing whitespace before `)` removed). |
| Actual | Preview emitted `public void MakeThemSpeak(IEnumerable<IAnimal> animals   )` — leading-paren and inter-argument whitespace were collapsed, but the trailing whitespace before `)` was retained. Preview token `dbd53850b02545babba1d4691af38211`. |
| Severity | Low — partial format is confusing (was it idempotent? did the formatter finish?). `format_range` should either fully normalize or report the residual. |
| Reproducibility | 100% on one probe. |
| File(s) to edit | `src/RoslynMcp.Roslyn/Services/FormatService.cs` — confirm `Formatter.FormatAsync` is called with the full enclosing syntax node (parameter-list span), not just the user-supplied character range. Range formatters in Roslyn sometimes leave residual trivia when the selection bisects a whitespace trivia span. |

### 9.8 `compile_check` returned `completedProjects=0, totalProjects=0` with `success=true` after an auto-reload

| Field | Detail |
|--------|--------|
| Tool | `compile_check` (stable — but surfaced while auto-reload-driven validation was happening) |
| Input | Called immediately after `extract_interface_apply` succeeded; execution gate triggered `staleAction: "auto-reloaded"`, `staleReloadMs: 1123` during the call. |
| Expected | After a reload, either the check actually runs against 3 projects (`completedProjects=3, totalProjects=3`) or the response signals that the check was skipped. |
| Actual | Response `{ "success": true, "errorCount": 0, "warningCount": 0, "totalDiagnostics": 0, "completedProjects": 0, "totalProjects": 0 }` — returned a **misleadingly green** result with zero projects actually checked. |
| Severity | **Medium-high** — silent false-positive on compile verification is dangerous; an agent trusting this result would ship broken code. The same verification issued later (without triggering a stale-reload) returned accurate `completedProjects=3, totalProjects=3`. |
| Reproducibility | Once observed; likely race between `staleAction: "auto-reloaded"` finishing workspace re-init and the compilation pipeline being ready. |
| File(s) to edit | `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs` — when the gate reports `staleAction=auto-reloaded`, the compile pipeline must await fresh `Compilation` resolution before returning. If projects are 0, `success=false` with an actionable message ("workspace just reloaded; retry") is the safer default. `src/RoslynMcp.Roslyn/Execution/WorkspaceExecutionGate.cs` for the auto-reload timing. |
| Evidence | `_meta.staleAction: "auto-reloaded"`, `_meta.staleReloadMs: 1123`, `completedProjects: 0`, `totalProjects: 0`, `success: true`, `elapsedMs: 0`. |

### 9.9 `workspace_close` response claims `staleAction: "auto-reloaded"`

| Field | Detail |
|--------|--------|
| Tool | `workspace_close` (stable) |
| Input | `workspace_close(workspaceId=750a49ecba804a9fb73414b425e86ae6)` — simple close of a loaded workspace. |
| Expected | `_meta.staleAction: null` or absent — a close is destroying the workspace, not reloading it. |
| Actual | Response carried `_meta.staleAction: "auto-reloaded"`, `_meta.staleReloadMs: 1733` — the execution gate auto-reloaded the workspace immediately before destroying it. Harmless for the caller but wastes ~1.7 s and is a misleading response shape. |
| Severity | P4 — response-shape drift; no functional impact. |
| Reproducibility | Once observed. |
| File(s) to edit | `src/RoslynMcp.Roslyn/Execution/WorkspaceExecutionGate.cs` — `workspace_close` should short-circuit the stale-reload check (the workspace is being torn down regardless). |

### 9.10 Initial `workspace_load` does not wait for concurrent `dotnet restore` to finalize

| Field | Detail |
|--------|--------|
| Tool | `workspace_load` + `project_diagnostics` (stable, but observed while setting up Phase 0). |
| Input | Sequence: `workspace_close` (prior session) → `workspace_load(SampleSolution.slnx)` → `dotnet restore SampleSolution.slnx` (in Bash, in parallel) → `project_diagnostics`. |
| Expected | By the time `project_diagnostics` runs, the workspace reflects the restored package set — `SampleLib.Tests/AnimalServiceTests.cs` resolves `Microsoft.VisualStudio.TestTools.UnitTesting` correctly. |
| Actual | First `project_diagnostics` returned **9 CS0246/CS0234/CS0103 errors** against `AnimalServiceTests.cs` (MSTest types not found) even though restore had completed. `restoreHint` was correctly populated. Subsequent `workspace_reload` cleared the errors. |
| Severity | Medium — confusing out-of-the-box UX for agents that run `workspace_load` + `dotnet restore` in parallel (which the exercise prompt recommends). |
| Reproducibility | 100% if `workspace_load` is issued before `dotnet restore` finalizes the package graph on disk. |
| Suggested fix | `workspace_load` response could set `restoreHint` proactively when it notices unresolved `MetadataReference` entries, without waiting for an explicit diagnostics call to surface it. Alternatively, document in the tool description that callers should restore first then load (or load → reload after restore completes). |

### 9.11 `roslyn://server/catalog` payload exceeds MCP tool-result cap

| Field | Detail |
|--------|--------|
| Tool | Resource read: `roslyn://server/catalog` |
| Input | Standard `ReadMcpResourceTool` call, no filtering. |
| Expected | Inline response fits in normal MCP output budget. |
| Actual | Response is 68.5 KB and the client had to persist it to disk (`C:\Users\daryl\.claude\projects\...\tool-results\toolu_...txt`) before consumption. |
| Severity | Low — UX friction for small-context agents; no data loss. |
| Reproducibility | 100% at current surface size (142 tools + 10 resources + 20 prompts). |
| Suggested fix | Add `roslyn://server/catalog/tools`, `catalog/prompts`, `catalog/resources` URI templates (or accept `?supportTier=experimental&category=refactoring` query params) so agents can fetch only the slice they need. |

### 9.13 `revert_last_apply` does not delete files created by the reverted apply

| Field | Detail |
|--------|--------|
| Tool | `revert_last_apply` (experimental — undo family). |
| Input | Immediately after a failed `extract_type_apply` that created a new file (`SampleLib/AnimalSpeaker.cs`) and mutated an existing one (`AnimalService.cs`). `revert_last_apply` returned `reverted: true, revertedOperation: "Extract 1 member(s) from 'AnimalService' into new type 'AnimalSpeaker'"`. |
| Expected | Revert should undo **all** changes from the reverted operation, including file creation. After revert, `AnimalSpeaker.cs` should NOT exist on disk (or should be an empty sentinel flagged for cleanup). |
| Actual | `AnimalService.cs` was correctly restored (the injected `_animalSpeaker` field was removed, the moved method restored). But `SampleLib/AnimalSpeaker.cs` **remained on disk as an untracked file**. Subsequent `compile_check` passed, because `AnimalSpeaker` was now just a harmless unused class in the project, but the workspace was left in a state the agent did not ask for. |
| Severity | **Medium** — violates the implied revert contract. Agents relying on revert to return to the pre-apply state will have stale files that re-trigger analyzers, pollute `find_unused_symbols`, or leak into commits. |
| Reproducibility | 100% on this probe. |
| Evidence | `git status --short` post-revert showed `?? SampleLib/AnimalSpeaker.cs` still present; file content matched the `extract_type_apply` diff. |
| File(s) to edit | `src/RoslynMcp.Roslyn/Services/UndoService.cs` (or wherever `revert_last_apply` is implemented) — when the reverted operation includes a `create_file`-equivalent step, the revert must delete that file. Check `IMutationHistory` entry structure — it likely records text edits but not file creations. |

### 9.12 PostToolUse hook adds N+1 verification step per `*_apply`

| Field | Detail |
|--------|--------|
| Tool | Every `mcp__roslyn__*_apply` family member (`extract_interface_apply`, `extract_type_apply`, `move_type_to_file_apply`, `format_range_apply`, etc.). |
| Input | Any successful apply. |
| Expected | If compile_check is required, prefer the existing atomic pathway via `apply_with_verify` (v1.15+). |
| Actual | PostToolUse hook stops continuation with: `"A Roslyn refactoring (X_apply) was just applied to N files. Please run compile_check or build_workspace to verify no compilation errors were introduced."` — requires the agent to emit an explicit `compile_check` call before any other work. For an audit exercising 40 experimental tools this adds ~40 mandatory extra turns. |
| Severity | Low — policy working as designed, but friction is significant for long multi-refactor sessions. |
| Reproducibility | 100% on every successful `*_apply`. |
| Suggested fix | Either: (a) surface `apply_with_verify` as the default pattern in tool descriptions / `domains/tool-usage-guide.md` and relax the hook when the prior apply tool is `apply_with_verify`; or (b) bake compile_check into every `*_apply` response so the verification result is inline, eliminating the hook round-trip. |
| File(s) to edit | `hooks/hooks.json` PostToolUse matcher; `src/RoslynMcp.Roslyn/Services/*ApplyService.cs` if inlining. |

## 10. Improvement suggestions

Ranked by recurrence × fix cost (likely payoff first):

1. **`extract_type_preview` / `extract_type_apply` — consumer-safety** (re 9.1). Preview must enumerate external call sites of extracted members, even if it does not rewrite them, so agents can decide whether to manually update or abort. Minimum viable: emit a **warning** when extracting `public` members. Ideal: chain into `symbol_refactor_preview` so the apply rewrites consumers atomically (documented as the pattern in Phase 1d of `experimental-promotion-exercise.md`, but not implemented in the service path).
2. **`compile_check` — fail-loud on auto-reload race** (re 9.8). When the execution gate reports `staleAction=auto-reloaded` and the compilation pipeline has not yet resolved projects, the response must NOT claim `success=true` with zero projects. Return `success=false` + `"workspace just reloaded; retry compile_check"` instead.
3. **`fix_all_preview` — composition diagnosis + fallback** (re 9.2). Ship a `list_fixers(diagnosticId?)` companion so agents can see which `CodeFixProvider`s are actually wired up. The existing `guidanceMessage` is already actionable for IDE0005 (points at `organize_usings_preview`); extend the pattern to CA1822 (→ `apply_code_action` per-site or a `make_static_preview` composite) and CS0414 (→ `apply_text_edit`).
4. **PostToolUse hook — eliminate the N+1 verification turn** (re 9.12). Surface `apply_with_verify` as the default pattern in `domains/tool-usage-guide.md` **and** relax the hook when the preceding tool was `apply_with_verify`. Alternatively inline compile_check into the `*_apply` response shape.
5. **PreToolUse hook — trust the preview token** (re 9.4). Validate against `IPreviewStore` (authoritative) rather than transcript inspection (unreliable).
6. **`workspace_load` — surface restore drift proactively** (re 9.10). When `MetadataReference` resolution is incomplete, populate `restoreHint` on the load response itself, not only on a later `project_diagnostics` call.
7. **Formatting regressions** (re 9.5, 9.6, 9.7). All three are minor but produce noisy diffs that fight `.editorconfig`. Suggested owner: a shared "preserve-trivia" pass after `SyntaxFormatter` in the refactoring service base class.
8. **Catalog slicing URIs** (re 9.11). Add `roslyn://server/catalog/{tools|prompts|resources}` or accept `?supportTier=&category=` filters so agents can fetch a slice without busting the MCP output budget.
9. **`*_close` should skip stale-reload** (re 9.9). Minor perf/response-shape cleanup.
10. **`type_hierarchy`, `find_references`, etc. — honor `metadataName`** (re 9.3 + open backlog `find-references-metadataname-parameter-rejected`). Schema advertises the parameter; runtime rejects it on multiple tools. Either honor it everywhere (matching `find_references_bulk`) or remove it from the schema and document the discrepancy.

## 11. Known issue regression check

> Deferred — full Phase 11 not executed. The following backlog items were touched incidentally during Phase 0–1 and are worth noting for the next run:

| Source id | Summary | Status this run |
|-----------|---------|-----------------|
| `find-references-metadataname-parameter-rejected` | `find_references` / sibling discovery tools reject `metadataName`-only calls despite schema advertising support | **still reproduces** (new instance on `type_hierarchy` — see 9.3) |
| `change-signature-preview-callsite-summary` | `change_signature_preview` preview diff omits callsite files | **not reproduced** (Phase 1j not executed — would have been the direct test) |
| `roslyn-mcp-post-edit-validation-bundle` / `post-edit-validate-workspace-scoped-to-touched-files` | `validate_workspace` is the composite verify; PostToolUse hook does not use it | **related new finding 9.12** — hook requires plain `compile_check` rather than leveraging `apply_with_verify` / `validate_workspace` |
| `self-edit-bootstrap-mode-mcp-development` | Agents targeting the Roslyn MCP repo itself can't trust apply/preview | *(not applicable — SampleSolution workspace, not self-edit)* |

Full Phase 11 sweep deferred to next execution.

---

## Execution log (transient — removed at finalize)

### Phase 0 — setup and live surface baseline

- `server_info` 10 ms — server `1.18.0+172d3c5`, catalog `2026.04`, surface counts 102/40/9/1/0/20 (tools stable/exp, resources stable/exp, prompts stable/exp). PASS.
- `roslyn://server/catalog` 68.5 KB — 40 experimental tools, 1 experimental resource, 20 experimental prompts (matches appendix).
- `workspace_load` 2708 ms — SampleSolution.slnx, 3 projects, 23 docs, 0 workspace diagnostics, `isReady=true`. PASS.
- `workspace_status` implicit via `workspace_load` response (`isReady=true`, `isStale=false`).
- `project_graph` 2 ms — SampleApp (exe→SampleLib), SampleLib.Tests (lib→SampleLib), SampleLib (lib). Multi-project, test project present.
- `dotnet restore SampleSolution.slnx` — 3 projects restored.
- Repo shape recorded.
- Coverage ledger seeded (40+1+20 rows).
- Scorecard placeholder seeded.

Phase 0 checkpoint: **PASS** — `server_info`/catalog agree, `workspace_load` clean, restore OK, ledger seeded.

### Phase 1 — refactoring family (partial; paused for user decision)

- **1a `fix_all_preview`**: exercised with 5 distinct diagnostic IDs (CA1822, CA1861, IDE0005, CS8019, CS0414) × 2 scopes (solution, project). Every call returned `fixedCount=0, previewToken=""` with an actionable `guidanceMessage`. **FLAG — see 9.2.** Schema-wise the tool is responsive and error-path quality is PASS. Round-trip cannot be demonstrated here.
  - Coverage: `fix_all_preview` → exercised-preview-only; `fix_all_apply` → blocked (no preview token ever returned).
- **1b `format_range_preview` → `format_range_apply`**: PASS round-trip. `AnimalService.cs:16` (spaces inside parens) → 161 ms preview → 39 ms apply → compile_check clean. Non-default path (specific line range) exercised.
  - Coverage: `format_range_apply` → exercised-apply.
- **1c `extract_interface_preview` → `extract_interface_apply`**: PASS round-trip. `Dog → IDog` (3 members) → 24 ms preview → 480 ms apply → compile_check clean (0 errors, 0 warnings). Minor formatting nit in the preview diff (`, IDog` on its own line instead of inline with `IAnimal`), cosmetic only.
  - Coverage: `extract_interface_preview` / `extract_interface_apply` → exercised / exercised-apply.
  - Follow-on FLAG: `type_hierarchy(metadataName="SampleLib.IDog")` rejected with schema-advertised parameter — **see 9.3**.
- **1d `extract_type_preview` → `extract_type_apply`**: **FAIL round-trip.** See 9.1. Preview looked plausible; apply broke `SampleApp/Program.cs:6` (external consumer) and added an uninitialised field. `revert_last_apply` restored clean state.
  - Coverage: `extract_type_apply` → exercised-apply (with failure finding).
- **1e `move_type_to_file_preview` → `move_type_to_file_apply`**: PASS round-trip (second attempt). `TrulyUnusedConcreteType` moved from `ConventionFixtures.cs` to its own file, compile_check clean. First `apply` blocked by PreToolUse hook false-positive — **see 9.4** — recovered via re-preview + re-apply.
  - Coverage: `move_type_to_file_apply` → exercised-apply.
- **1f–1k (bulk_replace_type, extract_method, restructure, replace_string_literals, change_signature, symbol_refactor)**: **not yet exercised** — paused for user decision per 2026-04-15 check-in.

Phase 1 checkpoint: **PARTIAL FAIL** — 2/5 attempted round-trips succeeded cleanly; 1 produced actionable preview+env. guidance; 1 produced a genuine consumer-breaking server bug (9.1); 1 hit hook friction but completed on retry (9.4).
