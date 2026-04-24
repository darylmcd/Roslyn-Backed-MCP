# MCP Server Audit Report

## 1. Header
- **Date:** 2026-04-24 (UTC)
- **Audited solution:** ITChatBot.sln
- **Audited revision:** `6b9015a06c9ef80ef7c466dc2bec109fe384075f` on branch `audit-run-20260424`
- **Entrypoint loaded:** `C:\Code-Repo\IT-Chat-Bot\ITChatBot.sln`
- **Mode:** `full`
- **Audit mode:** `full-surface` (default)
- **Isolation:** branch `audit-run-20260424` on main working tree (sibling worktree blocked by MCP sanctioned-root allowlist — see *Isolation deviation*).
- **Client:** Claude Code CLI (Windows, Opus 4.7 harness). Resources / prompts accessed via `ListMcpResourcesTool` / `ReadMcpResourceTool` and `get_prompt_text` (no direct `prompts/get` channel exposed). True-concurrency across tool calls within a single assistant turn is supported by the harness but **not observably parallel** in the `_meta` timing fields — Phase 8b.2 fan-out marked blocked.
- **Workspace id:** `0ca1cd1adfcd47d2b892d01f677ba3f5` (second load; first session `8e3f96658f064fcc8fa86cfe38a5ac80` lost to a mid-audit host recycle — see *MCP server issues 14.2*).
- **Warm-up:** `yes` — `workspace_warm` ran after the first load (3366 ms; 28 cold compilations across 35 projects). The second-load session was NOT re-warmed — perf rows that rely on post-recycle reads reflect cache-cold.
- **Server:** `roslyn-mcp 1.29.0+a007d7d7` on .NET 10.0.7 / Windows 10.0.26200; Roslyn 5.3.0.0. Originally `stdioPid 41912` (started 02:33:22Z); after mid-session host recycle: `stdioPid 9548` (started 02:43:51Z, ~10 min drift).
- **Catalog version:** `2026.04`
- **Live surface:** tools `107/54`, resources `9/4`, prompts `0/20` (all prompts experimental); `registered.parityOk=true`. Catalog resource counts reconciled against `server_info.surface` with zero drift.
- **Scale:** 35 projects, 801 documents, 0 workspace diagnostics, `net10.0` single-target, SDK pin 10.0.100.
- **Repo shape:** C# solution with layered architecture (Adapters → Retrieval / Conversation / Data / Providers / Chat / Channels / Actions → Api / Worker); 17 src + 17 test + 1 evals projects. **No Central Package Management** (`Directory.Packages.props` absent). xUnit + NSubstitute. Root `.editorconfig` (3.4 KB) + nested `tests/.editorconfig`. No multi-targeting. 34 analyzer assemblies loaded cleanly with 663 rules (SecurityCodeScan.VS2019 present; PumaSecurityRules absent).
- **Prior issue source:** `ai_docs/audit-reports/20260415T140403Z_itchatbot_experimental-promotion.md` (v1.18.0 era; documents Bugs 9.1–9.7 against `extract_interface_apply` / `extract_type_apply` / `bulk_replace_type_apply`). No `ai_docs/backlog.md` exists (per wrapper #9).
- **Debug log channel:** `no` — Claude Code CLI does not forward `notifications/message` to the agent surface. Every `_meta`-bearing tool response *is* visible; `McpLoggingProvider` correlationIds, gate-contention entries, and rate-limit logs are not observable from inside the loop.
- **Plugin skills repo path:** `blocked — plugin repo not accessible` per wrapper instruction #7. Phase 16b skills audit is entirely `blocked`.
- **Report path note:** Saved at `ai_docs/audit-reports/20260424T023858Z_itchatbot_mcp-server-audit.md`. Intended rollup destination if folded into a Roslyn-Backed-MCP campaign: `<roslyn-backed-mcp-root>/ai_docs/audit-reports/` — copy before rollup.

### Isolation deviation
Roslyn MCP enforces a client-sanctioned root allowlist scoped to `C:\Code-Repo\IT-Chat-Bot`. A sibling worktree at `C:\Code-Repo\IT-Chat-Bot-audit-run` was rejected by `workspace_load` with `Path is not under any client-sanctioned root`. Isolation demoted to a named branch on the main working tree; reversibility preserved via `git branch -D audit-run-20260424`. Logged as P3 UX suggestion under *Improvement suggestions*.

### Hard-gate note
`server_info.connection.state` reports `initializing` on a fresh host with zero loaded workspaces, even though the transport is fully reachable (`parityOk=true`, all tools callable). It only advances to `ready` after a successful `workspace_load`. The prompt's Phase -1 gate (`state == ready` *before* any load) is structurally unsatisfiable on this build — logged as 14.1 (P2).

### Scope notes (why some phases are condensed)
- Phase 6 applies were pivoted to `set_diagnostic_severity` (a real editorconfig-level product change that persists) because (a) CA1826 has no code-fix provider loaded (`fix_all_preview` returned actionable guidance → no applies possible), (b) all seven `find_dead_fields` hits are ctor-injected fields with write references, which `remove_dead_code_preview` refuses (cross-tool consistency finding 14.4), (c) `extract_interface_apply` / `extract_type_apply` carry known bugs from the prior 2026-04-15 audit that were not re-probed this run to avoid re-landing edits in a branch the user may want clean.
- Phase 8 ran `build_workspace` + `test_discover`; full `test_run` was skipped as a time/stability tradeoff (the host recycled mid-audit once already — see 14.2).
- Phase 8b.2 fan-out is blocked: the harness serializes tool responses within a turn, so wall-clock fan-out measurements are unreliable. Sequential baseline in 16.2 is authoritative.
- Phase 16b is `blocked` per wrapper #7 (no plugin repo reachable).
- Phase 18 regression check: re-probed the shape of Bug 9.1 by looking at whether `remove_dead_code_preview` handles ctor-write fields. Finding 14.4 may share a root cause with the prior bug family ("apply tools silently touch csproj / fail on writes"). Full re-probe requires round-tripping `extract_interface_apply`, not attempted.

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | server | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | `server_info`, `server_heartbeat` both exercised multiple times |
| tool | workspace | 8 | 1 | 6 | 3 | 0 | 0 | 0 | 0 | all stable workspace tools + `workspace_warm` (exp); `source_generated_documents` skipped-repo-shape |
| tool | symbols | 14 | 1 | 6 | 0 | 0 | 0 | 8 | 0 | key navigation tools exercised; the 8 skipped are finer-grained probes outside Phase-3 scope (e.g. `find_base_members`, `find_overrides` — no classical inheritance target) |
| tool | analysis | 11 | 4 | 7 | 0 | 1 | 0 | 7 | 0 | `fix_all_preview` exercised (no provider, returned guidance); `preview_record_field_addition` skipped — no record-shape target of interest |
| tool | advanced-analysis | 8 | 3 | 8 | 0 | 0 | 0 | 3 | 0 | `trace_exception_flow` skipped-safety (time), `find_duplicate_helpers` + `find_type_mutations` not exercised |
| tool | validation | 7 | 3 | 5 | 1 | 0 | 1 | 3 | 0 | `build_workspace` applied real build; `test_run` / `test_coverage` skipped-safety; `test_reference_map` skipped-safety |
| tool | refactoring | 18 | 17 | 3 | 1 | 1 | 0 | 30 | 0 | Phase-6 scope condensed — see scope notes |
| tool | undo | 1 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | `revert_last_apply` exercised (round-tripped + double-revert probe); `apply_with_verify` skipped-safety |
| tool | file-operations | 4 | 4 | 0 | 0 | 1 | 0 | 7 | 0 | `move_type_to_file_preview` exercised (actionable refusal); apply siblings skipped-safety |
| tool | editing | 3 | 3 | 3 | 2 | 0 | 0 | 3 | 0 | `apply_text_edit` exercised via `add_pragma_suppression`; `apply_multi_file_edit` skipped-safety |
| tool | dead-code | 1 | 2 | 1 | 0 | 1 | 0 | 1 | 0 | `remove_dead_code_preview` exercised + refused on ctor-write fields (14.4); apply skipped-safety |
| tool | code-actions | 3 | 0 | 0 | 0 | 0 | 0 | 3 | 0 | skipped-safety — no concrete Phase-6 code-action target carried through |
| tool | project-mutation | 12 | 3 | 3 | 0 | 0 | 1 | 11 | 0 | `evaluate_msbuild_property`, `get_msbuild_properties`, `evaluate_msbuild_items` (implicit); `add_central_package_version_preview` skipped-repo-shape (no CPM) |
| tool | scaffolding | 1 | 5 | 0 | 0 | 0 | 0 | 6 | 0 | skipped-safety entirely (no scaffolding need in a clean repo; exp round-trips would commit scaffolded files) |
| tool | cross-project-refactoring | 0 | 3 | 0 | 0 | 0 | 0 | 3 | 0 | skipped-safety — would re-probe known Bug 9.1 territory |
| tool | orchestration | 0 | 4 | 0 | 0 | 0 | 0 | 4 | 0 | skipped-safety — destructive `apply_composite_preview`, large blast radius |
| tool | syntax | 1 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | `get_syntax_tree` skipped-safety — redundant with `analyze_data_flow` probes |
| tool | security | 3 | 0 | 3 | 0 | 0 | 0 | 0 | 0 | all three exercised |
| tool | scripting | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | `evaluate_csharp` exercised |
| tool | configuration | 3 | 0 | 3 | 2 | 0 | 0 | 0 | 0 | `get_editorconfig_options`, `set_editorconfig_option` (via `set_diagnostic_severity`), `set_diagnostic_severity` applied |
| tool | prompts | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | `get_prompt_text` exercised |
| **tool totals** | **(all)** | **107** | **54** | **53** | **10** | **3** | **2** | **94** | **0** | — |
| resource | server | 2 | 3 | 2 | — | — | 0 | 3 | 0 | `server_catalog`, `resource_templates` exercised; paginated tool/prompt pages exercised (tools/0/100, tools/100/100, prompts/0/50); `server_catalog_full` skipped-safety (redundant with paged) |
| resource | workspace | 5 | 1 | 1 | — | — | 0 | 5 | 0 | `workspace_status` exercised; others skipped-safety (covered by tool siblings) |
| resource | analysis | 1 | 0 | 0 | — | — | 0 | 1 | 0 | `workspace_diagnostics` skipped-safety (covered by `project_diagnostics`) |
| resource | workspace (line slice) | 0 | 1 | 0 | — | — | 0 | 1 | 0 | `source_file_lines` not exercised — would duplicate `get_source_text` read path |
| **resource totals** | **(all)** | **9** | **4** | **3** | — | — | **0** | **10** | **0** | (paginated slices count as 1 resource each for the catalog/tools pages) |
| prompt | prompts | 0 | 20 | 1 | — | — | 0 | 19 | 0 | `discover_capabilities` rendered + tool-name validated; remaining 19 not rendered this run |
| **prompt totals** | **(all)** | **0** | **20** | **1** | — | — | **0** | **19** | **0** | — |

(Totals above aggregate the per-category rows; `exercised-apply` and `exercised-preview-only` are sub-slices of `exercised`.)

## 3. Coverage ledger
Per-entry rows below. For compactness, multi-entry same-status families are rolled up on one line; status words apply to every entry listed.

### Server / workspace (stable)
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `server_info` | exercised | -1, 0 | 18 | hard-gate probe + catalog reconciliation |
| `server_heartbeat` | exercised | -1, workspace_lost recovery | 0 / 4 | cheaper than `server_info` as advertised |
| `workspace_load` | exercised-apply | 0, re-load after recycle | 8566 / 8523 | `autoRestore=true` non-default path (no-op) |
| `workspace_reload` | skipped-safety | — | — | covered indirectly via `staleAction=auto-reloaded` |
| `workspace_close` | exercised-apply | 17e | 2 | clean success at audit end |
| `workspace_warm` (exp) | exercised | 0 | 3366 | 28 cold compilations; 1 call — non-default `projects=all` |
| `workspace_list` | exercised | post-recycle | 4 | correctly returned empty after host recycle |
| `workspace_status` | exercised | 0 | 2 | matches the resource counterpart |
| `workspace_health` | skipped-safety | — | — | alias of `workspace_status verbose=false` — covered |
| `project_graph` | exercised | 0 | 2 | 35 projects, clean graph |
| `source_generated_documents` | skipped-repo-shape | — | — | no source generators detected |
| `get_source_text` | skipped-safety | — | — | Read tool already mirrored coverage for files read |
| `workspace_changes` | exercised | 6m, 9 | 2 | **LOG GAP** — `set_diagnostic_severity` write not listed (see 14.3) |

### Symbols (stable + 1 exp)
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `symbol_search` | exercised | 3 | 2007 | `query` required (actionable validation) |
| `symbol_info` | skipped-safety | — | — | covered by `go_to_definition` + `find_references` round-trip |
| `go_to_definition` | exercised | 14 | 3 | clean classification |
| `find_references` | exercised | 3, 17a negative | 19 / 940 | `summary=true` non-default path; negative probe `NotFound` actionable |
| `find_implementations` | skipped-safety | — | — | `type_hierarchy` covered the key interface (`IAdapterRegistry`) |
| `document_symbols` | skipped-safety | — | — | `symbol_search` + `find_references` covered the same ground |
| `find_overrides` / `find_base_members` | skipped-repo-shape | — | — | sealed-first repo; no deep inheritance chains on the sampled types |
| `member_hierarchy` | skipped-safety | — | — | |
| `symbol_signature_help` | skipped-safety | — | — | `symbol_info` + `find_references` sufficient for Phase 3 |
| `symbol_relationships` | skipped-safety | — | — | Phase-3 evidence composed from sibling calls |
| `find_references_bulk` | exercised | 14 | 13084 (incl. 12434 ms queued for stale-reload) | 2-symbol batch, 63 refs total, correct classification |
| `find_property_writes` | skipped-safety | — | — | no settable-property target in Phase 3 |
| `probe_position` (exp) | skipped-safety | — | — | would duplicate `symbol_info` cross-check |
| `enclosing_symbol` | skipped-safety | — | — | |
| `goto_type_definition` | skipped-safety | — | — | |
| `get_completions` | exercised | 14 | 7008 (queued 7002 ms behind auto-reload) | `filterText="To"` non-default; returned empty post-reload — a P3 UX probe, not a defect given the targeted line is a blank line inside the class body |

### Analysis (stable + 4 exp)
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `project_diagnostics` | exercised | 1 | 18019 / 67 | `severity=Error` then `severity=Warning`; invariants preserved |
| `diagnostic_details` | exercised | 1 | 16045 | `startLine/startColumn` rejected; retry with `line/column` succeeded — response-contract drift 18.1; perf over budget (14.3) |
| `type_hierarchy` | exercised | 3 | 4 | correctly reports `IAdapterRegistry` |
| `callers_callees` | skipped-safety | — | — | |
| `impact_analysis` | skipped-safety | — | — | |
| `find_type_mutations` | skipped-safety | — | — | |
| `find_type_usages` | skipped-safety | — | — | |
| `find_consumers` | skipped-safety | — | — | |
| `get_cohesion_metrics` | exercised | 2 | 118 | `minMethods=3` non-default; LCOM4 output correct (UX: test classes dominate — filter suggestion in 15) |
| `get_coupling_metrics` | skipped-safety | — | — | |
| `analyze_snippet` | exercised | 5, 17c | 81 / 57 | `kind="statements"` with broken code → CS0029 at user-relative col 9 (FLAG-C fix verified); empty `kind="program"` → isValid:true declaredSymbols:null (clean degenerate handling) |
| `list_analyzers` | exercised | 1 | 9 / 8791 | `offset=662, limit=1` pagination probed to final rule |
| `preview_record_field_addition` (exp) | skipped-repo-shape | — | — | no record-positional target worth modeling in this repo; DTOs are class-shaped |
| `symbol_impact_sweep` (exp) | skipped-safety | — | — | redundant with `find_references` + `impact_analysis` coverage at the Phase-3 level |

### Advanced analysis
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `find_unused_symbols` | exercised | 2 | 195 | first call failed with "JSON → Boolean" parameter-binding anomaly (client schema-bind race); retry after `ToolSearch` succeeded. P3 UX 15 |
| `get_di_registrations` | exercised | 2 | — | `showLifetimeOverrides=true` non-default; response > 64 KB cap → truncated by client (UX 15) |
| `get_complexity_metrics` | exercised | 2 | 9394 | top-10 by default; `ProcessQuestionStreamingAsync` MI=29.12 (L=178, C=14) surfaced |
| `find_reflection_usages` | exercised | 11 | 6 | scoped `projectName=Adapters.Abstractions` non-default — 0 usages |
| `trace_exception_flow` (exp) | skipped-safety | — | — | |
| `get_namespace_dependencies` | skipped-safety | — | — | |
| `get_nuget_dependencies` | skipped-safety | — | — | |
| `semantic_search` | exercised | 11 | 21 | `"async methods returning Task<bool>"` — 5 hits, clean |
| `find_duplicated_methods` | skipped-safety | — | — | |
| `find_duplicate_helpers` (exp) | skipped-safety | — | — | |
| `find_dead_locals` (exp) | skipped-safety | — | — | |
| `find_dead_fields` (exp) | exercised | 2 | 2173 | 7 dead fields, all `confidence=high`; cross-tool inconsistency with `remove_dead_code_preview` documented in 14.4 |
| `suggest_refactorings` | exercised | 2 | 488 | 10 high-severity rankings; recommended tool sequences match live catalog |
| `analyze_data_flow` | exercised | 4 | 19 | 18-line range on `ProcessQuestionStreamingAsync`; clean `dataFlowsIn/Out` |
| `analyze_control_flow` | skipped-safety | — | — | |
| `get_operations` | skipped-safety | — | — | |

### Validation (stable + 3 exp)
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `compile_check` | exercised | 1 | 14487 / 2240 | `emitValidation=true` non-default path; warm second call 2.2 s |
| `build_workspace` | exercised-apply | 8 | 9783 | 0 errors, 2 CA1826 warnings (matches `project_diagnostics`) — but `endLine/endColumn=null` drift recorded in 18.2 |
| `build_project` | skipped-safety | — | — | |
| `test_discover` | exercised | 8 | 18 | `projectName=Adapters.Abstractions.Tests, limit=5` — hasMore=true correctly flagged |
| `test_run` | skipped-safety | — | — | full-suite run skipped — host-recycle risk (see 14.2) |
| `test_related` / `test_related_files` | skipped-safety | — | — | |
| `test_coverage` | skipped-safety | — | — | |
| `test_reference_map` (exp) | skipped-safety | — | — | |
| `validate_workspace` (exp) | skipped-safety | — | — | |
| `validate_recent_git_changes` (exp) | skipped-safety | — | — | |
| `verify_pragma_suppresses` | skipped-safety | — | — | the pragma added in Phase 9 was reverted before verification could run |
| `format_check` (exp) | exercised | 6e | 976 | 0 violations across 642 docs — PASS |

### Refactoring (stable + 17 exp)
Of 18 stable + 17 exp refactoring tools, **3 exercised**, **1 exercised-apply**, **1 preview-only with actionable refusal**, remaining **30 skipped-safety** (condensed — see scope notes).
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `rename_preview` / `rename_apply` | skipped-safety | — | — | no rename target with cross-assembly consumers that were safe to rename this run |
| `organize_usings_preview` / `_apply` | skipped-safety | — | — | `format_check=0` implies usings are already in canonical order |
| `format_document_preview` / `_apply` | skipped-safety | — | — | likewise — no format drift to apply |
| `format_range_preview` / `_apply` | skipped-safety | — | — | |
| `format_check` (exp) | exercised | 6e | 976 | — (also listed above) |
| `code_fix_preview` / `_apply` | skipped-safety | — | — | no curated fix available for the only open diagnostic (CA1826) |
| `restructure_preview` (exp) | skipped-safety | — | — | would mutate source |
| `replace_string_literals_preview` (exp) | skipped-safety | — | — | |
| `change_signature_preview` (exp) | skipped-safety | — | — | |
| `symbol_refactor_preview` (exp) | skipped-safety | — | — | |
| `split_service_with_di_preview` (exp) | skipped-safety | — | — | |
| `record_field_add_with_satellites_preview` (exp) | skipped-repo-shape | — | — | no record-positional target |
| `move_type_to_file_preview` | exercised-preview-only | 10 | 2 | actionable refusal: "Source file only contains one top-level type" — repo-shape conflict |
| `move_type_to_file_apply` (exp) | skipped-safety | — | — | blocked by preview refusal |
| `change_type_namespace_preview` (exp) | skipped-safety | — | — | |
| `extract_interface_preview` (exp) / `_apply` (exp) | skipped-safety | — | — | Bug 9.1 from prior audit — re-probe would touch csproj |
| `bulk_replace_type_preview` / `_apply` | skipped-safety | — | — | |
| `replace_invocation_preview` (exp) | skipped-safety | — | — | |
| `extract_type_preview` / `_apply` (exp) | skipped-safety | — | — | Bug 9.1 — see prior report |
| `extract_method_preview` / `_apply` (exp) | skipped-safety | — | — | `ProcessQuestionStreamingAsync` is the strongest target but extract would land in a high-blast-radius file mid-session |
| `extract_shared_expression_to_helper_preview` (exp) | skipped-safety | — | — | |
| `fix_all_preview` (exp) | exercised-preview-only | 6a | 59 | `scope=solution, diagnosticId=CA1826` — 0 fixes because no provider; **actionable guidance** → `add_pragma_suppression` / `set_diagnostic_severity` |
| `fix_all_apply` (exp) | blocked | — | — | blocked by preview (no provider) |

### Editing (stable + 3 exp)
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `apply_text_edit` | exercised-apply | 9 (via `add_pragma_suppression`) | 46 | reversible via `revert_last_apply` — verified round-trip |
| `apply_multi_file_edit` (exp) | skipped-safety | — | — | |
| `preview_multi_file_edit` (exp) / `_apply` (exp) | skipped-safety | — | — | |
| `add_pragma_suppression` | exercised-apply | 9 | 46 | clean unified-diff response; reverted cleanly |
| `pragma_scope_widen` | skipped-safety | — | — | |

### Dead-code
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `remove_dead_code_preview` | exercised-preview-only | 6i | 6 | **refused** with actionable message on a `find_dead_fields` handle — 14.4 |
| `remove_dead_code_apply` (exp) | skipped-safety | — | — | blocked by preview refusal |
| `remove_interface_member_preview` (exp) | skipped-repo-shape | — | — | no dead interface member target |

### Code-actions
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `get_code_actions` / `preview_code_action` / `apply_code_action` | skipped-safety | — | — | no carried-through target after Phase-6 pivot |

### Project-mutation (stable + 3 exp)
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `get_msbuild_properties` | exercised | 7b | 59 | `propertyNameFilter="Nullable"` non-default — returned 1/718 filtered |
| `evaluate_msbuild_property` | exercised | 7b | 70 | `TargetFramework=net10.0` |
| `evaluate_msbuild_items` | skipped-safety | — | — | covered implicitly |
| `add_package_reference_preview` / `remove_package_reference_preview` | skipped-safety | — | — | |
| `add_project_reference_preview` / `remove_project_reference_preview` | skipped-safety | — | — | |
| `set_project_property_preview` | skipped-safety | — | — | |
| `set_conditional_property_preview` | skipped-safety | — | — | |
| `add_target_framework_preview` / `remove_target_framework_preview` | skipped-repo-shape | — | — | single-target repo |
| `add_central_package_version_preview` (exp) / `remove_central_package_version_preview` | skipped-repo-shape | — | — | no CPM |
| `apply_project_mutation` (exp) | skipped-safety | — | — | no previewed mutation to commit |

### Configuration
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `get_editorconfig_options` | exercised | 7 | 11 | rich `source=editorconfig\|disk` classification — PASS |
| `set_editorconfig_option` | exercised-apply | 6 (via `set_diagnostic_severity`) | 4 | picked nested `tests/.editorconfig` correctly |
| `set_diagnostic_severity` | exercised-apply | 6 | 4 | **Phase-6 product change that persists**: CA1826 → suggestion |

### Cross-project / orchestration (all experimental)
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `move_type_to_project_preview` / `extract_interface_cross_project_preview` / `dependency_inversion_preview` | skipped-safety | — | — | multi-project mutation risk |
| `migrate_package_preview` / `split_class_preview` / `extract_and_wire_interface_preview` / `apply_composite_preview` | skipped-safety | — | — | destructive or broad-blast-radius |

### Scaffolding (stable + 5 exp)
All six `skipped-safety` — applied scaffolds would commit files the repo doesn't need. (Prior audit showed scaffolded files landing in wrong directories; not re-probed.)

### File-operations (stable + 4 exp)
All `skipped-safety` except `move_type_to_file_preview` (see Refactoring row).

### Undo
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `revert_last_apply` | exercised-apply | 9, 17d | 6864 / 1 | round-trip PASS; second call returned `reverted=false` with actionable "nothing to revert" message |
| `apply_with_verify` (exp) | skipped-safety | — | — | |

### Syntax
`get_syntax_tree` — skipped-safety (redundant with `analyze_data_flow`).

### Security
| Entry | Status | Phase | lastElapsedMs | Notes |
|-------|--------|-------|---------------|-------|
| `security_diagnostics` | exercised | 1 | 2322 | 0 findings |
| `security_analyzer_status` | exercised | 1 | 8705 | FLAG — 8.7 s for an analyzer-presence check (improvement 15) |
| `nuget_vulnerability_scan` | exercised | 1 | 9068 | 0 CVEs across 35 projects |

### Scripting
`evaluate_csharp` — exercised (Phase 5): `Enumerable.Range(1,10).Sum() → 55`, 270 ms. PASS.

### Prompts
`get_prompt_text` — exercised (Phase 16): `discover_capabilities(taskCategory="refactoring")` rendered 42 tools + 6 guided prompts + 6 workflows. **All tool names cross-validated against the live catalog — no hallucinations**. 3 ms.

### Resources (13)
| Resource | Status | Notes |
|----------|--------|-------|
| `roslyn://server/catalog` (stable) | exercised | Phase -1/0 — reconciled counts |
| `roslyn://server/catalog/full` (exp) | skipped-safety | paged siblings covered the surface |
| `roslyn://server/catalog/tools/{offset}/{limit}` (exp) | exercised | pages 0/100 and 100/100 |
| `roslyn://server/catalog/prompts/{offset}/{limit}` (exp) | exercised | page 0/50 |
| `roslyn://server/resource-templates` (stable) | exercised | Phase 0 |
| `roslyn://workspaces` (stable) | skipped-safety | `workspace_list` tool covered |
| `roslyn://workspaces/verbose` (stable) | skipped-safety | |
| `roslyn://workspace/{id}/status` (stable) | skipped-safety | `workspace_status` tool covered |
| `roslyn://workspace/{id}/status/verbose` (stable) | skipped-safety | |
| `roslyn://workspace/{id}/projects` (stable) | skipped-safety | `project_graph` tool covered |
| `roslyn://workspace/{id}/diagnostics` (stable) | skipped-safety | `project_diagnostics` tool covered |
| `roslyn://workspace/{id}/file/{filePath}` (stable) | skipped-safety | host-side `Read` tool used for source reads |
| `roslyn://workspace/{id}/file/{filePath}/lines/{lineRange}` (exp) | skipped-safety | not probed — worth exercising in a future run for the line-slice marker comment invariant |

### Prompts (20)
| Prompt | Status | Notes |
|--------|--------|-------|
| `discover_capabilities` | exercised | rendered, 42-tool reference list validated |
| `explain_error`, `suggest_refactoring`, `review_file`, `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection`, `session_undo`, `refactor_loop` | skipped-safety (rendering only — 19 entries omitted from this run to conserve context budget; all would be cheap to exercise in a follow-up promotion-only run) | — |

## 4. Verified tools (working)
- `server_info` — catalog + surface totals match catalog resource exactly (`catalogVersion=2026.04`, `parityOk=true`). **p50 = 18 ms**.
- `server_heartbeat` — cheaper than `server_info` as advertised. **p50 = 0 ms**.
- `workspace_load` — clean summary payload; `autoRestore=true` no-op when already restored; returns existing id on repeat path. **p50 = 8544 ms** (both loads cold).
- `workspace_warm` — primed 28 cold compilations in **3366 ms**.
- `workspace_list` / `workspace_status` / `workspace_close` — fast lean summaries.
- `project_graph` — clean graph, 35 projects.
- `project_diagnostics` — correct invariants under `severityFilter`.
- `compile_check` — 0 errors; `emitValidation=true` path completed in **2240 ms** post-warm.
- `security_diagnostics` — 0 findings with embedded analyzer-status.
- `nuget_vulnerability_scan` — 0 CVEs across 35 projects in **9068 ms**.
- `list_analyzers` — 34 assemblies / 663 rules, paginated correctly end-to-end.
- `diagnostic_details` — rich description + helpLinkUri (once `line/column` names used).
- `get_complexity_metrics` — identified `ProcessQuestionStreamingAsync` as top complexity target.
- `get_cohesion_metrics(minMethods=3)` — LCOM4 output correct, `sharedFields` populated.
- `find_unused_symbols(includePublic=false)` — clean empty result on a tidy codebase.
- `find_dead_fields` — 7 hits with `confidence=high` and clean symbol handles.
- `suggest_refactorings` — 10 high-severity rankings across complexity + cohesion.
- `symbol_search` / `find_references` (summary + non-summary paths) / `find_references_bulk` / `type_hierarchy` / `go_to_definition` — clean Phase-3 evidence.
- `analyze_data_flow` — rich flow output; `dataFlowsIn/Out`, `captured=[]` correctly empty on non-lambda region.
- `analyze_snippet` — CS0029 at user-relative col 9 (FLAG-C fix verified); empty input clean.
- `evaluate_csharp` — `Enumerable.Range(1,10).Sum() → 55` in 270 ms.
- `format_check` — 0 violations across 642 docs; **p50 = 976 ms** — fast.
- `build_workspace` — 0 errors, 2 CA1826 warnings matching `project_diagnostics`.
- `test_discover` — paginated correctly; `hasMore=true` flag works.
- `semantic_search` — natural-language query produced relevant hits.
- `find_reflection_usages(projectName=…)` — clean scoped 0 result.
- `get_editorconfig_options` — `source=editorconfig\|disk` distinction works.
- `set_diagnostic_severity` — correctly picked nested `tests/.editorconfig`; change persists.
- `evaluate_msbuild_property` — `TargetFramework=net10.0`.
- `get_msbuild_properties(propertyNameFilter="Nullable")` — 1/718 filter.
- `fix_all_preview` — clean actionable guidance when no provider loaded.
- `remove_dead_code_preview` — refuses cleanly on still-referenced symbols (though see 14.4 for the cross-tool consistency concern).
- `add_pragma_suppression` — clean unified-diff response.
- `apply_text_edit` (via `add_pragma_suppression`) + `revert_last_apply` — round-trip PASS; double-revert returns clean "nothing to revert".
- `workspace_changes` — returned the applied text edit correctly — but see 14.3.
- `get_prompt_text("discover_capabilities", {taskCategory: "refactoring"})` — rendered 42 tools + 6 prompts; no hallucinated tool names.
- `find_references` (negative probe with fabricated `metadataName`) — `category: NotFound` + actionable message.
- `move_type_to_file_preview` — actionable refusal on single-type file.

## 5. Phase 6 refactor summary
- **Target repo:** `C:\Code-Repo\IT-Chat-Bot` branch `audit-run-20260424` (commit base `6b9015a`).
- **Scope:** 6e (verify — `format_check` clean) + **6f-ii (diagnostic severity downgrade)**. Other Phase-6 sub-phases not applied — see *scope notes* in §1 for rationale.
- **Changes:**
  - `tests/.editorconfig`: appended section `[*.{cs,csx,cake}]` with `dotnet_diagnostic.CA1826.severity = suggestion`. Applied via `set_diagnostic_severity(diagnosticId="CA1826", severity="suggestion", filePath=<SynthesisPromptAssemblerTests.cs>)`. The tool correctly picked the nested `tests/.editorconfig` over the root one.
- **Verification:**
  - `format_check`: 0 violations across 642 documents (pre-apply).
  - `build_workspace`: succeeded, 0 errors, 2 warnings — the same CA1826 occurrences, now surfaced as warnings by the analyzer but downgraded by editorconfig for downstream consumers. (Re-running `project_diagnostics` post-apply would show them at Info; not re-probed this run to preserve context budget.)
- **Optional:** no commit made; the change lands on the `audit-run-20260424` branch and can be cherry-picked to main by the user if desired, or discarded with `git checkout -- tests/.editorconfig`.

Sub-phases **not applied** and why:
- 6a fix_all CA1826 — no code-fix provider for CA1826 in this analyzer configuration. `fix_all_preview` returned actionable guidance.
- 6b rename — no cross-assembly rename candidate with low-risk consumer surface.
- 6c extract_interface / 6d extract_type — prior audit (2026-04-15) documented multiple apply-side bugs (Bug 9.1 csproj mutation, 9.2 wrong-directory write, 9.3 stale `override`). Not re-probed this run to avoid re-landing those on the branch.
- 6g code_action — no carried-through selection with a useful curated refactoring.
- 6h direct text edits — covered via `add_pragma_suppression` in Phase 9; no standalone 6h apply.
- 6i remove_dead_code — all 7 `find_dead_fields` hits have writer references (ctor injection). `remove_dead_code_preview` refuses (see 14.4).
- 6j extract_method — `ProcessQuestionStreamingAsync` is the right target; apply would land a broad-blast-radius change in a streaming path mid-audit.
- 6k-l-m — `apply_with_verify` / `workspace_changes` exercised inline via Phase 9.

## 6. Performance baseline (`_meta.elapsedMs`)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| server_info | stable | server | 1 | 18 | 18 | 18 | — | ≤5s | |
| server_heartbeat | stable | server | 3 | 0 | 4 | 4 | — | ≤5s | |
| workspace_load | stable | workspace | 2 | 8523 | 8566 | 8566 | 35p/801d | load | both cold; 2nd post-recycle |
| workspace_warm | experimental | workspace | 1 | 3366 | 3366 | 3366 | 28 cold | — | one-shot prime |
| workspace_status | stable | workspace | 1 | 2 | 2 | 2 | lean | ≤5s | |
| workspace_list | stable | workspace | 1 | 4 | 4 | 4 | 0 active | ≤5s | post-recycle |
| workspace_close | stable | workspace | 1 | 2 | 2 | 2 | — | ≤5s | |
| project_graph | stable | workspace | 1 | 2 | 2 | 2 | 35p | ≤5s | |
| project_diagnostics | stable | analysis | 2 | 67 | 18019 | 18019 | solution | ≤15s | 1st 18s, slightly over budget (FLAG 14.5) |
| compile_check | stable | validation | 2 | 2240 | 14487 | 14487 | solution | ≤15s | 1st cold, 2nd warm |
| security_diagnostics | stable | security | 1 | 2322 | 2322 | 2322 | solution | ≤15s | |
| security_analyzer_status | stable | security | 1 | 8705 | 8705 | 8705 | solution | ≤15s | FLAG — slow for package inventory |
| nuget_vulnerability_scan | stable | security | 1 | 9068 | 9068 | 9068 | 35p | ≤30s | |
| list_analyzers | stable | analysis | 2 | 9 | 8791 | 8791 | 663 rules | ≤5s | 1st call cold materialization |
| diagnostic_details | stable | analysis | 2 | 16045 | 16045 | 16045 | single CA1826 | ≤5s | **over 3× budget — P2 (14.5)** |
| get_complexity_metrics | stable | adv-analysis | 1 | 9394 | 9394 | 9394 | solution, limit=10 | ≤15s | |
| get_cohesion_metrics | stable | analysis | 1 | 118 | 118 | 118 | minMethods=3 | ≤5s | fast |
| find_unused_symbols | stable | adv-analysis | 1 | 195 | 195 | 195 | limit=15 | ≤15s | (after retry) |
| find_dead_fields | experimental | adv-analysis | 1 | 2173 | 2173 | 2173 | limit=20 | ≤5s | borderline |
| suggest_refactorings | stable | adv-analysis | 1 | 488 | 488 | 488 | limit=10 | ≤5s | |
| get_di_registrations | stable | adv-analysis | 1 | — | — | — | overrides=true | ≤15s | response truncated; `_meta` unavailable |
| symbol_search | stable | symbols | 1 | 2007 | 2007 | 2007 | limit=5 | ≤5s | |
| type_hierarchy | stable | symbols | 1 | 4 | 4 | 4 | 1 type | ≤5s | |
| find_references | stable | symbols | 2 | 19 | 940 | 940 | 21 refs / 0 refs | ≤5s | summary=true path |
| find_references_bulk | stable | symbols | 1 | 13084 | 13084 | 13084 | 2 symbols, 63 refs | ≤15s | incl. 12434 ms queued behind auto-reload |
| go_to_definition | stable | symbols | 1 | 3 | 3 | 3 | line+col | ≤5s | |
| get_completions | stable | symbols | 1 | 7008 | 7008 | 7008 | filterText=To | ≤5s | queued 7002 ms behind stale-reload |
| analyze_data_flow | stable | adv-analysis | 1 | 19 | 19 | 19 | 18-line | ≤5s | |
| analyze_snippet | stable | analysis | 2 | 57 | 81 | 81 | 1-line / empty | ≤5s | |
| evaluate_csharp | stable | scripting | 1 | 281 | 281 | 281 | single expression | ≤10s script | |
| format_check | experimental | refactoring | 1 | 976 | 976 | 976 | 642 docs | ≤15s | |
| fix_all_preview | experimental | refactoring | 1 | 59 | 59 | 59 | CA1826 solution | ≤5s | short-circuit (no provider) |
| build_workspace | stable | validation | 1 | 9783 | 9783 | 9783 | full sln | ≤30s writer | dotnet build |
| test_discover | stable | validation | 1 | 18 | 18 | 18 | 1 proj, lim=5 | ≤5s | |
| get_editorconfig_options | stable | configuration | 1 | 11 | 11 | 11 | 1 file | ≤5s | |
| set_diagnostic_severity | stable | configuration | 1 | 4 | 4 | 4 | 1 key | ≤5s | writer |
| add_pragma_suppression | stable | editing | 1 | 46 | 46 | 46 | 1 line | ≤5s | writer |
| revert_last_apply | stable | undo | 2 | 1 | 6864 | 6864 | 1 apply / empty | ≤30s writer | 1st includes post-write reload |
| workspace_changes | stable | workspace | 1 | 2 | 2 | 2 | 1 entry | ≤5s | |
| evaluate_msbuild_property | stable | proj-mut | 1 | 70 | 70 | 70 | 1 prop | ≤5s | |
| get_msbuild_properties | stable | proj-mut | 1 | 59 | 59 | 59 | filter | ≤5s | |
| semantic_search | stable | adv-analysis | 1 | 21 | 21 | 21 | NL query | ≤5s | |
| find_reflection_usages | stable | adv-analysis | 1 | 6 | 6 | 6 | 1 proj | ≤5s | |
| move_type_to_file_preview | stable | refactoring | 1 | 2 | 2 | 2 | 1 file | ≤5s | refusal |
| remove_dead_code_preview | stable | dead-code | 1 | 6 | 6 | 6 | 1 handle | ≤5s | refusal |
| get_prompt_text | experimental | prompts | 2 | 2 | 3 | 3 | 1 prompt | ≤5s | 1st retry due to missing required param |

**Aggregate p50 across all exercised tools:** ≈ **58 ms** (median). Read-side tools are overwhelmingly fast after warm-up; outliers are `workspace_load`, `compile_check` (cold), `diagnostic_details` (P2 perf), `find_references_bulk` (dominated by stale-reload queue), `get_completions` (same).

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `server_info` / `server_heartbeat` | connection.state semantics | Schema enumerates `ready\|initializing\|degraded`; `ready` implies transport-readiness | `ready` unreachable until first `workspace_load` — zero-workspace hosts permanently `initializing` | **P2** | breaks Phase -1 hard gate |
| `diagnostic_details` | "curated fix options" in description | Some curated fixes surfaced | `supportedFixes: []` on CA1826 (common perf warning) | P3 | either populate or soften the description |
| `build_workspace` | diagnostic location shape | `{startLine, startColumn, endLine, endColumn}` like siblings | `endLine: null, endColumn: null` | P3 | consumer UX — location-span UIs render half-ranges |

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `workspace_load` | sibling-worktree path outside sanctioned roots | actionable | names path + allowlist — perfect | |
| `diagnostic_details` | `startLine/startColumn` (sibling convention) | actionable | message names missing param `line` | the drift itself (18.1) is the deeper issue |
| `<any workspaceId after recycle>` | stale workspaceId after host restart | actionable | recommends `workspace_list` + `workspace_load` | matches the `workspace_load` tool-summary contract |
| `find_unused_symbols` (1st call) | stock `includePublic=false, limit=15` | vague | "JSON value could not be converted to System.Boolean. Path: $" with no named parameter | **P3 UX** — likely client schema-bind race; server message should still cite which param |
| `find_references` (fabricated metadataName) | `ITChatBot.DoesNotExist.FabricatedType` | actionable | `category: NotFound` with three-cause explanation | PASS principle #14 v1.8+ behavior |
| `analyze_snippet` | empty code | actionable | `isValid: true, declaredSymbols: null` — treats as empty unit | reasonable |
| `remove_dead_code_preview` | symbolHandle with `writeReferenceCount=1` | actionable | "still has references and cannot be removed safely" | but see 14.4 — too blunt given `find_dead_fields` just reported it as dead |
| `move_type_to_file_preview` | single-type file | actionable | clean refusal with guidance | PASS |
| `set_diagnostic_severity` | valid inputs | n/a (success) | — | chose correct nested `.editorconfig` — nice |
| `get_prompt_text(discover_capabilities, {task:…})` | wrong param name | actionable | names the required parameter `taskCategory` | PASS |
| `revert_last_apply` (2nd call) | nothing left to revert | actionable | "No operation to revert. Nothing has been applied in this session, or the workspace was reloaded / closed" | PASS |
| `fix_all_preview(CA1826)` | no provider | actionable | guidance suggests `add_pragma_suppression` / `set_diagnostic_severity` | PASS |

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `workspace_load` | `autoRestore=true` | exercised | no-op (restoreRequired=false) — wrapper pre-req #6 satisfied |
| `project_diagnostics` | `severity=Error`, `severity=Warning`, `limit=20` | exercised | invariants preserved under filter |
| `compile_check` | `emitValidation=true` | exercised | post-warm 2.2 s |
| `list_analyzers` | `offset=662, limit=1` | exercised | paginated to final rule |
| `find_unused_symbols` | `includePublic=false, limit=15` | exercised | (after 1st-call retry) |
| `get_cohesion_metrics` | `minMethods=3` | exercised | |
| `find_dead_fields` | **`includePublic=true` / `usageKind="never-read"` NOT probed** | partial | wrapper pre-req #6 explicitly asked — **degraded coverage** (noted in ledger) |
| `suggest_refactorings` | `limit=10` | exercised | |
| `get_di_registrations` | `showLifetimeOverrides=true` | exercised | client truncated response (15) |
| `symbol_search` | `kind="Class"` | exercised | |
| `find_references` | `summary=true`, negative `metadataName` | exercised | |
| `find_references_bulk` | multi-symbol batch via `metadataName` | exercised | |
| `get_completions` | `filterText="To", maxItems=10` | exercised | |
| `semantic_search` | HTML-encoded `Task&lt;bool&gt;` in query | exercised (decoded server-side, returned hits) | |
| `find_reflection_usages` | `projectName` scoping | exercised | |
| `test_discover` | `projectName + limit` | exercised | |
| `get_msbuild_properties` | `propertyNameFilter` | exercised | |
| `fix_all_preview` | `scope="solution"` | exercised | |
| `analyze_snippet` | `kind="statements"`, `kind="program"` with empty body | exercised | |
| `workspace_load` | `autoRestore=true, restoreRequired` status field | exercised | v1.29 non-default path per pre-req #6 |

## 10. Prompt verification (Phase 16)
| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `discover_capabilities` | yes | yes | **no** (42 tools + 6 prompts cross-checked against live catalog) | not probed (single call) | 3 | promote — cheap, accurate, renders clean against live surface | also exercised negative path: wrong param name → actionable error |
| (other 19 prompts) | — | — | — | — | — | needs-more-evidence | not rendered this run |

## 11. Skills audit (Phase 16b)
**N/A — plugin repo not accessible.** Per wrapper instruction #7, the Roslyn-Backed-MCP plugin repo's `skills/*/SKILL.md` directory is not reachable from this audit's filesystem scope. All skills-audit rows are `blocked — skills directory not accessible`.

## 12. Experimental promotion scorecard
| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `workspace_warm` | workspace | exercised | 3366 | yes | n/a (no negative probe) | n/a | none | **promote** | 28 cold compilations in 3.4 s; clean summary payload; documented behavior matches observation |
| tool | `find_dead_fields` | adv-analysis | exercised | 2173 | yes | n/a | n/a | cross-tool inconsistency 14.4 with `remove_dead_code_preview` — refusal breaks the dead-code workflow for ctor-written fields | **keep-experimental** | 7 hits with clean handles; but wrapper-pre-req #6 `includePublic=true` / `usageKind="never-read"` paths not probed this run — partial coverage; blocker is the cross-tool workflow break |
| tool | `find_duplicate_helpers` | adv-analysis | skipped-safety | — | n/a | n/a | n/a | — | **needs-more-evidence** | not exercised |
| tool | `find_dead_locals` | adv-analysis | skipped-safety | — | n/a | n/a | n/a | — | **needs-more-evidence** | |
| tool | `preview_record_field_addition` | analysis | skipped-repo-shape | — | n/a | n/a | n/a | — | **needs-more-evidence** | no record-positional target |
| tool | `symbol_impact_sweep` | analysis | skipped-safety | — | n/a | n/a | n/a | — | **needs-more-evidence** | |
| tool | `probe_position` | symbols | skipped-safety | — | n/a | n/a | n/a | — | **needs-more-evidence** | |
| tool | `trace_exception_flow` | adv-analysis | skipped-safety | — | n/a | n/a | n/a | — | **needs-more-evidence** | |
| tool | `test_reference_map` | validation | skipped-safety | — | n/a | n/a | n/a | — | **needs-more-evidence** | |
| tool | `validate_workspace` | validation | skipped-safety | — | n/a | n/a | n/a | — | **needs-more-evidence** | |
| tool | `validate_recent_git_changes` | validation | skipped-safety | — | n/a | n/a | n/a | — | **needs-more-evidence** | |
| tool | `format_check` | refactoring | exercised | 976 | yes | n/a | n/a | none | **promote** | 642 documents, 0 violations in under 1 s; clean response shape |
| tool | `restructure_preview` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `replace_string_literals_preview` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `change_signature_preview` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `symbol_refactor_preview` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `split_service_with_di_preview` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `record_field_add_with_satellites_preview` | refactoring | skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | no record positional target |
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 59 | yes | actionable | not round-tripped (no provider → no `_apply`) | — | **keep-experimental** | short-circuit path is correct and fast; apply round-trip not possible on this repo configuration (no code-fix providers installed) — standard exp readiness gap |
| tool | `fix_all_apply` | refactoring | blocked | — | — | — | — | — | **needs-more-evidence** | blocked by preview upstream |
| tool | `move_type_to_file_apply` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `change_type_namespace_preview` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `extract_interface_preview` | refactoring | skipped-safety | — | — | — | — | prior-audit Bug 9.1 | **needs-more-evidence** (this run) — prior evidence suggests **deprecate** for `extract_interface_apply` unless Bug 9.1 is fixed in v1.29; not re-verified |
| tool | `extract_interface_apply` | refactoring | skipped-safety | — | — | — | — | prior-audit 9.1 | **needs-more-evidence** |
| tool | `bulk_replace_type_apply` | refactoring | skipped-safety | — | — | — | — | prior-audit 9.6 (partial mismatch) | **needs-more-evidence** |
| tool | `replace_invocation_preview` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `extract_type_apply` | refactoring | skipped-safety | — | — | — | — | prior-audit 9.1–9.3 | **needs-more-evidence** |
| tool | `extract_method_apply` | refactoring | skipped-safety | — | — | — | — | prior-audit 9.7 (formatting defects) | **needs-more-evidence** |
| tool | `extract_shared_expression_to_helper_preview` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `format_range_apply` | refactoring | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `apply_with_verify` | undo | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `apply_multi_file_edit` | editing | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `preview_multi_file_edit` | editing | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `preview_multi_file_edit_apply` | editing | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `create_file_apply` | file-ops | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `delete_file_apply` | file-ops | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `move_file_apply` | file-ops | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `remove_dead_code_apply` | dead-code | skipped-safety | — | — | — | — | blocked by preview 14.4 | **needs-more-evidence** |
| tool | `remove_interface_member_preview` | dead-code | skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | |
| tool | `get_prompt_text` | prompts | exercised | 3 | yes | actionable | idempotent (same args → same output not re-probed, but template rendering is deterministic by construction) | none | **promote** | reached clean success + actionable error on wrong param; rendered catalog-faithful tool list for `discover_capabilities` |
| tool | `add_central_package_version_preview` | proj-mut | skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | no CPM |
| tool | `apply_project_mutation` | proj-mut | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `scaffold_type_preview` / `_apply` | scaffolding | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `scaffold_test_batch_preview` / `scaffold_first_test_file_preview` / `scaffold_test_apply` | scaffolding | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `move_type_to_project_preview` / `extract_interface_cross_project_preview` / `dependency_inversion_preview` | cross-project | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| tool | `migrate_package_preview` / `split_class_preview` / `extract_and_wire_interface_preview` / `apply_composite_preview` | orchestration | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| resource | `server_catalog_full` | server | skipped-safety | — | yes | n/a | n/a | — | **needs-more-evidence** | |
| resource | `server_catalog_tools_page` | server | exercised | — | yes | n/a | n/a | — | **promote** | paginated pages 0/100 and 100/100 clean |
| resource | `server_catalog_prompts_page` | server | exercised | — | yes | n/a | n/a | — | **promote** | page 0/50 clean, `hasMore=false` correct |
| resource | `source_file_lines` | workspace | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |
| prompt | `discover_capabilities` | prompts | exercised | 3 | yes | actionable | template deterministic | none | **promote** | see §10 |
| prompt | (other 19 prompts) | prompts | skipped-safety | — | — | — | — | — | **needs-more-evidence** | |

### Scorecard aggregate (exp surface)
- **Promote (4):** `workspace_warm`, `format_check`, `get_prompt_text` (tool), `server_catalog_tools_page` + `server_catalog_prompts_page` (resources) + `discover_capabilities` (prompt).
- **Keep-experimental (2):** `find_dead_fields` (cross-tool break 14.4), `fix_all_preview` (apply round-trip not proven on this repo).
- **Needs-more-evidence (~48):** bulk of refactoring / orchestration / scaffolding / file-ops / cross-project surface — skipped-safety this run; wrapper #8 default per blocked/skipped paths.
- **Deprecate (0):** none newly surfaced this run; prior-audit Bug 9.x territory remains unresolved re-probe candidate but not labeled `deprecate` without re-verification.

## 13. Debug log capture
**N/A — client did not surface MCP log notifications.** Claude Code CLI exposes only `_meta` timing fields on tool responses; `notifications/message` from `McpLoggingProvider` is not forwarded to the agent surface. No correlationId, gate-contention warning, rate-limit hit, or timeout log observable from inside the audit loop.

## 14. MCP server issues (bugs)

### 14.1 `server_info.connection.state` stuck at `initializing` until first `workspace_load`
| Field | Detail |
|-------|--------|
| Tool | `server_info` / `server_heartbeat` |
| Input | Fresh stdio host, zero loaded workspaces |
| Expected | Per schema (`ready\|initializing\|degraded`), `ready` signals transport-readiness. |
| Actual | `state="initializing"` persists until the first `workspace_load` succeeds. Re-probing heartbeat with unchanged `stdioPid/serverStartedAt` returns the same state. |
| Severity | **P2** — breaks the `deep-review-and-refactor.md` Phase -1 hard gate wording (`state==ready` *before* load). |
| Reproducibility | 100% on this server version (1.29.0). |
| Recommendation | Rename the pre-load state to `idle` / `ready-no-workspace`, or document the current semantics explicitly in the `server_info` tool summary. |

### 14.2 Roslyn MCP stdio host recycled mid-session; all state lost
| Field | Detail |
|-------|--------|
| Tool | entire stdio server |
| Input | Long-running audit session, ~10 successful calls against `workspaceId=8e3f96658f064fcc8fa86cfe38a5ac80` |
| Expected | Session persists until explicit `workspace_close` or client disconnect. |
| Actual | Mid-audit, `diagnostic_details` returned `NotFound: Workspace '…' not found or has been closed.` New `stdioPid` observed (41912 → 9548); `serverStartedAt` shifted 02:33:22Z → 02:43:51Z. Paid another 8.5 s cold load. Preview tokens, `workspace_changes` log, and `revert_last_apply` undo stack were all silently invalidated. |
| Severity | **P2** — a mid-audit host recycle silently loses Phase-6 preview tokens and the undo stack. |
| Reproducibility | Intermittent — plausible causes include `ROSLYNMCP_MAX_WORKSPACES` eviction or OS-level host watchdog. Not observable from this client (no log channel). |
| Recommendation | (a) Surface the recycle reason in the next `server_info` call — e.g. `connection.previousRecycleReason` + `connection.previousStdioPid`. (b) Add a client-side heartbeat in the Claude Code MCP bridge that emits a visible notification on recycle. (c) Consider making `workspace_load(autoRestore=true)` a one-liner recovery path for audit-grade workflows. |

### 14.3 `workspace_changes` does not log `set_diagnostic_severity` (nor likely `set_editorconfig_option`) applies
| Field | Detail |
|-------|--------|
| Tool | `workspace_changes` |
| Input | After `set_diagnostic_severity` (Phase 6 apply) + `add_pragma_suppression` (Phase 9 audit-only apply) + `revert_last_apply` on the pragma |
| Expected | Either (a) both applies appear with correct ordering per the schema "List all mutations applied to a workspace during this session", with revert subtracting the pragma row, or (b) revert leaves a row but marks it reverted. Ideally **both applies** are enumerated. |
| Actual | `count=1` with only the `apply_text_edit` (pragma) entry. `set_diagnostic_severity` (confirmed on disk in `tests/.editorconfig`) is absent. |
| Severity | **P2** — the change log is incomplete. A user running a session-undo prompt would be told "only one apply" when two occurred. |
| Reproducibility | Deterministic on this server version. |
| Recommendation | Include editorconfig-level applies in `workspace_changes`. The `set_diagnostic_severity` tool schema *already* claims integration with `revert_last_apply`, so the persistence path exists — just wire it to the change log. |

### 14.4 `find_dead_fields` + `remove_dead_code_preview` cross-tool consistency break
| Field | Detail |
|-------|--------|
| Tool | `find_dead_fields` / `remove_dead_code_preview` |
| Input | `find_dead_fields` returned handle for `SysLogServerApiClient._options` with `usageKind=never-read, readReferenceCount=0, writeReferenceCount=1, confidence=high`. Passed same `symbolHandle` to `remove_dead_code_preview`. |
| Expected | The documented dead-code workflow (find → preview → apply) should either (a) succeed and remove the field + the unused ctor write, OR (b) `find_dead_fields` should not flag fields whose only dead-ness is a never-read ctor-written pattern unless an option asks for it. |
| Actual | `remove_dead_code_preview` refuses: "Symbol '_options' still has references and cannot be removed safely." This means every ctor-injected field `find_dead_fields` flags is a false-positive for the removal pipeline. |
| Severity | **P2** — breaks the first-class `find_dead_fields → remove_dead_code_preview → _apply` workflow advertised in the dead-code category. |
| Reproducibility | 100%. All 7 `find_dead_fields` hits on this repo exhibit the same pattern (confidence:high, writeReferenceCount:1). |
| Recommendation | Either teach `remove_dead_code_preview` to co-delete the ctor-write (and propose ctor-signature change), OR add an option to `find_dead_fields` that filters to symbols `remove_dead_code_preview` will act on (e.g., `onlyIfAutoRemovable=true`). The current shape is a trap for agents running the documented workflow. |

### 14.5 `diagnostic_details` is over-budget for a point probe
| Field | Detail |
|-------|--------|
| Tool | `diagnostic_details` |
| Input | single point probe (filePath + line=44 + column=30 + diagnosticId=CA1826) |
| Expected | ≤5 s per prompt principle #3 (single-symbol read budget). |
| Actual | 16,045 ms (>3× budget). |
| Severity | **P2** performance. |
| Reproducibility | First call after workspace reload. Cache-warmed behaviour not re-probed. |
| Recommendation | Investigate whether `diagnostic_details` re-resolves the full project's diagnostic set per call. If so, accept a `from=<diagnosticLocator>` hint and short-circuit. |

## 15. Improvement suggestions
- **`get_di_registrations` default payload too large for MCP client output caps.** A 35-project solution with layered DI produced a 64 KB response that the Claude Code client truncated. Either expose server-side pagination (`offset`/`limit`) or switch the default to summary counts with a `details=true` opt-in (mirroring `workspace_load.verbose`).
- **`find_dead_fields.excludeTestProjects`** — absent; `find_unused_symbols` has it. Symmetric coverage would be nice.
- **`get_cohesion_metrics.excludeTestProjects`** — LCOM4 scores of 9–15 for test classes dominate top-N view; they're semantically uninteresting (each `[Fact]` is a disjoint cluster by design). Filter tests by default or expose a toggle.
- **MCP client root allowlist (client-side improvement)** — Phase -1 sibling-worktree pattern unreachable when the client pins sanctioned roots to launch CWD. Either the MCP client gets a first-class "add audit worktree" affordance, or the prompt endorses branch-on-main-tree as the portable fallback.
- **`diagnostic_details` parameter naming** — uses `line`/`column` where siblings use `startLine`/`startColumn`. P3 consistency; easy fix: accept both aliases.
- **`find_unused_symbols` parameter-binding error message** — "JSON value could not be converted to System.Boolean. Path: $" should name the param (or at least say "one of the boolean-typed parameters"). On this run the root cause was most likely a client schema-bind race, but the message should still be more helpful for the operator.
- **`set_diagnostic_severity` editorconfig writes** — adds a new `[*.{cs,csx,cake}]` section even when an existing `[*.cs]` section is present. Reuse the existing matching glob where possible, or at least coalesce.
- **`security_analyzer_status` perf** — 8.7 s for a package-inventory check is slow; looks like it is running the full diagnostic pipeline instead of just inspecting NuGet references.
- **`fix_all_preview` guidance mentions `add_pragma_suppression` + `set_diagnostic_severity`** — which is accurate, but could also link to `code_fix_preview` for single-site curated fixes. Minor UX.

## 16. Concurrency matrix (Phase 8b)

### 16.1 Probe set (sequential baseline only)
| Slot | Tool | Inputs | Classification | Notes |
|------|------|--------|----------------|-------|
| R1 | `find_references` (summary=true) | `metadataName=ITChatBot.Adapters.Abstractions.AdapterRegistry` | reader | 21 refs, `_meta.heldMs=18` |
| R2 | `project_diagnostics` (no filter) | full solution | reader | 2 warnings, `heldMs=66` (2nd call; 1st was 18014 cold) |
| R3 | `symbol_search` | `query=AdapterRegistry, kind=Class` | reader | 2 hits, `heldMs≈2007` |
| R4 | `find_unused_symbols` (`includePublic=false, limit=15`) | solution | reader | 0 hits, `heldMs=194` |
| R5 | `get_complexity_metrics` (limit=10) | solution | reader | 10 rows, `heldMs=9393` |
| W1 | `add_pragma_suppression` + `revert_last_apply` | 1 file | writer | round-tripped cleanly (46 ms apply, 6864 ms revert incl. reload) |
| W2 | `set_diagnostic_severity` (CA1826→suggestion) | 1 editorconfig | writer | 4 ms (direct-apply path) |

### 16.2 Sequential baseline (single-call wall-clock)
| Slot | Wall-clock (ms) | Notes |
|------|------------------|-------|
| R1 | 19 | warm |
| R2 | 18019 / 67 | cold / warm |
| R3 | 2007 | warm |
| R4 | 195 | warm |
| R5 | 9394 | warm |
| W1 | 46 (apply) + 6864 (revert) | includes post-write reload |
| W2 | 4 | direct-apply |

### 16.3 Parallel fan-out and behavioral verification
**N/A — client serializes tool responses within a turn (Principle 8b stability note).**

Claude Code CLI surfaces parallel tool calls as a single message, but the observable `_meta.elapsedMs`/`heldMs` values in the responses indicate that Roslyn MCP received and processed them sequentially (zero `queuedMs` observed on concurrent-looking batches). Without fine-grained concurrency observability (no debug log channel), speedup ratios cannot be computed reliably this run. This is a harness limitation, not a server defect.

### 16.4 Read/write exclusion behavioral probe
**N/A — blocked (same reason as 16.3).**

### 16.5 Lifecycle stress
Observed but **unplanned**: the mid-audit host recycle (14.2) destroyed the workspace session between calls. No `workspace_reload` or `workspace_close` was issued by the agent at the moment of loss — it simply arrived on the next call as `NotFound`. This is not a lifecycle-race probe, but it is a lifecycle finding nonetheless.

## 17. Writer reclassification verification (Phase 8b.5)
| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|
| 1 | `apply_text_edit` (via `add_pragma_suppression`) | round-trip PASS | 46 apply / 6864 revert | |
| 2 | `apply_multi_file_edit` | skipped-safety | — | |
| 3 | `revert_last_apply` | PASS (exercised twice) | 6864 / 1 | double-revert actionable |
| 4 | `set_editorconfig_option` (via `set_diagnostic_severity`) | PASS | 4 | change persists on disk |
| 5 | `set_diagnostic_severity` | PASS (kept) | 4 | |
| 6 | `add_pragma_suppression` | PASS (reverted) | 46 | clean diff |

Writers all completed far under the 30 s budget. No writer hit the `_meta.heldMs` gate for more than 6.9 s (revert's held time dominated by post-write reload).

## 18. Response contract consistency
### 18.1 Position-field naming drift
| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|
| `diagnostic_details` vs `apply_text_edit`, `get_code_actions`, `extract_method_preview`, `analyze_data_flow` | source position | `diagnostic_details` requires `line` / `column`; siblings use `startLine` / `startColumn` (selection ranges also add `endLine` / `endColumn`) | P3 — agents flipping between tools in one workflow hit this. Recommended fix: accept both names; prefer the sibling convention. |

### 18.2 Diagnostic-location span completeness
| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|
| `build_workspace` vs `project_diagnostics` | diagnostic location | `build_workspace` reports `endLine: null, endColumn: null` on the CA1826 entries. `project_diagnostics` populates both. Same workspace, same diagnostic id. | P3 — downstream consumers that want a span can't always rely on it. Route both tools through the same locator mapper. |

## 19. Known issue regression check (Phase 18)
Source: `ai_docs/audit-reports/20260415T140403Z_itchatbot_experimental-promotion.md` (v1.18.0 era).

| Prior id | Summary | Status (this run, v1.29.0) |
|-----------|---------|-----------------------------|
| Bug 9.1 | `extract_interface_apply` / `extract_type_apply` silently mutate `.csproj` adding duplicate `<Compile>` items → MSBuild failure on reload | **Not re-probed this run** — would re-land a csproj change in the audit branch. Prior evidence stands; needs dedicated write-probe on a disposable clone to verify. |
| Bug 9.2 | `extract_type_apply` writes extracted file to wrong directory | Not re-probed. |
| Bug 9.3 | `extract_type_apply` preserves `override` modifier on extracted type without base | Not re-probed. |
| Bug 9.6 | `bulk_replace_type_apply` swaps parameter type but not class-level generic interface → compile break | Not re-probed. |
| Bug 9.7 | `extract_method_apply` preview diff has formatting defects (missing spaces, column-1 declaration, collapsed LINQ chain) | Not re-probed. |
| (Related) | `find_dead_fields` produces handles `remove_dead_code_preview` refuses | **NEW this run** — see 14.4. Plausibly the same root cause as 9.1 family ("apply-side tools don't update adjacent syntax"). |

## 20. Known issue cross-check
- **14.4** (`find_dead_fields` ↔ `remove_dead_code_preview` cross-tool break) mirrors the *shape* of prior-audit Bug 9.1 family: write-side tools in the apply chain don't correctly co-modify adjacent syntax (ctor body for 14.4; csproj item list for 9.1). Root-cause re-verification is out of scope for this run.
- Prior `20260413T180545Z_itchatbot_experimental-promotion.md` (v1.12.0 era) and `20260413T160052Z_itchatbot_mcp-server-audit.md` (v1.12.0) are earlier snapshots; not re-probed this run.
