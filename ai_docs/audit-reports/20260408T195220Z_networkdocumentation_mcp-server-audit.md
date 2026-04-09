# MCP server audit — NetworkDocumentation (user-roslyn / roslyn-mcp)

**UTC timestamp:** `20260408T195220Z`  
**Audited solution:** `NetworkDocumentation.sln` (`networkdocumentation`)  
**Server:** `user-roslyn` — **roslyn-mcp** `1.8.1+0e2a88b9d2071aab494aa79d0e8a5429425c4d57`  
**Catalog:** `2026.04` (per `server_info`)  
**Surface (authoritative):** 62 stable + 61 experimental **tools** (123 total), 9 stable **resources**, 16 experimental **prompts**  
**Disposable branch (recommended):** `audit/mcp-deep-review-20260408` (full-surface writes exercised there; working tree restored to match `HEAD` for `*.csproj` and source before report finalize)  
**Workspace IDs:** Primary deep-review session `6db5ce6c3c4c4bf092bda2208c7a02b0` (closed after Phase 17e); reload after audit `a2d4e6a6601c49b6b7b4193b22974d1c`.  
**Intended canonical store:** This file lives under the audited repo per `deep-review-and-refactor.md` (workspace is not Roslyn-Backed-MCP). Copy into the plugin repo’s `ai_docs/audit-reports/` if you maintain a central archive.

**Debug log capture:** The Cursor agent session **did not surface** MCP `notifications/message` (or equivalent) log streams to the model — treat as **no client-visible MCP log channel** unless your host enables it.

**MCP resource reads (`fetch_mcp_resource`):** Invocations for `roslyn://server/catalog` returned **`Server "user-roslyn" is not ready`** from the Cursor bridge during this session. Phase 15 comparisons to resources are therefore **blocked — host resource bridge not ready** (tools remained callable). Re-run resource parity checks when the bridge is healthy.

**MCP prompts (`prompts/list` / `prompts/get`):** The agent only has `call_mcp_tool` for **tools**, not prompt endpoints. **Phase 16 full-surface prompt execution is blocked — host does not expose prompt RPC to this agent.** Counts and names are taken from `server_info` and filesystem descriptors under the Cursor MCP cache.

**Phase 16b (plugin skills):** Roslyn-Backed-MCP **repo root is not in this workspace**. Skills were spot-checked against the **marketplace cache** at `…/roslyn-mcp-marketplace/roslyn-mcp/1.8.1/skills/*/SKILL.md` (version aligns with server `1.8.1`). Treat as **proxy evidence**, not a substitute for auditing the shipped git tree.

---

## Draft / finalize contract

This file is the **canonical** audit. A separate `.draft.md` was **not** retained: findings from **Phase 0–8** were produced in an earlier segment of the same user request and merged here; **Phases 8b–18** were appended from the continuation. If your process requires a per-phase draft file only, treat this document as the **post-merge** artifact.

---

## Executive summary

- **Overall:** Server `1.8.1` behaved predictably for workspace lifecycle, diagnostics, semantic queries, preview/apply for file and project mutations, and structured **NotFound** / **InvalidArgument** errors on bad inputs. **Parallel MCP fan-out could not be validated** — the agent host serializes tool calls. **`fetch_mcp_resource` failed** intermittently. **Prompts could not be executed** through the agent API.
- **Notable findings:** (1) `extract_interface_cross_project_preview` correctly **rejected a circular project reference** when targeting `NetworkDocumentation.Parsers`. (2) `find_overrides` at an **override** member site (`Device.Equals`) returned **`[]`** — use the **virtual declaration** site for override discovery. (3) `move_file_preview` / large diffs may hit **FLAG-6A** truncation in unified diff payloads. (4) Prior report `ai_docs/reports/mcp-server-audit-report.md` issue **`server_info.workspaceCount` vs `workspace_list`**: **not reproduced** in this run (`workspaceCount: 1` with one loaded session).
- **Solution test health (shell):** `dotnet test NetworkDocumentation.sln -c Debug --no-build` was reported in the prior segment as **8057 passed, 9 skipped, 1 failed** (`WebUiPlaywrightTests.ReactDiagram_WheelZoom_KeepsPointUnderCursor_NoDrift`). Treat as **environment/UI sensitivity** unless tied to a code change; not an MCP defect.

---

## Phase notes (0–18)

| Phase | Status | Notes |
|------|--------|--------|
| **0** | OK | `server_info`, catalog counts, workspace discovery. |
| **1** | OK | Broad diagnostics, `compile_check`, security / NuGet scans, analyzers. |
| **2** | OK | Complexity, cohesion, unused symbols, deps. |
| **3** | OK | Deep symbol pass on `RegionService` / related APIs. |
| **4** | OK | Data/control flow on `QueryEvaluator.EvaluateComparison` (or equivalent). |
| **5** | OK | `analyze_snippet`, `evaluate_csharp` (incl. watchdog on bad scripts). |
| **6** | OK | Rename round-trip, format/organize/fix previews, `apply_text_edit` + `revert_last_apply` where applicable; **no retained product refactor** required for this audit. |
| **7** | OK | EditorConfig / MSBuild property probes (**`project` name** parameter for MSBuild tools, not only path). |
| **8** | OK | Build, test discovery/run/coverage, semantic search, navigation, DI, source generators. |
| **8b** | **Blocked (client)** | **8b.2–8b.4 parallel / interleaving:** **N/A — client serializes tool calls.** **8b.1** sequential `_meta.elapsedMs` sampled (e.g. `compile_check` ~982–1082 ms; `workspace_reload` ~1.8–3.5 s). |
| **10** | OK (partial apply) | `move_type_to_file_preview` (RegionMapEntry), `move_file_preview` (large diff truncation flag), `create_file_apply` + `delete_file_apply` loop, `extract_interface_cross_project_preview` (**error: circular ref** — expected). No `apply_composite_preview`. |
| **9** | OK | **After Phase 10:** `apply_text_edit` audit comment → `revert_last_apply` → `compile_check` clean; second `revert_last_apply` → clear “nothing to revert” (also Phase **17d**). |
| **11** | OK (prior segment) | Semantic search, reflection, DI registrations, source-generated docs (see prior transcript). |
| **12** | OK | `scaffold_type_preview` / `scaffold_type_apply` → `delete_file_preview` / `delete_file_apply`; `internal sealed class` default confirmed. **`scaffold_test_*` not run.** |
| **13** | OK | `add_package_reference_preview` → `apply_project_mutation` → `remove_package_reference_preview` → `apply_project_mutation` → `workspace_reload`; **csproj whitespace drift** corrected to match `HEAD`. |
| **14** | OK / caveat | Navigation tools exercised in prior segment; **`find_overrides` empty at override site** (see summary). |
| **15** | **Blocked** | Resource reads via **`fetch_mcp_resource` blocked** (bridge). |
| **16** | **Blocked** | **Prompt RPC not available to agent.** |
| **16b** | OK (proxy) | 10 skills present under marketplace cache **1.8.1**; spot-check for stale tool names recommended against live `tools/*.json`. |
| **17** | OK | Bad workspace id, bad symbol handle, out-of-range `go_to_definition`, double revert, stale scaffold token after reload (**NotFound**). |
| **18** | N/A | **`ai_docs/backlog.md` has no MCP-specific rows.** Regression probes taken from `ai_docs/reports/mcp-server-audit-report.md` (see below). |

---

## Concurrency matrix (Phase 8b)

**Phase 8b.2 / 8b.3 / 8b.4 / benchmark comparison:** **N/A — client serializes tool calls** (Cursor agent MCP invocation model). Do not infer speedup, ±15% stability, or benchmark parity from this run.

**8b.1 Sequential baseline (sample):**

| Probe | `_meta.elapsedMs` (approx) | Notes |
|-------|---------------------------|--------|
| `workspace_status` | 0 | Trivial metadata |
| `compile_check` | ~981–1082 | Reader gate |
| `workspace_reload` | ~1797–3520 | Reload after mutations |

**8b.5 Writer reclassification:** Not all six writers were re-run in the continuation; **`apply_text_edit`**, **`revert_last_apply`** exercised with `_meta` recorded. **`apply_multi_file_edit`**, **`set_editorconfig_option`**, **`set_diagnostic_severity`**, **`add_pragma_suppression`**: **needs-more-evidence** in this continuation (prior segment may have touched subsets — not re-verified here).

---

## Known issue regression check (Phase 18)

Source: `ai_docs/reports/mcp-server-audit-report.md` (2026-04-03).

| # | Historical finding | This run |
|---|-------------------|----------|
| 1 | `server_info.workspaceCount` out of sync | **Not reproduced** — `workspaceCount: 1` with one session in `workspace_list`. |
| 2 | `find_type_mutations` for `NetworkInventory` | **Not re-run** — **needs-more-evidence** (continuation). |
| 3 | `find_type_usages` numeric `Classification` | **Not re-run** — **needs-more-evidence**. |
| 4 | `get_complexity_metrics` `MaxNestingDepth` 0 | **Not re-run** — **needs-more-evidence**. |
| 5 | `semantic_search` empty for signature-like query | Prior segment ran `semantic_search`; empty results may still occur — **partially addressed** by repo doc in same report file. |

---

## Coverage ledger — tools (123)

**Legend:** **PS** = exercised in **prior segment** of this audit (conversation summary; not re-invoked here). **CT** = **continuation** segment. **NME** = **needs-more-evidence** (no successful invoke in either segment **or** not re-verified). **SRS** = **skipped-repo-shape**. **SS** = **skipped-safety**.

| Tool | Status | Notes |
|------|--------|-------|
| `add_central_package_version_preview` | SRS | No `Directory.Packages.props` / central management in this repo. |
| `add_package_reference_preview` | CT | Used in reversible package loop. |
| `add_pragma_suppression` | NME | — |
| `add_project_reference_preview` | NME | — |
| `add_target_framework_preview` | SRS | Single `net10.0` target; multi-target not probed. |
| `analyze_control_flow` | PS | — |
| `analyze_data_flow` | PS | — |
| `analyze_snippet` | PS | — |
| `apply_code_action` | NME | — |
| `apply_composite_preview` | SS | Not invoked (destructive; full-surface optional). |
| `apply_multi_file_edit` | NME | — |
| `apply_project_mutation` | CT | Forward + reverse NuGet mutation. |
| `apply_text_edit` | CT | Phase 9 audit edit + prior segment. |
| `build_project` | PS | — |
| `build_workspace` | PS | — |
| `bulk_replace_type_apply` | NME | — |
| `bulk_replace_type_preview` | NME | — |
| `callers_callees` | PS | RegionService phase. |
| `code_fix_apply` | NME | — |
| `code_fix_preview` | PS | — |
| `compile_check` | CT+PS | Clean after undo. |
| `create_file_apply` | CT | Prior + continuation scratch file. |
| `create_file_preview` | CT | — |
| `delete_file_apply` | CT | — |
| `delete_file_preview` | CT | — |
| `dependency_inversion_preview` | NME | — |
| `diagnostic_details` | PS | — |
| `document_symbols` | PS | — |
| `enclosing_symbol` | PS | — |
| `evaluate_csharp` | PS | — |
| `evaluate_msbuild_items` | PS | — |
| `evaluate_msbuild_property` | PS | — |
| `extract_and_wire_interface_preview` | NME | — |
| `extract_interface_apply` | NME | — |
| `extract_interface_cross_project_preview` | CT | **InvalidOperation** circular reference — correct guard. |
| `extract_interface_preview` | NME | — |
| `extract_type_apply` | NME | — |
| `extract_type_preview` | NME | — |
| `find_base_members` | NME | — |
| `find_consumers` | PS | — |
| `find_implementations` | PS | — |
| `find_overrides` | CT | **[]** at `Device.Equals` override — **wrong caret** for API. |
| `find_property_writes` | NME | — |
| `find_references` | CT | **NotFound** envelope for junk `symbolHandle` (Phase 17). |
| `find_references_bulk` | PS | — |
| `find_reflection_usages` | PS | — |
| `find_shared_members` | PS | — |
| `find_type_mutations` | PS | See regression table. |
| `find_type_usages` | PS | — |
| `find_unused_symbols` | PS | — |
| `fix_all_apply` | NME | — |
| `fix_all_preview` | PS | — |
| `format_document_apply` | PS | — |
| `format_document_preview` | CT | Empty changes on `RegionService.cs` (already formatted). |
| `format_range_apply` | NME | — |
| `format_range_preview` | NME | — |
| `get_code_actions` | PS | Invocation error noted in prior segment — treat as **exercised-with-failure**. |
| `get_cohesion_metrics` | PS | — |
| `get_completions` | PS | — |
| `get_complexity_metrics` | PS | — |
| `get_di_registrations` | PS | — |
| `get_editorconfig_options` | PS | — |
| `get_msbuild_properties` | PS | — |
| `get_namespace_dependencies` | PS | — |
| `get_nuget_dependencies` | PS | — |
| `get_operations` | PS | — |
| `get_source_text` | NME | — |
| `get_syntax_tree` | NME | — |
| `go_to_definition` | CT | **InvalidArgument** line out of range (Phase 17). |
| `goto_type_definition` | PS | — |
| `impact_analysis` | PS | — |
| `list_analyzers` | PS | — |
| `member_hierarchy` | PS | — |
| `migrate_package_preview` | NME | — |
| `move_file_apply` | NME | Preview only (apply not required for audit). |
| `move_file_preview` | CT | Large diff **FLAG-6A** truncation. |
| `move_type_to_file_apply` | SS | Preview only; apply would split production type. |
| `move_type_to_file_preview` | CT+PS | Valid preview for `RegionMapEntry`. |
| `move_type_to_project_preview` | NME | — |
| `nuget_vulnerability_scan` | PS | — |
| `organize_usings_apply` | NME | — |
| `organize_usings_preview` | PS | — |
| `preview_code_action` | NME | — |
| `project_diagnostics` | PS | — |
| `project_graph` | PS | — |
| `remove_central_package_version_preview` | SRS | No central package management file. |
| `remove_dead_code_apply` | NME | — |
| `remove_dead_code_preview` | NME | — |
| `remove_package_reference_preview` | CT | Reversible loop. |
| `remove_project_reference_preview` | NME | — |
| `remove_target_framework_preview` | SRS | Single target. |
| `rename_apply` | PS | — |
| `rename_preview` | PS | — |
| `revert_last_apply` | CT | Undo audit edit + empty stack behavior. |
| `scaffold_test_apply` | NME | — |
| `scaffold_test_preview` | NME | — |
| `scaffold_type_apply` | CT | — |
| `scaffold_type_preview` | CT | — |
| `security_analyzer_status` | PS | — |
| `security_diagnostics` | PS | — |
| `semantic_search` | PS | — |
| `server_info` | CT | — |
| `set_conditional_property_preview` | NME | — |
| `set_diagnostic_severity` | NME | — |
| `set_editorconfig_option` | NME | — |
| `set_project_property_preview` | NME | — |
| `source_generated_documents` | PS | — |
| `split_class_preview` | NME | — |
| `symbol_info` | PS | — |
| `symbol_relationships` | PS | — |
| `symbol_search` | PS | — |
| `symbol_signature_help` | PS | — |
| `test_coverage` | PS | — |
| `test_discover` | PS | — |
| `test_related` | PS | — |
| `test_related_files` | PS | — |
| `test_run` | PS | Filtered run noted in prior segment. |
| `type_hierarchy` | PS | — |
| `workspace_close` | CT | Closed session; verified closed id **NotFound**. |
| `workspace_list` | CT | — |
| `workspace_load` | CT | Reload after close. |
| `workspace_reload` | CT | After project mutations. |
| `workspace_status` | CT | Bad id + good id probes. |

**Ledger reconciliation:** 123 rows above; `server_info.surface.tools` **62+61=123** — **matched**.

---

## Coverage ledger — resources (9)

| Resource | Status | Notes |
|----------|--------|-------|
| `roslyn://server/catalog` | **blocked** | `fetch_mcp_resource`: server not ready (Cursor). |
| `roslyn://server/resource-templates` | **blocked** | Same. |
| `roslyn://workspaces` | **blocked** | Same. |
| `roslyn://workspaces/verbose` | **blocked** | Same. |
| `roslyn://workspace/{id}/status` | **blocked** | Same. |
| `roslyn://workspace/{id}/status/verbose` | **blocked** | Same. |
| `roslyn://workspace/{id}/projects` | **blocked** | Same. |
| `roslyn://workspace/{id}/diagnostics` | **blocked** | Same. |
| `roslyn://workspace/{id}/file/{filePath}` | **blocked** | Same. |

**Totals:** 9 stable + 0 experimental — matches `server_info`.

---

## Coverage ledger — prompts (16)

All **experimental** per `server_info`. **Status:** **blocked — agent cannot invoke MCP prompts** (no `prompts/get` transport exposed to this tool surface). Names are per catalog policy (`explain_error`, `suggest_refactoring`, `review_file`, `discover_capabilities`, etc.); **re-validate** when the host exposes prompt RPC.

---

## Phase 16b — Skills audit (proxy, 1.8.1 cache)

| Skill | Frontmatter | Tool refs | Dry run | Safety | Notes |
|-------|-------------|-----------|---------|--------|-------|
| `analyze` | OK (cached) | Not exhaustively validated | pass | n/a | Spot-check against live tool list. |
| `complexity` | OK | Same | pass | n/a | — |
| `dead-code` | OK | Same | pass | n/a | — |
| `document` | OK | Same | pass | n/a | — |
| `explain-error` | OK | Same | pass | n/a | — |
| `migrate-package` | OK | Same | flag | pass | Mutations should stay preview→apply; confirm in SKILL body. |
| `refactor` | OK | Same | flag | pass | — |
| `review` | OK | Same | pass | n/a | — |
| `security` | OK | Same | pass | n/a | — |
| `test-coverage` | OK | Same | pass | n/a | — |

**Checkpoint:** Full per-skill tool reference extraction **not** automated here — **needs-more-evidence** for zero invalid refs.

---

## Improvement suggestions (server + host)

1. **Cursor bridge:** Fix intermittent **`user-roslyn` not ready** for `fetch_mcp_resource` so Phase 15 catalog/resource parity can run.  
2. **Agent API:** Expose **MCP prompts** (`prompts/list`, `prompts/get`) to agents for Phase 16 automation.  
3. **`find_overrides`:** Document that caret must be on **original virtual/interface** declaration for reliable results (override sites may return empty).  
4. **Large diffs:** When **FLAG-6A** triggers, surface **hunk count** and **file path** prominently in tool output (already partially done).  
5. **Optional:** Re-run `find_type_mutations` / `get_complexity_metrics` / `find_type_usages` regression cases from `mcp-server-audit-report.md` with saved symbol handles.

---

## Experimental promotion scorecard (abbreviated)

| Tier | Recommendation | Notes |
|------|----------------|-------|
| Core workspace + diagnostics | **keep-experimental** → **promote** candidates | Stable behavior; full promotion needs prompt/resource parity evidence. |
| Destructive / composite applies | **keep-experimental** | Apply siblings intentionally limited in audit. |
| Resources / prompts | **needs-more-evidence** | Blocked by host this session. |

---

## Final surface closure checklist

- [x] Ledger totals match `server_info` (123 / 9 / 16).  
- [x] Audit-only mutations reverted or restored to **git-clean** `NetworkDocumentation.Core.csproj` and no stray audit files under `AuditScratch/`.  
- [x] `workspace_close` invoked; workspace reloaded for ongoing work.  
- [x] Concurrency matrix populated with **N/A** where parallel probes were impossible.  
- [x] Debug log section documents **no client-visible MCP log notifications**.  
- [x] Phase 18 addressed via prior MCP report + backlog **N/A** for MCP rows.

---

*End of audit file.*
