# MCP Server Audit Report

## Completion status (read first)

**Partial session.** The living prompt [`ai_docs/prompts/deep-review-and-refactor.md`](../prompts/deep-review-and-refactor.md) defines an **exhaustive** multi-phase audit (Phases 0–18, full coverage ledger, dual-mode concurrency pairing, and Phase 6 product refactors). This file records **evidence from a single Cursor agent session**: Phases **0**, **1** (mostly), **2** (start), **5** (complete), plus **spot checks** (`symbol_search`, `find_references`, `evaluate_csharp` env probe, `workspace_close`). **Phases 3–4, 6–4, 7–18 and full Phase 8b timing matrices were not executed** in this session. Remaining catalog entries require a **continuation run** (or multiple runs) using the same prompt; treat non-invoked tools as **not yet dispositioned** rather than “green.”

**Intended canonical store:** This audit targets solution `NetworkDocumentation.sln` in **DotNet-Network-Documentation**. If your rollup pipeline expects raw audits under **Roslyn-Backed-MCP**, copy this file there via `eng/import-deep-review-audit.ps1` when available.

## Header

- **Date:** 2026-04-08 (UTC run; filename timestamp `20260408T034457Z`)
- **Audited solution:** `c:\Code-Repo\DotNet-Network-Documentation\NetworkDocumentation.sln`
- **Audited revision:** `main` @ `8d103fe` (short); working tree had uncommitted `ai_docs` edits at run time
- **Entrypoint loaded:** `NetworkDocumentation.sln`
- **Audit mode:** `conservative` — shared developer workspace with pending local changes; no disposable worktree was created for this invocation. Write/apply families (Phase 6, 8b writers, 10–13 applies) were **not** exercised end-to-end here.
- **Isolation:** Shared path `c:\Code-Repo\DotNet-Network-Documentation` (rationale: conservative)
- **Client:** Cursor agent with `user-roslyn` MCP (`roslyn-mcp` host **1.6.1+f16452924ef03cd3ed17dc36b53efab29c72217a**)
- **Workspace id (released):** `242c3dec0b9d44efbf2025e7ad601979`
- **Server:** `server_info`: catalog **2026.03**; tools **56 stable / 67 experimental**; resources **7 stable**; prompts **16 experimental**; `runtime` **.NET 10.0.5**; `roslynVersion` **5.3.0.0**
- **Roslyn / .NET:** as above
- **Scale:** **8** projects, **361** documents (`workspace_load`)
- **Repo shape:** Multi-project (`Core`, `Parsers`, `Excel`, `Diagrams`, `Cli`, `Web`, `Reports`, `Tests`); **tests present**; **analyzers present** (Meziantou, Microsoft, Puma, etc.); **no** Central Package Management in tree; **single** target framework **net10.0** per project; **DI** in Web host; **source generators** not emphasized (confirm in continuation via `source_generated_documents`).
- **Prior issue source for Phase 18:** `ai_docs/backlog.md` — no Roslyn MCP defect rows; product backlog only. Prior MCP narrative: `ai_docs/reports/mcp-server-audit-report.md` (reference only).
- **Lock mode at audit time:** `rw-lock` (single-mode lane; Session B columns **skipped-pending-second-run**)
- **Lock mode source of truth:** `launch-config` / OS env (**User** + **Machine** `ROSLYNMCP_WORKSPACE_RW_LOCK` = `true`); **confirmed** by `workspace_status._meta.gateMode` = `rw-lock`; **confirmed** by `evaluate_csharp` returning `"true"` for `Environment.GetEnvironmentVariable("ROSLYNMCP_WORKSPACE_RW_LOCK")`
- **Debug log channel:** `no` — MCP `notifications/message` not surfaced in this Cursor agent transcript for verbatim capture
- **Auto-pair detection (Phase 0.16):** No `*networkdocumentation*legacy-mutex*_mcp-server-audit.md` partial found under `ai_docs/audit-reports/` → **run 1 of dual-mode pair** (if operator wants dual-mode matrix completion).
- **Report path note:** This is the **mode-1 partial** of a deep-review pair for **rw-lock**. To populate **Session B** (legacy-mutex) columns, run `./eng/flip-rw-lock.ps1` (Roslyn-Backed-MCP repo), restart the MCP client fully, and re-invoke the deep-review prompt against the **same** checkout — **or** accept single-mode lane for smoke audits.

## Phase notes (this session)

### Phase 0 — PASS/FLAG summary

| Checkpoint | Result |
|------------|--------|
| `server_info` vs catalog counts | **PASS** — 56+67=123 tools; 7 resources; 16 prompts; catalog version 2026.03 matches |
| `workspace_load` | **PASS** — 8 projects, 361 docs, empty `WorkspaceDiagnostics` |
| `workspace_list` / `workspace_status` | **PASS** — session visible; **not stale** |
| `project_graph` | **PASS** — dependency edges match expectation (Core ← others; Tests ← all) |
| `dotnet restore` (host shell) | **PASS** — `NetworkDocumentation.sln` restored cleanly |
| Resource `roslyn://server/catalog` | **PASS** |
| Resource `roslyn://server/resource-templates` | **PASS** — 7 URIs |
| Debug notifications | **FLAG** — not captured (client limitation for this session) |

### Phase 1 — Diagnostics / security

| Step | Result | Notes |
|------|--------|-------|
| `project_diagnostics` (full workspace, default page) | **PASS** — `totalDiagnostics=0`; **~12s** `heldMs` (large-solution scan — note performance) |
| `project_diagnostics` (scoped: project `NetworkDocumentation.Core`, severity `Warning`, limit 10) | **PASS** — stable paging; 0 diagnostics |
| `compile_check` (default pagination) | **PASS** — 0 CS diagnostics; **~77ms** |
| `compile_check` (`emitValidation=true`, project `NetworkDocumentation.Core`) | **PASS** — 0 diagnostics; **~571ms** (emit path still cheap when clean / scoped) |
| `security_diagnostics` | **PASS** — 0 findings; recommends `SecurityCodeScan.VS2019` missing |
| `security_analyzer_status` | **PASS** — Puma present; SecurityCodeScan absent |
| `nuget_vulnerability_scan` | **PASS** — 0 vulns; 8 projects; **~5.1s** |
| `list_analyzers` (paged + project filter) | **PASS** — **832** rules solution-wide page 1; **823** rules in test project slice; pagination metadata coherent |
| `diagnostic_details` | **N/A** — no diagnostics to target |

### Phase 2 — Quality metrics (partial)

| Tool | Result |
|------|--------|
| `get_complexity_metrics` | **PASS** — top hotspots include `QueryEvaluator.EvaluateComparison` (CC 24), parser methods CC 16–24, diagram builders CC 16–18; nesting depth flagged up to **5** in `EigrpParser.ParseShowEigrpTopology` / `L3DiagramBuilder` |

**Not run this session:** `get_cohesion_metrics`, `find_unused_symbols` (both), `get_namespace_dependencies`, `get_nuget_dependencies`.

### Phase 5 — Snippet & script

| Probe | Result |
|-------|--------|
| `analyze_snippet` `expression` `1+2` | **PASS** |
| `analyze_snippet` `program` small class | **PASS** — lists `C`, `X` |
| `analyze_snippet` broken `statements` | **PASS** — CS0029; **StartColumn=9** (user-relative; literal-aligned — matches FLAG-C fix expectation) |
| `analyze_snippet` `returnExpression` | **PASS** |
| `evaluate_csharp` LINQ sum | **PASS** — `55` |
| `evaluate_csharp` `int.Parse("abc")` | **PASS** — graceful runtime error string |
| `evaluate_csharp` infinite loop (`timeoutSeconds=2`) | **PASS** — abandoned after **12s** (2s budget + 10s grace); message actionable; `ProgressHeartbeatCount=6` |
| `evaluate_csharp` env `ROSLYNMCP_WORKSPACE_RW_LOCK` | **PASS** — `"true"` |

### Spot checks (Phase 3 / 8b probe planning)

| Tool | Result |
|------|--------|
| `symbol_search` `QueryEvaluator` | **PASS** |
| `find_references` (`QueryEvaluator` type handle, limit 200) | **PASS** — **11** refs; classifications (`Read`) present; **~426ms** |

**Note:** Deep-review template suggests R1 hot symbol **50+** refs; this repo’s `QueryEvaluator` has **11** — substitute symbol in a full 8b run.

### Phase 6 / 8 / 8b / 9–18

**Not executed** in this session (conservative + partial). `workspace_close` called after evidence capture.

## Coverage summary (approximate)

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|-----------------|-------------|--------------------|----------------|---------|-------|
| tool | mixed | 17 | 0 | 0 | 0 | 106 | 0 | Unique tools invoked; ~25 total MCP tool calls incl. repeats |
| resource | server/workspace/analysis | 2 | n/a | n/a | 0 | 0 | 5 | Workspaces/diagnostics/file URIs not re-read after close |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 | Not invoked — client/session scope |

*Blocked column is 0 — use **skipped-safety / not invoked** interpretation: 101 tools pending disposition in a follow-up run.*

## Coverage ledger — tools invoked this session

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| tool | `server_info` | stable | exercised | 0 | Counts match catalog |
| tool | `workspace_load` | stable | exercised | 0 | Clean load |
| tool | `workspace_list` | stable | exercised | 0 | |
| tool | `workspace_status` | stable | exercised | 0 | `gateMode: rw-lock` |
| tool | `project_graph` | stable | exercised | 0 | |
| tool | `project_diagnostics` | stable | exercised | 1 | Full scan ~12s |
| tool | `compile_check` | stable | exercised | 1 | Default + scoped emitValidation |
| tool | `security_diagnostics` | stable | exercised | 1 | |
| tool | `security_analyzer_status` | stable | exercised | 1 | |
| tool | `nuget_vulnerability_scan` | stable | exercised | 1 | |
| tool | `list_analyzers` | stable | exercised | 1 | Pagination + project filter |
| tool | `get_complexity_metrics` | experimental | exercised | 2 | |
| tool | `analyze_snippet` | stable | exercised | 5 | All probe kinds |
| tool | `evaluate_csharp` | experimental | exercised | 5 | Sum, error, timeout, env |
| tool | `symbol_search` | stable | exercised | 3-spot | |
| tool | `find_references` | stable | exercised | 3-spot | |
| tool | `workspace_close` | stable | exercised | close | |

## Coverage ledger — remaining catalog tools (123 − 17 = **106** not invoked)

> **Machine enumeration:** Tool names = `mcps/user-roslyn/tools/*.json` (123 files) in Cursor project cache. **Disposition:** `blocked` is **not** claimed — these are **pending continuation** (`deep-review` re-invocation). Do **not** treat as pass.

`add_central_package_version_preview`, `add_package_reference_preview`, `add_pragma_suppression`, `add_project_reference_preview`, `add_target_framework_preview`, `analyze_control_flow`, `analyze_data_flow`, `apply_code_action`, `apply_composite_preview`, `apply_multi_file_edit`, `apply_project_mutation`, `apply_text_edit`, `build_project`, `build_workspace`, `bulk_replace_type_apply`, `bulk_replace_type_preview`, `callers_callees`, `code_fix_apply`, `code_fix_preview`, `create_file_apply`, `create_file_preview`, `delete_file_apply`, `delete_file_preview`, `dependency_inversion_preview`, `diagnostic_details`, `document_symbols`, `enclosing_symbol`, `extract_and_wire_interface_preview`, `extract_interface_apply`, `extract_interface_cross_project_preview`, `extract_interface_preview`, `extract_type_apply`, `extract_type_preview`, `find_base_members`, `find_consumers`, `find_implementations`, `find_overrides`, `find_property_writes`, `find_references_bulk`, `find_reflection_usages`, `find_shared_members`, `find_type_mutations`, `find_type_usages`, `find_unused_symbols`, `fix_all_apply`, `fix_all_preview`, `format_document_apply`, `format_document_preview`, `format_range_apply`, `format_range_preview`, `get_code_actions`, `get_cohesion_metrics`, `get_completions`, `get_di_registrations`, `get_editorconfig_options`, `get_msbuild_properties`, `get_namespace_dependencies`, `get_nuget_dependencies`, `get_operations`, `get_source_text`, `get_syntax_tree`, `go_to_definition`, `goto_type_definition`, `impact_analysis`, `member_hierarchy`, `migrate_package_preview`, `move_file_apply`, `move_file_preview`, `move_type_to_file_apply`, `move_type_to_file_preview`, `move_type_to_project_preview`, `organize_usings_apply`, `organize_usings_preview`, `preview_code_action`, `remove_central_package_version_preview`, `remove_dead_code_apply`, `remove_dead_code_preview`, `remove_package_reference_preview`, `remove_project_reference_preview`, `remove_target_framework_preview`, `rename_apply`, `rename_preview`, `revert_last_apply`, `scaffold_test_apply`, `scaffold_test_preview`, `scaffold_type_apply`, `scaffold_type_preview`, `semantic_search`, `set_conditional_property_preview`, `set_diagnostic_severity`, `set_editorconfig_option`, `set_project_property_preview`, `source_generated_documents`, `split_class_preview`, `symbol_info`, `symbol_relationships`, `symbol_signature_help`, `test_coverage`, `test_discover`, `test_related`, `test_related_files`, `test_run`, `type_hierarchy`, `workspace_reload`.

*(Count verification: 123 total − 17 unique tools invoked = **106** pending.)*

## Resources & prompts

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| resource | `server_catalog` | stable | exercised | 0 | `roslyn://server/catalog` |
| resource | `resource_templates` | stable | exercised | 0 | |
| resource | `workspaces` | stable | not invoked | — | Pending |
| resource | `workspace_status` URI | stable | not invoked | — | Pending |
| resource | `workspace_projects` URI | stable | not invoked | — | Pending |
| resource | `workspace_diagnostics` URI | stable | not invoked | — | Pending |
| resource | `source_file` URI | stable | not invoked | — | Pending |
| prompt | `explain_error` … `consumer_impact` (16) | experimental | not invoked | — | Regenerate / invoke per Phase 16 in continuation |

## Verified tools (working)

- `server_info` — live surface matches expectations for 2026.03
- `workspace_load` / `workspace_list` / `workspace_status` / `project_graph` — coherent session + graph
- `project_diagnostics` — 0-issue workspace; slow first full scan (~12s)
- `compile_check` — fast CS-only check; scoped `emitValidation` behaved
- `security_*` + `nuget_vulnerability_scan` — structured empty results
- `list_analyzers` — 832 rules; paging works
- `get_complexity_metrics` — plausible CC/nesting for hotspots
- `analyze_snippet` / `evaluate_csharp` — including timeout + env visibility
- `symbol_search` / `find_references` — handle + classifications OK
- `workspace_close` — clean release

## Phase 6 refactor summary

- **Target repo:** DotNet-Network-Documentation (this workspace)
- **Scope:** **N/A** — no preview/apply refactor chains executed (conservative partial audit)
- **Changes:** none via MCP applies
- **Verification:** host `dotnet restore` **PASS**; in-memory diagnostics **PASS**

## Concurrency mode matrix (Phase 8b)

**N/A — partial session:** No sequential baseline, parallel fan-out, read/write exclusion, lifecycle stress, or writer reclassification timings were collected. **Session B** (`legacy-mutex`) columns remain **`skipped-pending-second-run`** per dual-mode procedure.

## Writer reclassification verification (Phase 8b.5)

**N/A** — not executed.

## Debug log capture

`client did not surface MCP log notifications`

## MCP server issues (bugs)

**No new defects observed** in the exercised subset. One **performance FLAG**: first unfiltered `project_diagnostics` took **~12s** (`heldMs` ~12024) on this 8-project / 361-document solution — acceptable for cold graph but worth tracking if repeated probing is needed.

## Improvement suggestions

- Consider raising operator visibility into `gateMode` / lock mode on **`server_info`** (prompt notes `server_info` omits `ROSLYNMCP_WORKSPACE_RW_LOCK`; env + `evaluate_csharp` workaround works but is clunky)
- **`nuget_vulnerability_scan`**: `IncludesTransitive: false` — document implication for supply-chain audits

## Performance observations

| Tool | Input scale | Lock mode | Wall-clock (ms) | Notes |
|------|-------------|-----------|-------------------|-------|
| `project_diagnostics` | full solution | rw-lock | ~12024 | First page (0 issues) |
| `nuget_vulnerability_scan` | 8 projects | rw-lock | ~5083 | |
| `find_references` | `QueryEvaluator` | rw-lock | ~426 | 11 refs |
| `compile_check` | full (default) | rw-lock | ~77 | |
| `get_complexity_metrics` | default top50 | rw-lock | ~327 | |

## Known issue regression check

**N/A** — `ai_docs/backlog.md` contains **no** Roslyn MCP regression ids; no prior MCP bug backlog cross-walk performed beyond archival reference to `ai_docs/reports/mcp-server-audit-report.md`.

## Known issue cross-check

- None new vs prior audit docs in this short run.

---

*Generated by executing `ai_docs/prompts/deep-review-and-refactor.md` through Phase 0–2/5 scope in one agent session. **Completion gate:** full prompt compliance requires additional sessions.*
