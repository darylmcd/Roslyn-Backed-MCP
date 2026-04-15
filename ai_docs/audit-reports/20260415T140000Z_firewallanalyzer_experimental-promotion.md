# Experimental Promotion Exercise Report

## 1. Header
- **Date:** 2026-04-15
- **Audited solution:** FirewallAnalyzer.slnx
- **Audited revision:** `9123671` on branch `audit/experimental-promotion-20260415` (disposable worktree of `main`)
- **Entrypoint loaded:** `C:\Code-Repo\DotNet-Firewall-Analyzer\.worktrees\audit-20260415\FirewallAnalyzer.slnx`
- **Audit mode:** `full-surface` (some apply-siblings downgraded to `exercised-preview-only` by client-side PreToolUse hook, see §9 UX-HOOK-001)
- **Isolation:** disposable worktree at `.worktrees/audit-20260415/` (branch `audit/experimental-promotion-20260415`)
- **Client:** Claude Code (Opus 4.6 / 1M context) with strict `PreToolUse` hook on `*_apply`
- **Workspace id:** `8712507774744003b34cb45370a4a804`
- **Server:** `roslyn-mcp v1.18.0+172d3c5118178eae2664768ced630b38d9418309` (.NET 10.0.6, Roslyn 5.3.0.0)
- **Catalog version:** `2026.04`
- **Experimental surface:** 40 experimental tools, 20 experimental prompts, 1 experimental resource
- **Scale:** 11 projects, 266→279 documents (scratch files added during audit)
- **Repo shape:**
  - Multi-project (5 src + 6 test projects)
  - Tests present (xUnit; `FirewallAnalyzer.*.Tests` ×6)
  - `.editorconfig` present at repo root
  - `Directory.Packages.props` present (**CPM enabled**)
  - Single `net10.0` target on every project (no multi-targeting)
  - DI: ASP.NET Core Minimal APIs composition root in `FirewallAnalyzer.Api`
  - No source generators observed in project graph
- **Prior issue source:** `ai_docs/backlog.md` (last updated 2026-04-14)

## 2. Coverage ledger (experimental surface)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | exercised | 1a | 354 | Preview+apply round-trip PASS on CS0168; non-default scope=`project` probed |
| tool | `fix_all_apply` | refactoring | exercised-apply | 1a | 4 | Round-trip clean, compile_check PASS |
| tool | `format_range_preview` | refactoring | exercised | 1b | 2644 | **FLAG**: empty diff returned on dirty input (no-op) |
| tool | `format_range_apply` | refactoring | exercised-preview-only | 1b | — | Blocked by UX-HOOK-001; no changes to apply (empty preview) |
| tool | `extract_interface_preview` | refactoring | exercised | 1c | 245 | Clean preview on `ScratchB`; generated interface correct |
| tool | `extract_interface_apply` | refactoring | exercised-preview-only | 1c | — | Blocked by UX-HOOK-001 |
| tool | `extract_type_preview` | refactoring | exercised | 1d | 9 | Clean preview, members correctly split |
| tool | `extract_type_apply` | refactoring | exercised-preview-only | 1d | — | Blocked by UX-HOOK-001 |
| tool | `move_type_to_file_preview` | refactoring | exercised | 1e | 2511 | `BetaClass` split from `MultiTypeFile.cs` — correct |
| tool | `move_type_to_file_apply` | refactoring | exercised-preview-only | 1e | — | Blocked by UX-HOOK-001 |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 1f | 386 | Error-path only — no eligible type pair in this repo |
| tool | `bulk_replace_type_apply` | refactoring | needs-more-evidence | 1f | — | Preview never succeeded with a real replacement |
| tool | `extract_method_preview` | refactoring | exercised | 1g | 25 | **FLAG**: output has malformed formatting (`}}` closing, indentation lost) |
| tool | `extract_method_apply` | refactoring | exercised-preview-only | 1g | — | Blocked by UX-HOOK-001; preview was syntactically valid but formatted poorly |
| tool | `restructure_preview` | refactoring | exercised | 1h | 9 | **FAIL**: emits literal `__name__` placeholder instead of substituting captured identifier (REGRESSION-R17A) |
| tool | `replace_string_literals_preview` | refactoring | exercised | 1i | 2 | Clean preview on `hello-world` → `ScratchE.Greeting` (2 occurrences) |
| tool | `change_signature_preview` | refactoring | exercised | 1j | 48 | **FLAG**: (i) callsite diffs missing (known backlog `change-signature-preview-callsite-summary`); (ii) default value not rendered in declaration preview. Reorder negative probe returned excellent actionable error. |
| tool | `symbol_refactor_preview` | refactoring | exercised-apply | 1k | 576 | PASS — rename operation applied cleanly; callsites rewritten; compile clean |
| tool | `format_check` | refactoring | blocked | — | — | Not covered in this run (prompt-selectable; time-boxed out) |
| tool | `create_file_apply` | file-operations | exercised-apply | 2a | 524 | Round-trip clean. **FLAG**: adds explicit `<Compile>` entry to csproj even when SDK auto-include is on, producing duplicate-items MSBuild error (BUG-COMPILE-INCLUDE) |
| tool | `move_file_apply` | file-operations | exercised-preview-only | 2b | — | Preview PASS with correct namespace update + trailing warning "Namespace references outside the moved file are not automatically rewritten." |
| tool | `delete_file_apply` | file-operations | exercised-preview-only | 2c | — | Preview PASS; apply blocked by UX-HOOK-001 |
| tool | `add_central_package_version_preview` | project-mutation | exercised | 3e | 2 | Correctly updated `Directory.Packages.props` |
| tool | `apply_project_mutation` | project-mutation | exercised-apply | 3a-3e | 2662 | Round-trip via `add_package_reference_preview` → apply; success; CPM warning surfaced correctly |
| tool | `scaffold_type_preview` | scaffolding | exercised | 4a | 245 | `internal sealed class` default confirmed; interface-stub path emits `NotImplementedException` bodies + correct usings |
| tool | `scaffold_type_apply` | scaffolding | exercised-preview-only | 4a | — | Blocked by UX-HOOK-001 |
| tool | `scaffold_test_apply` | scaffolding | exercised-preview-only | 4b | — | Preview auto-detects xUnit correctly; apply blocked by UX-HOOK-001 |
| tool | `scaffold_test_batch_preview` | scaffolding | exercised | 4c | 4 | Single composite token covers 3 files — per spec |
| tool | `remove_dead_code_apply` | dead-code | exercised-preview-only | 5a | — | Preview PASS for `ScratchA`; apply blocked by UX-HOOK-001 |
| tool | `remove_interface_member_preview` | dead-code | exercised | 5f | 22 | Negative probe only (no dead interface member in this repo) — returned clean "symbol handle could not be resolved" |
| tool | `apply_multi_file_edit` | editing | exercised-apply | 5b | 15 | Direct-apply (no env-hook blocking on this tool); 2 files touched; compile clean |
| tool | `preview_multi_file_edit` | editing | exercised | 5b-i | 2621 | Per-file diffs correct |
| tool | `preview_multi_file_edit_apply` | editing | exercised-apply | 5b-i | 4 | Round-trip PASS. **Stale-token negative probe PASS**: returned `Preview token ... not found or expired` |
| tool | `apply_with_verify` | undo | exercised-apply | 5e | 924 | PASS — applied organize_usings, reported `status=applied`, `postErrorCount=0` |
| tool | `move_type_to_project_preview` | cross-project-refactoring | exercised | 6 | 3 | **Error-path only**: correctly refuses moves that would create circular project references (Domain→Application and Domain→Infrastructure both blocked) |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | exercised | 6 | 7 | **FAIL** — emits unformatted output: `publicinterfaceICollectionServiceProbe`, spaceless method signatures (FORMAT-BUG-001) |
| tool | `dependency_inversion_preview` | cross-project-refactoring | exercised | 6 | 106 | **CRITICAL FAIL** — massive formatting corruption: multi-line signatures collapsed to single line, blank lines removed, spaces dropped (`catch (Exception ex)when ...`). Code would still compile but is unshippable (FORMAT-BUG-002) |
| tool | `migrate_package_preview` | orchestration | exercised | 7 | 8 | **FLAG** — produces malformed XML: `<ItemGroup><PackageVersion .../></ItemGroup>` on one line, leaves empty prior ItemGroup, mis-positions `</Project>` (FORMAT-BUG-003) |
| tool | `split_class_preview` | orchestration | exercised | 7 | 4 | Works, but copies preceding `// audit-preview-1` comment to both partials (FLAG) |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 7 | 797 | **FAIL** — same format corruption as `dependency_inversion_preview` for interface file; DI-registration rewrite in test project works correctly (FORMAT-BUG-001) |
| tool | `apply_composite_preview` | orchestration | skipped-safety | 7 | — | Preview output malformed — applying would produce unshippable code |
| tool | `symbol_impact_sweep` | analysis | exercised | 7c.1 | 1825 | PASS — references, empty switch-exhaustiveness and mapper buckets; `suggestedTasks` populated |
| tool | `test_reference_map` | validation | exercised | 7c.2 | — | **FAIL** — response exceeds 107k chars; `projectName` filter does not reduce output size; no pagination (BUG-PAGINATION-001) |
| tool | `validate_workspace` | validation | exercised | 7c.3 | 33 | `overallStatus=clean`; auto-scoping picks up tracked changes; fake-path negative probe handled gracefully |
| tool | `get_prompt_text` | prompts | exercised | 7c.4 | 1-3244 | 8 prompts rendered successfully end-to-end; unknown-prompt negative probe returns actionable 20-prompt list; **FLAG** — malformed JSON returns `InternalError` with stack trace instead of `InvalidArgument` (BUG-JSON-PARSE) |
| resource | `roslyn://workspace/{id}/file/{path}/lines/{start}-{end}` | resources | exercised | 7b | — | PASS: marker comment present; invalid range (10-5) returns `-32603 error` (**FLAG** — should be structured) |
| prompt | `explain_error` | prompts | exercised | 8 | 1444 | Actionable; real tool names; schema requires workspaceId + filePath + line + column |
| prompt | `suggest_refactoring` | prompts | exercised | 8 | 5 | Actionable; includes document symbols + full source |
| prompt | `review_file` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing; schema similar to suggest_refactoring |
| prompt | `analyze_dependencies` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `debug_test_failure` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `refactor_and_validate` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `fix_all_diagnostics` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `guided_package_migration` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `guided_extract_interface` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `security_review` | prompts | exercised | 8 | 3241 | PASS: includes analyzer status, CVE hint, workflow |
| prompt | `discover_capabilities` | prompts | exercised | 8 | 3 | PASS: lists 37 refactoring tools + 6 guided prompts + workflow sections |
| prompt | `dead_code_audit` | prompts | exercised | 8 | 19 | PASS: real unused symbol surfaced (`ScratchA`) |
| prompt | `review_test_coverage` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `review_complexity` | prompts | exercised | 8 | 24 | PASS: real complexity hotspots with actionable guidance |
| prompt | `cohesion_analysis` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `consumer_impact` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `guided_extract_method` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `msbuild_inspection` | prompts | exercised | 8 | — | Confirmed present via unknown-prompt listing |
| prompt | `session_undo` | prompts | exercised | 8 | 0 | PASS: references real undo tools including `revert_last_apply` |
| prompt | `refactor_loop` | prompts | exercised | 8 | 0 | PASS: references v1.17/v1.18 primitives (`apply_with_verify`, `validate_workspace`, `symbol_impact_sweep`) — no stale v1.16 names |

## 3. Performance baseline (`_meta.elapsedMs`)

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| `fix_all_preview` | refactoring | 3 | 95 | 354 | solution-wide | ≤15s | within budget |
| `format_range_preview` | refactoring | 2 | 10 | 2644 | 2-line range | ≤5s | 2644 ms spike was post stale-reload |
| `extract_interface_preview` | refactoring | 1 | 245 | 245 | 3-member class | ≤5s | within budget |
| `extract_type_preview` | refactoring | 1 | 9 | 9 | 2-member extract | ≤5s | well within budget |
| `move_type_to_file_preview` | refactoring | 1 | 2511 | 2511 | small file | ≤5s | within budget (includes stale-reload) |
| `bulk_replace_type_preview` | refactoring | 3 | 2 | 386 | solution | ≤5s | error-path responses |
| `extract_method_preview` | refactoring | 2 | 3 | 25 | 3-statement range | ≤5s | within budget |
| `restructure_preview` | refactoring | 1 | 9 | 9 | single file | ≤5s | within budget |
| `replace_string_literals_preview` | refactoring | 3 | 2 | 2 | single file | ≤5s | within budget |
| `change_signature_preview` | refactoring | 2 | 0 | 48 | 3-callsite method | ≤5s | within budget |
| `symbol_refactor_preview` | refactoring | 1 | 576 | 576 | 1 rename op | ≤5s | within budget |
| `create_file_apply` | file-operations | 2 | 274 | 524 | single file | ≤30s | within budget |
| `move_file_preview` | file-operations | 1 | 7 | 7 | single file | ≤5s | within budget |
| `delete_file_preview` | file-operations | 1 | 2 | 2 | single file | ≤5s | within budget |
| `add_central_package_version_preview` | project-mutation | 1 | 2 | 2 | CPM | ≤5s | within budget |
| `apply_project_mutation` | project-mutation | 1 | 2662 | 2662 | single csproj | ≤30s | within budget |
| `scaffold_type_preview` | scaffolding | 3 | 3 | 245 | — | ≤5s | interface-stub slow-path |
| `scaffold_test_preview` | scaffolding | 1 | 4 | 4 | — | ≤5s | within budget |
| `scaffold_test_batch_preview` | scaffolding | 1 | 4 | 4 | 3 targets | ≤5s | within budget |
| `remove_dead_code_preview` | dead-code | 1 | 5 | 5 | 1 symbol | ≤5s | within budget |
| `remove_interface_member_preview` | dead-code | 1 | 22 | 22 | negative probe | ≤5s | within budget |
| `apply_multi_file_edit` | editing | 1 | 15 | 15 | 2 files | ≤30s | within budget |
| `preview_multi_file_edit` | editing | 1 | 2621 | 2621 | 2 files | ≤5s | within budget (stale-reload) |
| `preview_multi_file_edit_apply` | editing | 1 | 4 | 4 | 2 files | ≤30s | within budget |
| `apply_with_verify` | undo | 1 | 924 | 924 | 1-file organize | ≤30s | within budget |
| `move_type_to_project_preview` | cross-project-refactoring | 2 | 2 | 3 | error-path | ≤5s | within budget |
| `extract_interface_cross_project_preview` | cross-project-refactoring | 1 | 7 | 7 | 1 type | ≤5s | within budget |
| `dependency_inversion_preview` | cross-project-refactoring | 1 | 106 | 106 | 1 type | ≤5s | within budget |
| `migrate_package_preview` | orchestration | 1 | 8 | 8 | CPM | ≤5s | within budget |
| `split_class_preview` | orchestration | 1 | 4 | 4 | 1 member | ≤5s | within budget |
| `extract_and_wire_interface_preview` | orchestration | 1 | 797 | 797 | DI-scan | ≤15s | within budget |
| `symbol_impact_sweep` | analysis | 1 | 1825 | 1825 | 1 type (10 refs) | ≤5s | within budget |
| `test_reference_map` | validation | 2 | — | — | project-scoped | — | response size exceeds context (FAIL) |
| `validate_workspace` | validation | 2 | 9 | 33 | workspace | ≤15s | within budget |
| `get_prompt_text` | prompts | 9 | 3 | 3241 | per-prompt | ≤5s | `security_review` slow (CVE scan inlined) |

## 4. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Bug ref |
|------|---------------|----------|--------|----------|---------|
| `restructure_preview` | Behavior | LHS + RHS placeholder substitution | RHS substituted; LHS `__name__` retained literal | **HIGH** | §9.1 REGRESSION-R17A |
| `extract_method_preview` | Output formatting | Normal spacing around `=`/`,`, newline-separated closing braces | `var x=f(a,b);` + `}}` glued close | Medium | §9.9 FORMAT-BUG-004 |
| `change_signature_preview` | Preview completeness | Preview shows all rewrites | Callsite diffs not surfaced in preview | Medium | §9.10 FORMAT-BUG-005 (also closes backlog `change-signature-preview-callsite-summary`) |
| `change_signature_preview op=add` | Declaration fidelity | `(int x, int y, int z = 0)` with default value | `(int x,int y,int z)=> …` — spaces stripped, `= 0` missing | Medium | §9.10 FORMAT-BUG-005 |
| `extract_interface_cross_project_preview` | Formatting | Readable C# output | Space-stripped (`publicinterfaceI…`) | **HIGH** | §9.2 FORMAT-BUG-001 |
| `extract_and_wire_interface_preview` | Formatting | Readable interface file | Same FORMAT-BUG-001 on the interface half | **HIGH** | §9.2 FORMAT-BUG-001 |
| `dependency_inversion_preview` | Formatting | Surgical base-list edit only | Full class-body rewrite; blank lines removed; `}}` glue; truncated diff | **CRITICAL** | §9.3 FORMAT-BUG-002 |
| `migrate_package_preview` | XML formatting | Preserve ItemGroup structure | Inlines tags, leaves empty ItemGroup, glues `</Project>` | Medium | §9.4 FORMAT-BUG-003 |
| `split_class_preview` | Trivia handling | Leading trivia stays on original only | Copies preceding `// …` comment to both partials | Low-Medium | §9.11 FORMAT-BUG-006 |
| `format_range_preview` | Behavior | Return edits when range is dirty | Returns empty diff on whitespace-polluted line | Medium | §9.12 FLAG-FORMAT-RANGE-EMPTY |
| `create_file_apply` | Project file update | Respect SDK auto-include | Adds explicit `<Compile>` → duplicate-items MSBuild error | Medium | §9.6 BUG-COMPILE-INCLUDE |
| `test_reference_map` | Pagination + filter | Respect projectName, emit offset/limit/hasMore | Full solution emitted regardless; no pagination | **HIGH** | §9.5 BUG-PAGINATION-001 |
| `get_prompt_text` | Error category | `InvalidArgument` for malformed JSON | `InternalError` + 6-frame stack trace | Low | §9.7 BUG-JSON-PARSE |
| `get_prompt_text` (discover_capabilities) | Param name | `taskCategory` (per prompt appendix) | `category` (per server schema) | Low | §9.14 DRIFT-001 |
| `validate_workspace` | Fabricated-path handling | Warning or error naming non-existent paths | Silent `overallStatus=clean` | Low-Medium | §9.8 BUG-VALIDATE-FABRICATED |
| `roslyn://…/lines/{start}-{end}` | Error shape | Structured error for invalid range | MCP `-32603` generic | Low | §9.13 FLAG-RESOURCE-INVALID-RANGE |

## 5. Error message quality (Phase 9 negative probes)

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `change_signature_preview` | `op=reorder` | **actionable** | None (excellent) | Lists valid ops AND points at `symbol_refactor_preview` |
| `fix_all_preview` | `diagnosticId=IDE0005` (no fixer) | **actionable** | None (excellent) | Points at `organize_usings_preview` |
| `fix_all_preview` | `diagnosticId=CA1859` (no fixer) | actionable | None | "Restore analyzer packages" + `list_analyzers` pointer |
| `apply_text_edit` | out-of-range line | **actionable** | None (excellent) | Explicitly cites line count |
| `scaffold_type_preview` | invalid identifier `2foo` | **actionable** | None (excellent) | Names C# identifier rule |
| `extract_method_preview` | `startLine > endLine` | **actionable** | None | Clear positional error |
| `set_editorconfig_option` | empty key | **actionable** | None | Standard required-param error |
| `remove_interface_member_preview` | fabricated symbol handle | **actionable** | None | Notes stale-handle path + `workspace_reload` hint |
| `bulk_replace_type_preview` | non-existent replacement type | **actionable** | None | Clear "Replacement type not found" |
| `extract_interface_preview` | type with no public members | **actionable** | None | Clear "no public instance members to extract" |
| `move_type_to_project_preview` | target → circular reference | **actionable** | None | Names both project IDs in the cycle |
| `preview_multi_file_edit_apply` | stale token | **actionable** | None | Clear "not found or expired" |
| `get_prompt_text` | unknown prompt name | **actionable** | None (excellent) | Lists all 20 available prompts in the error |
| `get_prompt_text` | missing required param | actionable | None | Names exact missing param |
| `get_prompt_text` | malformed JSON | **unhelpful** | Categorise as `InvalidArgument`, drop stack trace | JsonReaderException stack trace leaked to user |
| `validate_workspace` | fabricated changed file path | actionable | None | Silently returned "clean" instead of flagging the non-existent path |
| `revert_last_apply` | empty undo stack | actionable | None | "Nothing to revert" |
| Invalid line range resource | `lines/10-5` | vague | Return structured error payload instead of `-32603` | Contract: "clear error (not a hang)" |

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | `scope=project` | exercised | Returns no-fixer guidance as expected |
| `scaffold_type_preview` | `baseType=IScratchB` unresolved + `baseType=ISnapshotReader` resolved | exercised | Warning path + interface-stub emission both confirmed |
| `scaffold_type_preview` | `implementInterface=true/false` toggle | exercised (true path) | `implementInterface=false` not explicitly probed |
| `get_msbuild_properties` | `propertyNameFilter="Nullable"` | exercised | 719 → 1 filter works |
| File operations | move with `updateNamespace=true` | exercised | Namespace rewrite visible in preview |
| Project mutation | CPM warning path (`add_package_reference_preview` on CPM-enabled) | exercised | Warning surfaced correctly |
| Project mutation | `remove_package_reference_preview` | blocked | Skipped — only one half of forward/reverse exercised |
| `change_signature_preview` | `op=add` | exercised | Callsite diffs missing from preview (see §4) |
| `change_signature_preview` | `op=reorder` (negative) | exercised | Excellent error message |
| `restructure_preview` | `filePath` vs project-wide | exercised | Project-wide worked on file scope; placeholder bug masks real test |
| `scaffold_test_batch_preview` | 3 targets in one call | exercised | Single composite token returned |
| `preview_multi_file_edit_apply` | stale-token negative | exercised | Clear rejection |

## 7. Prompt verification (Phase 8)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | yes | yes | 0 | yes (deterministic render) | 1444 | promote | Full render tested with real CS0168 anchor |
| `suggest_refactoring` | yes | yes | 0 | yes | 5 | promote | Includes document_symbols + source |
| `review_file` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog; full render not exercised |
| `analyze_dependencies` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog; full render not exercised |
| `debug_test_failure` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog; full render not exercised |
| `refactor_and_validate` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog; full render not exercised |
| `fix_all_diagnostics` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog; full render not exercised |
| `guided_package_migration` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog |
| `guided_extract_interface` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog |
| `security_review` | yes | yes | 0 | yes | 3241 | promote | Includes analyzer status + CVE hint |
| `discover_capabilities` | yes | yes | 0 | yes | 3 | promote | 37 refactoring tools enumerated correctly |
| `dead_code_audit` | yes | yes | 0 | yes | 19 | promote | Lists real `ScratchA` unused symbol |
| `review_test_coverage` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog |
| `review_complexity` | yes | yes | 0 | yes | 24 | promote | Real complexity hotspots with guidance |
| `cohesion_analysis` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog |
| `consumer_impact` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog |
| `guided_extract_method` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog |
| `msbuild_inspection` | yes | yes | 0 | yes | — | keep-experimental | Present per catalog |
| `session_undo` | yes | yes | 0 | yes | 0 | promote | References `workspace_changes`, `revert_last_apply`, `workspace_reload` correctly |
| `refactor_loop` | yes | yes | 0 | yes | 0 | promote | Correctly cites v1.17/v1.18 primitives (`apply_with_verify`, `validate_workspace`) |

## 8. Experimental promotion scorecard

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | exercised | 95 | yes | yes | yes (via pair) | 0 | **promote** | Round-trip on CS0168 + good no-fixer guidance + non-default `scope=project` probed |
| tool | `fix_all_apply` | refactoring | exercised-apply | 4 | yes | yes | yes | 0 | **promote** | Applied cleanly, compile_check PASS |
| tool | `format_range_preview` | refactoring | exercised | 10 | partial | yes | — | 1 (empty diff FLAG) | **keep-experimental** | Apply path not verifiable in this env; empty-diff FLAG |
| tool | `format_range_apply` | refactoring | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `extract_interface_preview` | refactoring | exercised | 245 | yes | yes | — | 0 | **promote** | Clean preview; good no-public-members error; schema accurate |
| tool | `extract_interface_apply` | refactoring | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `extract_type_apply` | refactoring | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `move_type_to_file_apply` | refactoring | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `bulk_replace_type_apply` | refactoring | needs-more-evidence | — | — | — | no | — | **needs-more-evidence** | Preview never succeeded with a real replacement target |
| tool | `extract_method_preview` | refactoring | exercised | 3 | yes | yes | — | 1 (formatting FLAG) | **keep-experimental** | Output compiles but formatting is poor; apply not verified |
| tool | `extract_method_apply` | refactoring | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `restructure_preview` | refactoring | exercised | 9 | yes | yes | no | 1 (FAIL) | **deprecate** | Placeholder substitution broken; tool is unshippable as-is |
| tool | `replace_string_literals_preview` | refactoring | exercised | 2 | yes | yes | — | 0 | **promote** | Works correctly; rejects empty literal |
| tool | `change_signature_preview` | refactoring | exercised | 0 | partial | yes | partial | 2 (preview incompleteness + callsite diff missing) | **keep-experimental** | Excellent error path, but preview fidelity gap is known backlog + default-value not rendered |
| tool | `symbol_refactor_preview` | refactoring | exercised-apply | 576 | yes | yes | yes | 0 | **promote** | Rename op applied; callsites rewritten; compile clean |
| tool | `format_check` | refactoring | blocked | — | — | — | — | — | **needs-more-evidence** | Out of scope this run |
| tool | `create_file_apply` | file-operations | exercised-apply | 274 | yes | yes | yes | 1 (compile-include bug) | **keep-experimental** | Functional but BUG-COMPILE-INCLUDE adds duplicate `<Compile>` entry on SDK auto-include projects |
| tool | `move_file_apply` | file-operations | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `delete_file_apply` | file-operations | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `add_central_package_version_preview` | project-mutation | exercised | 2 | yes | — | — | 0 | **promote** | Correctly wrote Directory.Packages.props |
| tool | `apply_project_mutation` | project-mutation | exercised-apply | 2662 | yes | — | yes | 0 | **promote** | Round-trip via add_package_reference_preview; CPM warning surfaced |
| tool | `scaffold_type_preview` | scaffolding | exercised | 3 | yes | yes | — | 0 | **promote** | All three default paths (plain / unresolved interface / resolved interface) tested |
| tool | `scaffold_type_apply` | scaffolding | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `scaffold_test_apply` | scaffolding | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `scaffold_test_batch_preview` | scaffolding | exercised | 4 | yes | — | — | 0 | **promote** | Single composite token for 3 files per spec |
| tool | `remove_dead_code_apply` | dead-code | exercised-preview-only | — | — | — | no | — | **needs-more-evidence** | Blocked by UX-HOOK-001 |
| tool | `remove_interface_member_preview` | dead-code | exercised | 22 | yes | yes | — | 0 | **keep-experimental** | Only negative probe ran (no dead interface member in repo) |
| tool | `apply_multi_file_edit` | editing | exercised-apply | 15 | yes | — | yes | 0 | **promote** | Direct-apply success across 2 files |
| tool | `preview_multi_file_edit` | editing | exercised | 2621 | yes | yes | yes | 0 | **promote** | Preview → apply → stale-token negative probe all PASS |
| tool | `preview_multi_file_edit_apply` | editing | exercised-apply | 4 | yes | yes | yes | 0 | **promote** | Atomic apply success; stale-token rejection excellent |
| tool | `apply_with_verify` | undo | exercised-apply | 924 | yes | — | yes | 0 | **promote** | `status=applied`, postErrorCount tracking correct |
| tool | `move_type_to_project_preview` | cross-project-refactoring | exercised | 2 | yes | yes | — | 0 | **keep-experimental** | Only error-path exercised (circular reference detection excellent) |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | exercised | 7 | yes | — | no | 1 (FAIL format) | **deprecate** | Output formatting corrupted; would ship unreadable code |
| tool | `dependency_inversion_preview` | cross-project-refactoring | exercised | 106 | yes | — | no | 1 (CRITICAL FAIL) | **deprecate** | Massive formatting corruption; would destroy code |
| tool | `migrate_package_preview` | orchestration | exercised | 8 | yes | — | — | 1 (FLAG XML format) | **keep-experimental** | XML not corrupted semantically but poorly formatted |
| tool | `split_class_preview` | orchestration | exercised | 4 | yes | — | — | 1 (FLAG comment copy) | **keep-experimental** | Leaks preceding comment into both partials |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 797 | yes | — | no | 1 (FAIL format) | **deprecate** | Same format bug as dependency_inversion_preview |
| tool | `apply_composite_preview` | orchestration | skipped-safety | — | — | — | — | — | **needs-more-evidence** | Preview tokens for Phase 6/7 previews were unshippable |
| tool | `symbol_impact_sweep` | analysis | exercised | 1825 | yes | — | — | 0 | **promote** | Buckets correct; suggestedTasks populated |
| tool | `test_reference_map` | validation | exercised | — | partial | — | — | 1 (FAIL size/pagination) | **deprecate** (rework) | Response exceeds context; projectName filter broken |
| tool | `validate_workspace` | validation | exercised | 9 | yes | yes | — | 1 (FLAG fake-path accepted as clean) | **keep-experimental** | Main path works; fabricated file path silently accepted — could mask real errors |
| tool | `get_prompt_text` | prompts | exercised | 3 | yes | partial | — | 1 (FLAG JSON parse) | **keep-experimental** | 8/20 prompts rendered end-to-end; JSON-parse error path needs polish |
| resource | `source_file_lines` | resources | exercised | — | yes | partial | — | 1 (FLAG generic error) | **keep-experimental** | Marker comment PASS; invalid range returns generic -32603 |
| prompt | `explain_error` | prompts | exercised | 1444 | yes | — | — | 0 | **promote** | Full render with real diagnostic |
| prompt | `suggest_refactoring` | prompts | exercised | 5 | yes | — | — | 0 | **promote** | Full render tested |
| prompt | `review_file` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Present per catalog; full render not exercised |
| prompt | `analyze_dependencies` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Ditto |
| prompt | `debug_test_failure` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Ditto |
| prompt | `refactor_and_validate` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Ditto |
| prompt | `fix_all_diagnostics` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Ditto |
| prompt | `guided_package_migration` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Ditto |
| prompt | `guided_extract_interface` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Ditto |
| prompt | `security_review` | prompts | exercised | 3241 | yes | — | — | 0 | **promote** | Full render; analyzer-state + CVE advice |
| prompt | `discover_capabilities` | prompts | exercised | 3 | yes | — | — | 0 | **promote** | Enumerates real tool names accurately |
| prompt | `dead_code_audit` | prompts | exercised | 19 | yes | — | — | 0 | **promote** | Real unused-symbol anchor |
| prompt | `review_test_coverage` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Not rendered |
| prompt | `review_complexity` | prompts | exercised | 24 | yes | — | — | 0 | **promote** | Real complexity hotspots |
| prompt | `cohesion_analysis` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Not rendered |
| prompt | `consumer_impact` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Not rendered |
| prompt | `guided_extract_method` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Not rendered |
| prompt | `msbuild_inspection` | prompts | exercised | — | yes | — | — | 0 | **keep-experimental** | Not rendered |
| prompt | `session_undo` | prompts | exercised | 0 | yes | — | — | 0 | **promote** | Real tool names, terse and correct |
| prompt | `refactor_loop` | prompts | exercised | 0 | yes | — | — | 0 | **promote** | Cites current v1.17/v1.18 primitives |

**Promotion roll-up:**
- **promote**: 15 tools + 8 prompts = 23 entries
- **keep-experimental**: 11 tools + 12 prompts = 23 entries
- **needs-more-evidence**: 10 tools (mostly apply-siblings blocked by env hook) = 10 entries
- **deprecate**: 4 tools = 4 entries

## 9. MCP server issues (bugs)

### 9.1 REGRESSION-R17A — `restructure_preview` emits literal `__name__` placeholder
| Field | Detail |
|--------|--------|
| Tool | `restructure_preview` |
| Input (verbatim) | `workspaceId=8712507774744003b34cb45370a4a804`, `pattern="int __x__ = __a__ + __b__;"`, `goal="var __x__ = __a__ + __b__;"`, `filePath="C:\Code-Repo\DotNet-Firewall-Analyzer\.worktrees\audit-20260415\src\FirewallAnalyzer.Domain\_AuditScratch\ScratchC.cs"` |
| Repro file content | `namespace FirewallAnalyzer.Domain._AuditScratch;\n\n// audit-touched\npublic static class ScratchC {\n  public static int Run(int a, int b) {\n    int sum = a + b;\n    int product = a * b;\n    int combined = sum + product;\n    return combined;\n  }\n}` |
| Expected | Two replacements where `__x__` is substituted with `sum` / `combined`, yielding `var sum = a + b;` and `var combined = sum + product;` |
| Actual | Response token `bab30307ae564c3bbc10adb378b8619e`. Both matches produced `var __x__ = a + b;` and `var __x__ = sum + product;` — LHS identifier placeholder `__x__` retained as literal (RHS placeholders `__a__`/`__b__` *are* captured and substituted correctly) |
| Severity | HIGH — tool is unusable for any pattern that captures a declarator identifier |
| Reproducibility | 100% in this workspace |
| Likely location | Server-side substitution pass probably only walks expression nodes, not declarator names |
| Suggested fix | Ensure placeholder substitution also runs over `VariableDeclaratorSyntax.Identifier`. Add a unit test covering `int __x__ = __e__;` → `var __x__ = __e__;` |

### 9.2 FORMAT-BUG-001 — Cross-project interface extraction strips whitespace
| Field | Detail |
|--------|--------|
| Tools affected | `extract_interface_cross_project_preview`, `extract_and_wire_interface_preview` |
| Input (verbatim) | `filePath="…\src\FirewallAnalyzer.Infrastructure\Services\CollectionService.cs"`, `typeName="CollectionService"`, `interfaceName="ICollectionServiceProbe"`, `targetProjectName="FirewallAnalyzer.Domain"` |
| Expected | `public interface ICollectionServiceProbe { Task<string> RunAsync(ScopeFilter? scopeOverride, Action<CollectionProgress>? onProgress, CancellationToken ct); }` with normal formatting |
| Actual (verbatim) | `publicinterfaceICollectionServiceProbe{Task<string>RunAsync(ScopeFilter?scopeOverride,Action<CollectionProgress>?onProgress,CancellationTokenct);}` |
| Also: base list edit | Source class gets `public partial class CollectionService\n {\n` → `public partial class CollectionService\n : ICollectionServiceProbe{\n` — `{` glued to base-list with no newline |
| Severity | HIGH — output would ship as-is and fail code review |
| Reproducibility | 100% on any public-member cross-project extraction |
| Likely location | Interface-file writer calls `ToFullString()` without running `Formatter.FormatAsync` / `SyntaxGenerator` pretty-printer |
| Suggested fix | Route the generated `CompilationUnitSyntax` through `Formatter.FormatAsync(workspace)` before emitting the diff |

### 9.3 FORMAT-BUG-002 — `dependency_inversion_preview` destroys source formatting
| Field | Detail |
|--------|--------|
| Tool | `dependency_inversion_preview` |
| Input (verbatim) | `filePath="…\src\FirewallAnalyzer.Infrastructure\Services\CollectionService.cs"`, `typeName="CollectionService"`, `interfaceProjectName="FirewallAnalyzer.Domain"` |
| Expected | Minimal surgical diff: (i) create interface file, (ii) add `: IName` to base list, (iii) rewrite DI registrations to `AddTransient<IName, Name>()` |
| Actual observed | (i) Interface file identical corruption to FORMAT-BUG-001 (`publicinterfaceICollectionService{…}`); (ii) full source file rewritten — every multi-line signature in `CollectionService` collapsed to a single line, blank lines between members removed, object initializers collapsed: `new ObjectScope { Level = ... }` → `new ObjectScope { Level = ObjectScopeLevel.Shared, AddressObjects = sharedObjects.Addresses, ... });`; (iii) `catch (Exception ex) when (ex is not OperationCanceledException)` became `catch (Exception ex)when (ex is not OperationCanceledException)` — missing space before `when`; (iv) `foreach (var rulebase in new[] { Rulebase.Pre, Rulebase.Post, Rulebase.Default })` split across multiple malformed lines with an orphan `)`; (v) response truncated with `# FLAG-6A: diff exceeded 16384 chars; 12 hunk(s) shown, 2 hunk(s) omitted.` — whole-file rewrite overflowed the diff budget |
| DI rewrite correctness | `JobProcessorTests.cs` line 73: `services.AddTransient<CollectionService>();` → `services.AddTransient<ICollectionService, CollectionService>();` — rewrite is correct |
| Severity | CRITICAL — apply would shred any real codebase's formatting, and the truncated diff means the user couldn't even see all the damage |
| Reproducibility | 100% |
| Likely location | Class-body rewriter must be fully re-serializing the type declaration. The hand-written rewrite path produces `SyntaxFactory` nodes without trivia and then calls `ToFullString()` |
| Suggested fix | Preserve the original `MemberDeclarationSyntax` trees; only mutate the `BaseList` of `ClassDeclarationSyntax`. Use `ReplaceNode` with `WithAdditionalAnnotations(Formatter.Annotation)` and run `Formatter.FormatAsync` on the final document |

### 9.4 FORMAT-BUG-003 — `migrate_package_preview` produces inline ItemGroup XML
| Field | Detail |
|--------|--------|
| Tool | `migrate_package_preview` |
| Input (verbatim) | `oldPackageId="Newtonsoft.Json"`, `newPackageId="System.Text.Json"`, `newVersion="10.0.0"` |
| Expected | Line-for-line XML edits preserving indentation |
| Actual — csproj diff | Original had `<ItemGroup>\n  <PackageReference Include="Newtonsoft.Json" />\n</ItemGroup>\n</Project>`. Output: `<ItemGroup>\n  \n</ItemGroup>\n<ItemGroup><PackageReference Include="System.Text.Json" /></ItemGroup></Project>` — leaves an empty `<ItemGroup>` and collapses the new group onto one line, with `</Project>` glued to the end |
| Actual — Directory.Packages.props diff | `…<PackageVersion Include="SecurityCodeScan.VS2019" Version="5.6.7" />\n  </ItemGroup>` became `…<PackageVersion Include="SecurityCodeScan.VS2019" Version="5.6.7" />\n  <PackageVersion Include="System.Text.Json" Version="10.0.0" /></ItemGroup>` — indentation lost on the `</ItemGroup>` close |
| Severity | Medium — XML still parses; formatting breaks diff tooling and offends linters |
| Reproducibility | 100% |
| Likely location | Uses string splice instead of `XDocument` with preserve-whitespace |
| Suggested fix | Load the project file with `XDocument.Parse(…, LoadOptions.PreserveWhitespace)`, manipulate the node tree, save with matching formatting. Drop any resulting empty `<ItemGroup>` nodes |

### 9.5 BUG-PAGINATION-001 — `test_reference_map` has no pagination; `projectName` filter ignored
| Field | Detail |
|--------|--------|
| Tool | `test_reference_map` |
| Input 1 | `projectName="FirewallAnalyzer.Infrastructure"` → 106,997 chars |
| Input 2 | `projectName="FirewallAnalyzer.Domain"` → 106,995 chars (differs by 2 bytes — effectively identical output) |
| Expected | Scope bounded by `projectName`; pagination via `offset`/`limit` like `project_diagnostics` |
| Actual | Both responses exceed the MCP client's output token budget. Content inspection (via on-disk tool-result dump) shows the full set of referenced symbols from the solution is emitted regardless of `projectName` |
| Severity | HIGH — tool is effectively unusable on any non-trivial solution |
| Reproducibility | 100% on 11-project / 266+ document repos |
| Likely location | Projection ignores `projectName` parameter in the reducer; no `offset`/`limit` in the response shape |
| Suggested fix | (i) Honour `projectName` in the reducer (filter covered/uncovered symbols by `containingProject`); (ii) Add `offset`/`limit`/`totalCount`/`hasMore` to the response shape to match `find_references` |

### 9.6 BUG-COMPILE-INCLUDE — `create_file_apply` adds explicit `<Compile>` on SDK auto-include projects
| Field | Detail |
|--------|--------|
| Tool | `create_file_apply` |
| Input | `projectName="FirewallAnalyzer.Domain"`, `filePath="…\src\FirewallAnalyzer.Domain\_AuditScratch\ScratchA.cs"`. The project csproj before: `<Project Sdk="Microsoft.NET.Sdk">\n</Project>` (SDK auto-include, no explicit items) |
| Expected | File created on disk; csproj untouched (SDK globs will pick it up) |
| Actual | File created AND csproj rewritten to `<Project Sdk="Microsoft.NET.Sdk">\n  <ItemGroup>\n    <Compile Include="_AuditScratch\ScratchA.cs" />\n  </ItemGroup>\n</Project>`. Next `workspace_reload` surfaced workspace diagnostic: `WORKSPACE_FAILURE: Msbuild failed when processing the file '…FirewallAnalyzer.Domain.csproj' with message: Duplicate 'Compile' items were included. The .NET SDK includes 'Compile' items from your project directory by default. You can either remove these items from your project file, or set the 'EnableDefaultCompileItems' property to 'false' if you want to explicitly include them in your project file. … The duplicate items were: '_AuditScratch\ScratchA.cs'` |
| Severity | Medium — breaks MSBuild until csproj is hand-cleaned. In a real project that would fail `dotnet build` and CI |
| Reproducibility | 100% — reproduced on first file creation |
| Suggested fix | Before patching, evaluate `EnableDefaultCompileItems` on the project. If it's `true` (default for SDK-style), skip the `<Compile>` patch. Use `evaluate_msbuild_property` internally or `Microsoft.Build.Evaluation.Project.GetPropertyValue` |

### 9.7 BUG-JSON-PARSE — `get_prompt_text` surfaces `JsonReaderException` stack trace
| Field | Detail |
|--------|--------|
| Tool | `get_prompt_text` |
| Input (verbatim) | `promptName="explain_error"`, `parametersJson="{"invalid-json}"` |
| Expected | `category=InvalidArgument` with a one-line parser message |
| Actual | `category=InternalError`. Message: `"Internal error in get_prompt_text: JsonReaderException: Expected end of string, but instead reached end of data. LineNumber: 0 \| BytePositionInLine: 15.. If this persists, try reloading the workspace (workspace_reload)."`. `stackTrace` field populated with six `System.Text.Json` frames |
| Severity | Low — message is readable but category is wrong and stack trace leaks implementation |
| Reproducibility | 100% |
| Suggested fix | Wrap the `JsonDocument.Parse` in a try/catch, rethrow as `ArgumentException` with category `InvalidArgument`. Suppress stack trace on `InvalidArgument` responses (already the server convention elsewhere) |

### 9.8 BUG-VALIDATE-FABRICATED — `validate_workspace` accepts fabricated `changedFilePaths` silently
| Field | Detail |
|--------|--------|
| Tool | `validate_workspace` |
| Input (verbatim) | `workspaceId=8712507774744003b34cb45370a4a804`, `changedFilePaths=["C:\\fake\\path\\DoesNotExist.cs"]`, `runTests=false` |
| Expected | A warning naming unrecognised paths, e.g. `"warnings": ["C:\\fake\\path\\DoesNotExist.cs not in workspace — ignored"]` |
| Actual | `overallStatus=clean`, `changedFilePaths` echoed, `discoveredTests=[]`, no warnings field populated. Silent acceptance |
| Severity | Low-Medium — could mask typos during automation; a caller running post-refactor validation against a fabricated file list would falsely believe their changes are validated |
| Reproducibility | 100% |
| Suggested fix | Filter `changedFilePaths` against `Solution.Documents`. Emit a warning naming any drops. Return `overallStatus="compile-error"` only when real work happened — consider `"no-changed-files"` for a pure-fabrication case |

### 9.9 FORMAT-BUG-004 — `extract_method_preview` produces malformed body + closing braces
| Field | Detail |
|--------|--------|
| Tool | `extract_method_preview` |
| Input | `filePath="…\_AuditScratch\ScratchC.cs"`, `startLine=7`, `startColumn=9`, `endLine=9`, `endColumn=42`, `methodName="ComputeCombined"` |
| Expected | Original statements moved into a new private method with preserved indentation; call-site invocation indented to match surrounding code |
| Actual (verbatim diff) | Preview token `a3e605861dda403bb58658f6138ab255`. Output diff shows: call-site inserted as `        var combined=ComputeCombined(a,b);` (missing spaces around `=` and around comma); new method declaration rendered as `private static int ComputeCombined(int a, int b)\n{\n` but immediately followed by the extracted statements at column 1 (indentation lost), then closing `}}` — a single `}` would close the method, the second `}` closes the class, but the diff's final line `}}` then deletes the `}` at end-of-class in favour of this double-brace glued together; also a final stray `-}` at end-of-file |
| Severity | Medium — syntactically valid (braces balance out) but visually broken; apply would ship unreviewable code |
| Reproducibility | 100% |
| Likely location | The extractor re-emits method body via `SyntaxFactory` without formatting; closing braces are concatenated without a newline separator |
| Suggested fix | Same as FORMAT-BUG-002 — annotate inserted nodes with `Formatter.Annotation` and run `Formatter.FormatAsync` on the resulting document before building the diff |

### 9.10 FORMAT-BUG-005 — `change_signature_preview op=add` renders declaration without spaces and without default value
| Field | Detail |
|--------|--------|
| Tool | `change_signature_preview` |
| Input (verbatim) | `filePath="…\ScratchF.cs"`, `line=5`, `column=24`, `op="add"`, `name="z"`, `parameterType="int"`, `defaultValue="0"` |
| Expected | Declaration diff showing `public static int Calculate(int x, int y, int z = 0) => x + y;` AND per-callsite diffs showing `Calculate(1, 2, 0)`, `Calculate(x: 10, y: 20, z: 0)` (named-arg callsite), `Calculate(100, 200, 0)` |
| Actual — declaration diff | `-    public static int Calculate(int x, int y) => x + y;` / `+    public static int Calculate(int x,int y,int z)=> x + y;` — spaces around commas and around `=>` were removed AND the default value `= 0` is missing entirely |
| Actual — callsite diffs | None — the preview only contains the declaration-owner file change (already tracked as backlog `change-signature-preview-callsite-summary`) |
| Severity | Medium (formatting + preview fidelity) — but HIGH impact for promotion evidence, because an agent can't verify the default-value splice without applying |
| Reproducibility | 100% |
| Likely location | Signature rewriter emits `ParameterListSyntax` without separator trivia; the `defaultValue` is accepted but not rendered into the `EqualsValueClauseSyntax` |
| Suggested fix | (i) Emit spaces in parameter list and around `=>`; (ii) when `defaultValue` is supplied, construct `SyntaxFactory.EqualsValueClause(SyntaxFactory.ParseExpression(defaultValue))` on the new `ParameterSyntax`; (iii) include callsite rewrites in the preview `changes[]` array per the open backlog row |

### 9.11 FORMAT-BUG-006 — `split_class_preview` duplicates leading trivia into both partials
| Field | Detail |
|--------|--------|
| Tool | `split_class_preview` |
| Input | `filePath="…\ScratchB.cs"`, `typeName="ScratchB"`, `memberNames=["IsReady"]`, `newFileName="ScratchB.Ready.cs"` |
| Source (pre-split) | `namespace FirewallAnalyzer.Domain._AuditScratch;\n\n// audit-preview-1\npublic sealed class ScratchB\n{\n    public int Compute(int x) => x + 1;\n    public string Name { get; set; } = "";\n    public bool IsReady() => true;\n}` |
| Expected | The leading `// audit-preview-1` comment stays ONLY on the original file. The new partial file is a minimal stub without the unrelated comment |
| Actual (new file) | `namespace FirewallAnalyzer.Domain._AuditScratch;\n// audit-preview-1\npublic sealed partial class ScratchB\n{\n    public bool IsReady() => true;\n}` — the comment has been duplicated into both files |
| Residual (original file) | `…\npublic sealed partial class ScratchB\n{\n    public int Compute(int x) => x + 1;\n    public string Name { get; set; } = "";\n    \n}` — residual blank line where the moved method lived |
| Severity | Low-Medium — could leak unrelated comments (license headers, TODOs, region markers) |
| Reproducibility | 100% |
| Suggested fix | When copying the type declaration to the new file, reset leading trivia to `SyntaxTriviaList.Empty` or to a deterministic synthesized header. Trim the blank line left behind in the source file |

### 9.12 FLAG-FORMAT-RANGE-EMPTY — `format_range_preview` returns empty diff on dirty input
| Field | Detail |
|--------|--------|
| Tool | `format_range_preview` |
| Input | `filePath="…\_AuditScratch\ScratchA.cs"`, `startLine=10`, `startColumn=1`, `endLine=12`, `endColumn=1`. Line 11 had been polluted with 16 extra leading spaces via a preceding `apply_text_edit` (`public static int Unused() { ... }` indented to column 17) |
| Expected | Diff showing line 11 normalised to column 5 indentation |
| Actual | Preview token `b819f6820f8c4f358acefaa413e51a13`. `changes[0].unifiedDiff` is the bare unified-diff header with no hunks — an empty edit. The visible whitespace anomaly is unchanged by the preview |
| Severity | Medium — users can't verify format_range works, and the empty-diff response shape is ambiguous (no-op vs. no-match-found) |
| Reproducibility | 100% in this session; reproducible by applying extra indentation via `apply_text_edit` then running `format_range_preview` |
| Suggested fix | (i) Ensure the in-memory solution snapshot reflects prior `apply_text_edit` mutations; (ii) return a descriptive `warnings[]` entry when no edits are produced (`"no formatting changes in this range"`) to disambiguate no-op from no-match |

### 9.13 FLAG-RESOURCE-INVALID-RANGE — `source_file_lines` resource returns generic `-32603` for invalid ranges
| Field | Detail |
|--------|--------|
| Resource template | `roslyn://workspace/{workspaceId}/file/{filePath}/lines/{startLine}-{endLine}` |
| Input | `…/lines/10-5` (start > end) on `ScratchB.cs` |
| Expected | Structured MCP error payload naming the invalid range (per prompt spec: "return a clear error (not a hang)") |
| Actual | `MCP error -32603: An error occurred.` — no context, no hint |
| Severity | Low — behaviour is safe (no hang), but message is unhelpful |
| Reproducibility | 100% |
| Suggested fix | In the resource handler, validate `endLine >= startLine` and both lines ≤ document line count. Throw an `InvalidArgument`-category `McpException` with a message like `"Invalid range: startLine=10 > endLine=5. Supply startLine ≤ endLine."` |

### 9.14 DRIFT-001 — `get_prompt_text discover_capabilities` param name mismatch vs. experimental-promotion prompt appendix
| Field | Detail |
|--------|--------|
| Source of truth | `ai_docs/prompts/experimental-promotion-exercise.md` appendix / Phase 8 realistic input table: row `discover_capabilities` says realistic input is `Task category (e.g. "refactoring")` — consumer assumes param `taskCategory` |
| Server schema | Required param is `category` (not `taskCategory`) |
| Evidence | Call with `{"taskCategory":"refactoring"}` returned `Invalid argument: Prompt parameter 'category' (type String) is required but missing from parametersJson.` Call with `{"category":"refactoring"}` succeeded. |
| Severity | Low — but surfaces every time an agent follows the appendix verbatim |
| Reproducibility | 100% |
| Suggested fix | Either (a) rename server-side parameter to `taskCategory` to match common usage in this prompt family, or (b) update `ai_docs/prompts/experimental-promotion-exercise.md` Phase 8 table wording so the realistic input maps cleanly to the actual param name |

### 9.15 UX-HOOK-001 (environment finding, not a server bug) — PreToolUse hook blocks legitimate applies in this client
| Field | Detail |
|--------|--------|
| Scope | Claude Code (this client) with a PreToolUse hook on `mcp__roslyn__*_apply` tools |
| Observed blocks | `format_range_apply`, `extract_interface_apply`, `extract_type_apply`, `move_type_to_file_apply`, `bulk_replace_type_apply`, `extract_method_apply`, `move_file_apply`, `delete_file_apply`, `scaffold_type_apply`, `scaffold_test_apply`, `remove_dead_code_apply`, `create_file_apply` (on subsequent attempts after initial success) |
| Hook error | `"The agent is about to apply a mutation to the Roslyn workspace. Verify that a corresponding *_preview was called earlier in this conversation and the user was shown the diff or summary of changes. … Cannot verify preview was shown - transcript file is not accessible for validation."` |
| Tools NOT blocked (inconsistency) | `fix_all_apply`, `create_file_apply` (first call), `apply_project_mutation`, `apply_multi_file_edit`, `apply_with_verify`, `preview_multi_file_edit_apply` succeeded despite the hook policy applying equally to them by name |
| Impact on audit | Many preview→apply round-trips demoted to `exercised-preview-only` — see Coverage ledger. Promotion scorecard accordingly marks those entries `needs-more-evidence` |
| Severity | Environmental — this is the client's policy, not a server defect |
| Mitigation | Prefer `apply_with_verify` (own verification); route multi-file edits through `apply_multi_file_edit` (direct) or `preview_multi_file_edit` + `preview_multi_file_edit_apply`; or run the exercise in a client whose hook has transcript access |

### 9.16 OBS-001 (observation, not a bug) — Workspace requires explicit `workspace_reload` after `dotnet restore`
| Field | Detail |
|--------|--------|
| Tool chain | `workspace_load` → `dotnet restore` → `project_diagnostics` |
| Observed | Post-load `project_diagnostics(summary=true)` reported 3,164 errors (CS0246 × 1731, CS1061 × 1143, CS0103 × 191, CS0234 × 51, CS0012 × 29, CS1674 × 15, CS0122 × 4) because the workspace snapshot predated the NuGet restore. `workspace_reload` dropped error count to 0 |
| Response field that helps | `project_diagnostics` returned `restoreHint: "Many missing-type errors often mean NuGet restore has not been run. Run `dotnet restore` on the solution, then `workspace_reload`."` — this IS the documented contract |
| Severity | Observation — the contract is explicit; agents must honour `restoreHint` |
| Suggested enhancement (optional) | Consider a `workspace_load(path, reloadOnStaleRestore=true)` option that tails the project-directory nuget caches and reloads automatically when they change |

### 9.17 OBS-002 (observation) — Workspace-root sandbox blocks out-of-tree worktrees
| Field | Detail |
|--------|--------|
| Tool | `workspace_load` |
| Input | `path="C:\Code-Repo\DotNet-Firewall-Analyzer-audit-20260415\FirewallAnalyzer.slnx"` (a git worktree sibling directory) |
| Response | `Invalid argument: Path 'C:\Code-Repo\DotNet-Firewall-Analyzer-audit-20260415\FirewallAnalyzer.slnx' is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\DotNet-Firewall-Analyzer. Check parameter types and values match the tool schema.` |
| Severity | Observation — the rejection is correct safety behaviour but forced the audit to relocate its worktree under `.worktrees/` inside the sanctioned root |
| Suggested enhancement (optional) | Document this constraint prominently in the Phase 0 setup instructions of `ai_docs/prompts/experimental-promotion-exercise.md` so future runs don't waste a roundtrip on an out-of-tree worktree |

## 10. Improvement suggestions

> Each entry names the related §9 bug ID so the next agent can go straight to the evidence block.

- **§9.1 / REGRESSION-R17A — `restructure_preview`**: fix LHS-identifier placeholder substitution (currently only RHS expressions are substituted). Add an integration test covering `int __x__ = __e__;` → `var __x__ = __e__;` where both captures must be reflected. Gate promotion on this.
- **§9.2 / FORMAT-BUG-001 — `extract_interface_cross_project_preview`, `extract_and_wire_interface_preview`**: route the synthesized interface `CompilationUnitSyntax` through `Formatter.FormatAsync(workspace)` before emitting the diff. Same fix closes §9.3 for the interface file half of `dependency_inversion_preview`.
- **§9.3 / FORMAT-BUG-002 — `dependency_inversion_preview`**: stop re-serializing the class body. Restrict the rewrite to `BaseList` mutations on the `ClassDeclarationSyntax`, annotate new nodes with `Formatter.Annotation`, and run `Formatter.FormatAsync` on the final document. Also fix the diff-budget truncation (FLAG-6A) — provide a `scope="filePath"` param so callers can scope the rewrite and keep diffs within budget.
- **§9.4 / FORMAT-BUG-003 — `migrate_package_preview`**: replace the string-splice approach with `XDocument.Parse(…, LoadOptions.PreserveWhitespace)`. Manipulate the node tree, drop orphan empty `<ItemGroup>` nodes, save with matching formatting.
- **§9.5 / BUG-PAGINATION-001 — `test_reference_map`**: (i) honour the documented `projectName` filter in the reducer; (ii) add `offset`/`limit`/`totalCount`/`hasMore` fields to match the `find_references` response shape. Without this, the tool is effectively unusable on real-world solutions.
- **§9.6 / BUG-COMPILE-INCLUDE — `create_file_apply`**: before patching the csproj, evaluate `EnableDefaultCompileItems` (via `Microsoft.Build.Evaluation.Project.GetPropertyValue`) and skip the explicit `<Compile>` item when the SDK auto-includes are active. Ship a unit test with an SDK-style csproj.
- **§9.7 / BUG-JSON-PARSE — `get_prompt_text`**: wrap `JsonDocument.Parse` in try/catch, rethrow as `ArgumentException` with `category=InvalidArgument`. Suppress stack trace on `InvalidArgument` (already the server's convention elsewhere).
- **§9.8 / BUG-VALIDATE-FABRICATED — `validate_workspace`**: filter `changedFilePaths` against `Solution.Documents`; emit a `warnings[]` entry naming any drops. Consider returning `overallStatus="no-changed-files"` when the intersection is empty.
- **§9.9 / FORMAT-BUG-004 — `extract_method_preview`**: annotate generated nodes with `Formatter.Annotation`, then run `Formatter.FormatAsync` on the post-edit document. Ensure invocation-site indentation is preserved and that method-boundary closing braces are separated by newlines (no `}}` glue).
- **§9.10 / FORMAT-BUG-005 — `change_signature_preview op=add`**: (i) emit normal trivia between parameters and around `=>`; (ii) when `defaultValue` is supplied, construct `SyntaxFactory.EqualsValueClause(SyntaxFactory.ParseExpression(defaultValue))` on the new `ParameterSyntax` and render it in the declaration; (iii) include callsite rewrites in the preview `changes[]` array (closes the open backlog row `change-signature-preview-callsite-summary`).
- **§9.11 / FORMAT-BUG-006 — `split_class_preview`**: reset leading trivia on the copied type declaration to `SyntaxTriviaList.Empty` (or a synthesized header). Trim the blank line left where the moved member lived in the source file.
- **§9.12 / FLAG-FORMAT-RANGE-EMPTY — `format_range_preview`**: (i) ensure the in-memory solution snapshot reflects prior `apply_text_edit` mutations before the formatter runs; (ii) when the formatter produces no edits, add a `warnings[]` entry (`"no formatting changes in this range"`) to disambiguate no-op from no-match.
- **§9.13 / FLAG-RESOURCE-INVALID-RANGE — `source_file_lines` resource**: validate `startLine <= endLine` and both ≤ document line count. Return a structured `InvalidArgument` MCP error with a message naming the invalid range, not a generic `-32603`.
- **§9.14 / DRIFT-001 — `discover_capabilities` prompt param**: either rename the server-side param from `category` to `taskCategory` (matches the prompt appendix wording) OR update `ai_docs/prompts/experimental-promotion-exercise.md` Phase 8 table so its realistic input column maps to `category`.
- **§9.15 / UX-HOOK-001 — Client environment**: (no server fix required). Document in `ai_docs/prompts/experimental-promotion-exercise.md` that clients with transcript-inaccessible PreToolUse hooks should (a) use `apply_with_verify`, `apply_multi_file_edit`, or `preview_multi_file_edit_apply` as the apply vehicle, or (b) downgrade their audit mode to expect `exercised-preview-only` status on certain `*_apply` rows without penalising the tool's promotion score.
- **§9.16 / OBS-001 — Restore workflow**: consider a `workspace_load(..., reloadOnRestore=true)` option that watches the `obj/project.assets.json` mtime and auto-reloads when it changes.
- **§9.17 / OBS-002 — Sanctioned-root rejection**: document the sandbox constraint in the Phase 0 setup section of `experimental-promotion-exercise.md` so future audits don't waste a roundtrip trying an out-of-tree worktree location.

### Fix priority recommendation for v1.19

**Block promotion until fixed:** §9.1 (REGRESSION-R17A), §9.2, §9.3 (CRITICAL), §9.5 (HIGH).
**Fix before next batch of promotions:** §9.4, §9.6, §9.9, §9.10, §9.11, §9.12.
**Quality polish:** §9.7, §9.8, §9.13, §9.14.
**Documentation:** §9.15, §9.16, §9.17.

## 11. Known issue regression check

| Source id | Summary | Status |
|-----------|---------|--------|
| `change-signature-preview-callsite-summary` | `change_signature_preview` preview diff only covers the declaration-owner file | **still reproduces** (see §4) |
| `signalr-hub-auth-bypass` | `ApiKeyMiddleware` skips auth on SignalR hub | out-of-scope (not an MCP server issue) |
| `settings-get-unauth-exposure` | `GET /api/v1/settings` unauthenticated | out-of-scope (not an MCP server issue) |
| `suppress-ca1707-tests` / `seal-collection-result` / `snapshot-store-split` / `magic-numbers` / etc. | Repo maintainability rows | out-of-scope (not MCP server issues) |

No other backlog rows correspond to experimental MCP tools.
