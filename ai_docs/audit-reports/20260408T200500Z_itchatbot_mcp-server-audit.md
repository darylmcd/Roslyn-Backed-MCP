# MCP server audit — Roslyn MCP × IT-Chat-Bot

**Generated (UTC):** 2026-04-08T20:05:00Z  
**Target repository:** `c:\Code-Repo\IT-Chat-Bot`  
**Disposable branch:** `audit/deep-review-mcp-20260408`  
**Audit mode:** `full-surface` (writes exercised on disposable branch; audit-only mutations reverted where supported)  
**Solution entrypoint:** `ITChatBot.sln` (34 projects, 750 documents at last `workspace_status` verbose snapshot)  
**MCP server:** `roslyn-mcp` **1.8.1** (`0e2a88b9d2071aab494aa79d0e8a5429425c4d57`), **catalog 2026.04**, Roslyn **5.3.0.0**, runtime **.NET 10.0.5**, OS **Microsoft Windows 10.0.26200**  
**Aggregate surface (`server_info`):** tools **62 stable / 61 experimental**, resources **9 stable / 0 experimental**, prompts **0 stable / 16 experimental**

## Client / transport limitations

| Limitation | Impact |
|------------|--------|
| `fetch_mcp_resource` for `roslyn://server/catalog`, `roslyn://server/resource-templates`, and `roslyn://workspaces` returned **Server "user-roslyn" is not ready** at least once during this run | Per-tool **Category** / **SupportTier** from the live catalog JSON could not be merged from the authoritative resource; ledger uses **tier `—`** and **category `—`** unless inferred from tool descriptors. Aggregate counts match `server_info`. |
| MCP **`notifications/message`** / stderr correlation stream | Not captured in this Cursor session; noted only where tool responses exposed `_meta`. |
| **Parallel Phase 8b** concurrency probes | Cursor serializes MCP tool calls from this agent; parallel RW-lock contention scenarios **not executed** — see Phase 8b. |
| **16 prompts** | No `prompts/get` / list invocation in this client path — all prompt rows **`blocked`**. |
| **Phase 16b** plugin skills | Roslyn-Backed-MCP repo checkout not present in workspace — **`blocked`**. |

## Repo shape (constraints)

- **Multi-project** solution; **tests** present (many `*.Tests` projects).  
- **No** `Directory.Packages.props` — **Central Package Management absent** (`add_central_package_version_preview` / `remove_central_package_version_preview` fail fast with explicit message).  
- **Single target** `net10.0` across observed projects.  
- **Source generators** present (e.g. logger/regex in retrieval — exercised via `source_generated_documents` in prior transcript).  
- **DI** usage — exercised via `get_di_registrations` in prior transcript.  
- **`dotnet restore`** succeeded before semantic work (prior session).  
- **`.editorconfig`** — **mutated** by `set_editorconfig_option` (`dotnet_sort_system_directives_first = true` under `[*.{cs,csx,cake}]`). This is **not** reverted by `revert_last_apply` (documented server behavior).  

## Incident log (MCP / tool)

| ID | Severity | Finding |
|----|----------|---------|
| I1 | FAIL | `apply_text_edit` with a bad/zero-width edit produced a **corrupt diff** (namespace line broken). **Recovery:** `revert_last_apply` restored consistency (prior transcript). |
| I2 | FLAG | `rename_preview` / caret resolution: wrong column on `bool` token yields **InvalidOperation** (“Cannot rename metadata or built-in symbol 'bool'”) — error is actionable; column must target the symbol name. |
| I3 | FLAG | `find_overrides` at **interface** `CreatePlanAsync` returned **[]** (may be expected for implicit interface implementation — document as behavior vs. gap). |
| I4 | PASS | `add_central_package_version_preview` / `remove_central_package_version_preview` — clear **InvalidOperation** when `Directory.Packages.props` missing. |
| I5 | PASS | `fix_all_preview` for `IDE0005` returned **GuidanceMessage** when no FixAll provider — actionable fallback to `organize_usings_*`. |
| I6 | FLAG | `set_editorconfig_option` is **direct-apply, not undo-stack** — leaves persistent `.editorconfig` diff; use git revert for product cleanliness. |

## Phase summaries (0–18)

| Phase | Result | Notes |
|-------|--------|------|
| **0** | PASS | `server_info`, `workspace_load` on `ITChatBot.sln`, `workspace_list`, `workspace_status`, `project_graph`, restore precheck, ledger seeded. Verbose `workspace_status` exercised in closure pass. |
| **1** | PASS | `project_diagnostics`, `compile_check` (+ `emitValidation`, paging, severity filters), `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`, `list_analyzers`, `diagnostic_details`. |
| **2** | PASS | `get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols` (both visibility modes), `get_namespace_dependencies`, `get_nuget_dependencies`. |
| **3** | PASS | Deep pass on **RetrievalPlanner** / **IRetrievalPlanner**: `symbol_search`, `symbol_info`, `document_symbols`, `type_hierarchy`, `find_implementations`, `find_references`, `find_consumers`, `find_shared_members`, `find_type_mutations`, `find_type_usages`, `callers_callees`, `find_property_writes`, `member_hierarchy`, `symbol_relationships`, `symbol_signature_help`, `impact_analysis`, `semantic_search`. |
| **4** | PASS | `get_source_text`, `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree` on **CreatePlanAsync** / planner body regions. |
| **5** | PASS | `analyze_snippet` (expression / program / broken / `returnExpression`), `evaluate_csharp` (sum, runtime error, infinite loop → timeout). |
| **6** | PASS | Refactor lane: `format_document_*` (no-op), **rename** round-trip on `MatchesCategoryCapability`, `compile_check` clean. **Net product intent:** behavior-preserving validation of rename pipeline. |
| **7** | PASS | `get_editorconfig_options`, `set_editorconfig_option`, `get_msbuild_properties`, `evaluate_msbuild_property`, `evaluate_msbuild_items` (correct **`project`** parameter name vs mistaken `projectName`). |
| **8** | PASS | `dotnet build` + `build_workspace` succeeded; `test_discover`; `test_run` **ITChatBot.Retrieval.Tests** 139 passed; **`test_run` solution-wide** structured result: **797 passed, 108 failed** — failures concentrated in **ITChatBot.Integration.Tests** (`CloudAdapter` **ambiguous constructors** / host startup). **This is application/DI test environment, not Roslyn MCP defect.** |
| **8b** | **blocked** | Intended parallel RW-lock / concurrency matrix **not run** — Cursor client serializes tool calls; `_meta.gateMode` / `heldMs` observed on individual calls only. |
| **10** | PASS | Preview-heavy: `move_type_to_file_preview`, `add_package_reference_preview`, `get_code_actions` → `preview_code_action` → **`apply_code_action` → `revert_last_apply`** (clean undo). |
| **9** | PASS | Undo after 10: **`rename_preview` → `rename_apply` → `revert_last_apply`**; separate **`apply_code_action` → `revert_last_apply`** — stack behaves; `revert_last_apply` **~6–7 s** `heldMs` observed on one revert. |
| **11** | PASS | Security / reflection / DI / generators lane covered in prior transcript + `semantic_search` / `project_graph` refresh. |
| **12** | PASS | Editor actions: `get_code_actions`, `preview_code_action`, `apply_code_action` (reverted). |
| **13** | PASS | Formatting: `format_document_*`, `format_range_preview` (empty changes), `organize_usings_*` (earlier), `fix_all_preview` (guidance path). |
| **14** | PASS | Navigation: `go_to_definition`, `goto_type_definition`, `enclosing_symbol`, `get_completions`, `find_references_bulk`, `symbol_search`. |
| **15** | **blocked** | All **`roslyn://*` resources**: client resource fetch **not ready** during attempts — cannot cross-check tool vs resource payloads in this session. |
| **16** | **blocked** | **16 prompts**: no prompt-list / get invocation available here — **`blocked` (client)**. |
| **16b** | **blocked** | Plugin skills directory (Roslyn-Backed-MCP) **not in workspace**. |
| **17** | PASS | Negative probes: `workspace_status` bogus id → **NotFound** (prior); `find_references` invalid handle → **NotFound** (prior); CPM tools → explicit **InvalidOperation**. |
| **18** | N/A | Regression watch vs prior Roslyn MCP issues: **no separate MCP bug backlog in this repo** (`ai_docs/backlog.md` is product backlog). Prior incidents (I1–I2) recorded above. |

## Validation summary

| Check | Outcome |
|-------|---------|
| `build_workspace` | PASS (0 errors; MSBuild warnings only as reported) |
| `build_project` `ITChatBot.Retrieval` | PASS |
| `test_run` `ITChatBot.Retrieval.Tests` | PASS (139/139 prior; not re-run after final `workspace_close`) |
| `test_run` full solution | **FAIL overall** — 108 failures, integration host/DI (`CloudAdapter` ambiguity); **unit/component tests largely pass** |
| `test_coverage` | **Success: true** but **no coverlet collector** — message explains prerequisite |
| `workspace_close` | PASS (session closed at end of audit) |

## Coverage summary (counts by status)

**Tools (n=123):**

| Status | Count |
|--------|------:|
| exercised | 86 |
| exercised-apply | 6 |
| exercised-preview-only | 1 |
| skipped-safety | 30 |
| blocked | 0 |

**Resources (n=9):**

| Status | Count |
|--------|------:|
| blocked | 9 |

**Prompts (n=16):**

| Status | Count |
|--------|------:|
| blocked | 16 |

## Experimental promotion scorecard (rollup)

| Recommendation | Count | Notes |
|----------------|------:|-------|
| needs-more-evidence | 61 | Experimental **tools** not all individually stress-tested; many **`skipped-safety`** rows. |
| promote | 0 | Insufficient per-call evidence for promotion in this run. |
| keep-experimental | — | Default for exercised experimental tools pending dedicated promotion pass. |
| deprecate | 0 | |

*(Full per-call promotion rating deferred — catalog **`SupportTier`** unavailable via `roslyn://server/catalog` in this session.)*

## Performance baseline (representative `_meta.elapsedMs`)

| Tool | elapsedMs (approx) | Notes |
|------|-------------------:|-------|
| `compile_check` (emit) | ~2600 | Prior transcript — emit path slower than diagnostics-only |
| `test_run` (solution) | 27978 | Wall-clock host `dotnet test` |
| `test_coverage` | 4541 | No collector |
| `revert_last_apply` | 6816 / 7028 | Observed on different reverts |
| `semantic_search` | 90 | |
| `get_code_actions` | 212 | |
| `project_graph` | 0 | Fast graph read |

## Schema vs behaviour

- **`evaluate_msbuild_property` / `evaluate_msbuild_items`:** use **`project`**, not `projectName` (wrong name caused initial failures).  
- **`add_package_reference_preview`:** uses **`projectName`** — intentional naming asymmetry across MSBuild tools (**FLAG** for documentation consistency).  
- **`apply_code_action`:** schema requires only `previewToken` — **no `workspaceId`**; works as observed.

## Final surface closure

- [x] All catalog tools have a ledger row (123/123).  
- [x] All resources have a ledger row (9/9) — **blocked** via client.  
- [x] All prompts have a ledger row (16/16) — **blocked** via client.  
- [x] `workspace_close` executed for workspace `140381f35b494ae7a33739b65363788d`.  
- [x] No silent omissions: every row has **exactly one** final status.

---

## Coverage ledger — tools (123)

`tier` / `category`: **—** = not loaded from `roslyn://server/catalog` (resource fetch failure).

| kind | name | tier | category | status | phase | lastElapsedMs | notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | add_central_package_version_preview | — | — | exercised | 0 / 15 | 1 | InvalidOperation: no `Directory.Packages.props` — repo-shape |
| tool | add_package_reference_preview | — | — | exercised-preview-only | 10 | 8 | Newtonsoft.Json preview to `ITChatBot.Configuration.csproj`; not applied |
| tool | add_pragma_suppression | — | — | skipped-safety | — | — | Audit avoided pragma/project churn |
| tool | add_project_reference_preview | — | — | skipped-safety | — | — | Destructive csproj graph edit not applied |
| tool | add_target_framework_preview | — | — | skipped-safety | — | — | Single-TFM repo; no apply |
| tool | analyze_control_flow | — | — | exercised | 4 | — | Prior transcript |
| tool | analyze_data_flow | — | — | exercised | 4 | — | Prior transcript |
| tool | analyze_snippet | — | — | exercised | 5 | — | Prior transcript |
| tool | apply_code_action | — | — | exercised-apply | 10 / 9 | 3 | Applied then **reverted** |
| tool | apply_composite_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | apply_multi_file_edit | — | — | skipped-safety | — | — | Not invoked (text edit failure used `apply_text_edit` in prior transcript) |
| tool | apply_project_mutation | — | — | skipped-safety | — | — | Not invoked |
| tool | apply_text_edit | — | — | exercised | 6–10 | — | **FAIL** path + revert (prior transcript) |
| tool | build_project | — | — | exercised | 8 | 1041 | `ITChatBot.Retrieval` |
| tool | build_workspace | — | — | exercised | 8 | — | Prior transcript |
| tool | bulk_replace_type_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | bulk_replace_type_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | callers_callees | — | — | exercised | 3–4 | — | Prior transcript |
| tool | code_fix_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | code_fix_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | compile_check | — | — | exercised | 1 | — | Prior transcript + paging/emit |
| tool | create_file_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | create_file_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | delete_file_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | delete_file_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | dependency_inversion_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | diagnostic_details | — | — | exercised | 1 | — | Prior transcript |
| tool | document_symbols | — | — | exercised | 3 | — | Prior transcript |
| tool | enclosing_symbol | — | — | exercised | 14 | 2 | |
| tool | evaluate_csharp | — | — | exercised | 5 | — | Prior transcript |
| tool | evaluate_msbuild_items | — | — | exercised | 7 | — | Prior transcript |
| tool | evaluate_msbuild_property | — | — | exercised | 7 | — | Prior transcript |
| tool | extract_and_wire_interface_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | extract_interface_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | extract_interface_cross_project_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | extract_interface_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | extract_type_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | extract_type_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | find_base_members | — | — | exercised | 14 | — | Implementation `CreatePlanAsync` → interface member |
| tool | find_consumers | — | — | exercised | 3 | — | Prior transcript |
| tool | find_implementations | — | — | exercised | 3 | — | Prior transcript |
| tool | find_overrides | — | — | exercised | 14 | — | Empty for interface probe |
| tool | find_property_writes | — | — | exercised | 3 | — | Prior transcript |
| tool | find_references | — | — | exercised | 3 / 17 | — | Includes invalid-handle probe (prior) |
| tool | find_references_bulk | — | — | exercised | 14 | 51 | |
| tool | find_reflection_usages | — | — | exercised | 11 | — | Prior transcript |
| tool | find_shared_members | — | — | exercised | 3 | — | Prior transcript |
| tool | find_type_mutations | — | — | exercised | 3 | — | Prior transcript |
| tool | find_type_usages | — | — | exercised | 3 | — | Prior transcript |
| tool | find_unused_symbols | — | — | exercised | 2 | — | Prior transcript |
| tool | fix_all_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | fix_all_preview | — | — | exercised | 13 | 72 | IDE0005 guidance-only |
| tool | format_document_apply | — | — | exercised-apply | 6 | 0 | Empty apply (still registers apply pipeline) |
| tool | format_document_preview | — | — | exercised | 6 | — | Prior transcript |
| tool | format_range_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | format_range_preview | — | — | exercised | 13 | 2 | Empty changes |
| tool | get_code_actions | — | — | exercised | 12 | 212 | |
| tool | get_cohesion_metrics | — | — | exercised | 2 | — | Prior transcript |
| tool | get_completions | — | — | exercised | 14 | 25 | `IsIncomplete: true` |
| tool | get_complexity_metrics | — | — | exercised | 2 | — | Prior transcript |
| tool | get_di_registrations | — | — | exercised | 11 | — | Prior transcript |
| tool | get_editorconfig_options | — | — | exercised | 7 | — | Prior transcript |
| tool | get_msbuild_properties | — | — | exercised | 7 | — | Prior transcript |
| tool | get_namespace_dependencies | — | — | exercised | 2 | — | Prior transcript |
| tool | get_nuget_dependencies | — | — | exercised | 2 | — | Prior transcript |
| tool | get_operations | — | — | exercised | 4 | — | Prior transcript |
| tool | get_source_text | — | — | exercised | 4 | — | Prior transcript |
| tool | get_syntax_tree | — | — | exercised | 4 | — | Prior transcript |
| tool | go_to_definition | — | — | exercised | 14 | — | |
| tool | goto_type_definition | — | — | exercised | 14 | — | |
| tool | impact_analysis | — | — | exercised | 3 | — | Prior transcript |
| tool | list_analyzers | — | — | exercised | 1 | — | Prior transcript |
| tool | member_hierarchy | — | — | exercised | 3 | — | Prior transcript |
| tool | migrate_package_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | move_file_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | move_file_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | move_type_to_file_apply | — | — | skipped-safety | — | — | Preview only (avoid split file apply) |
| tool | move_type_to_file_preview | — | — | exercised | 10 | 65 | `SelectedSource` → new file |
| tool | move_type_to_project_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | nuget_vulnerability_scan | — | — | exercised | 1 | — | Prior transcript |
| tool | organize_usings_apply | — | — | exercised-apply | 6 | 0 | Empty apply (prior transcript); on undo stack semantics |
| tool | organize_usings_preview | — | — | exercised | 6 / 9 | 26 | |
| tool | preview_code_action | — | — | exercised | 10 | 55 | Expression-body refactor |
| tool | project_diagnostics | — | — | exercised | 1 | — | Prior transcript |
| tool | project_graph | — | — | exercised | 0 / 14 | 0 | |
| tool | remove_central_package_version_preview | — | — | exercised | 15 | 1 | Same as add-central — no CPM file |
| tool | remove_dead_code_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | remove_dead_code_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | remove_package_reference_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | remove_project_reference_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | remove_target_framework_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | rename_apply | — | — | exercised-apply | 6 / 9 | 4 | Undone by revert |
| tool | rename_preview | — | — | exercised | 6 / 9 | 21 | |
| tool | revert_last_apply | — | — | exercised | 9 | 6816–7028 | Multiple successful reverts |
| tool | scaffold_test_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | scaffold_test_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | scaffold_type_apply | — | — | skipped-safety | — | — | Not invoked |
| tool | scaffold_type_preview | — | — | exercised | 10 | — | Prior transcript |
| tool | security_analyzer_status | — | — | exercised | 1 | — | Prior transcript |
| tool | security_diagnostics | — | — | exercised | 1 | — | Prior transcript |
| tool | semantic_search | — | — | exercised | 3 / 11 | 90 | |
| tool | server_info | — | — | exercised | 0 | 0 | |
| tool | set_conditional_property_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | set_diagnostic_severity | — | — | skipped-safety | — | — | Not invoked |
| tool | set_editorconfig_option | — | — | exercised-apply | 7 | — | **Direct disk write** — see repo diff |
| tool | set_project_property_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | source_generated_documents | — | — | exercised | 11 | — | Prior transcript |
| tool | split_class_preview | — | — | skipped-safety | — | — | Not invoked |
| tool | symbol_info | — | — | exercised | 3 | — | Prior transcript |
| tool | symbol_relationships | — | — | exercised | 3 | — | Prior transcript |
| tool | symbol_search | — | — | exercised | 3 / 14 | — | |
| tool | symbol_signature_help | — | — | exercised | 3 | — | Prior transcript |
| tool | test_coverage | — | — | exercised | 8 | 4541 | No coverlet collector message |
| tool | test_discover | — | — | exercised | 8 | — | Prior transcript |
| tool | test_related | — | — | exercised | 8 | — | |
| tool | test_related_files | — | — | exercised | 8 | 8 | |
| tool | test_run | — | — | exercised | 8 | 27978 | Full solution + prior single-project |
| tool | type_hierarchy | — | — | exercised | 3 | — | Prior transcript |
| tool | workspace_close | — | — | exercised | closure | 3 | End of audit |
| tool | workspace_list | — | — | exercised | 0 / closure | — | |
| tool | workspace_load | — | — | exercised | 0 | — | Prior transcript |
| tool | workspace_reload | — | — | exercised | 0 | — | Prior transcript |
| tool | workspace_status | — | — | exercised | 0 / 14 | 0 | Summary + verbose |

## Coverage ledger — resources (9)

| kind | name | tier | category | status | phase | lastElapsedMs | notes |
|------|------|------|----------|--------|-------|---------------|-------|
| resource | roslyn://server/catalog | stable | — | blocked | 15 | — | `fetch_mcp_resource` / server not ready |
| resource | roslyn://server/resource-templates | stable | — | blocked | 15 | — | Same |
| resource | roslyn://workspaces | stable | — | blocked | 15 | — | Same |
| resource | roslyn://workspaces/verbose | stable | — | blocked | 15 | — | Same |
| resource | roslyn://workspace/{id}/status | stable | — | blocked | 15 | — | Same |
| resource | roslyn://workspace/{id}/status/verbose | stable | — | blocked | 15 | — | Same |
| resource | roslyn://workspace/{id}/projects | stable | — | blocked | 15 | — | Same |
| resource | roslyn://workspace/{id}/diagnostics | stable | — | blocked | 15 | — | Same |
| resource | roslyn://workspace/{id}/file/{filePath} | stable | — | blocked | 15 | — | Same |

*(Cross-check with `workspace_status` / `project_graph` / `project_diagnostics` / `get_source_text` tools used instead.)*

## Coverage ledger — prompts (16)

| kind | name | tier | category | status | phase | lastElapsedMs | notes |
|------|------|------|----------|--------|-------|---------------|-------|
| prompt | *(all 16 experimental prompts in catalog 2026.04)* | experimental | — | blocked | 16 | — | MCP **`prompts/list` / `prompts/get`** not exercised in Cursor agent path |

---

**Phase 6 refactor summary (product):** Rename pipeline validated on `MatchesCategoryCapability`; formatting/organize usings largely no-op on touched files. **Persistent:** `.editorconfig` **`dotnet_sort_system_directives_first = true`** via `set_editorconfig_option`.

**Follow-up (operator):** Restore `.editorconfig` if the sort directive change was audit-only; fix integration-test **CloudAdapter** DI registration to unblock `ITChatBot.Integration.Tests`; re-run audit with working **`roslyn://server/catalog`** resource fetch to fill **tier/category** columns and promotion scoring; optional Roslyn-Backed-MCP checkout for **Phase 16b** skills parity.
