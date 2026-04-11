# MCP Server Audit Report

**Intended final path (if consolidating under Roslyn-Backed-MCP):** `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/20260409T165300Z_networkdocumentation_mcp-server-audit.md` — copy this file there before rollup/backlog updates.

## 1. Header

- **Date:** 2026-04-09 (UTC run ~16:43–16:52)
- **Audited solution:** `NetworkDocumentation.sln` (worktree checkout)
- **Audited revision:** `d886493` (branch `agent/deep-audit-20260409` in worktree)
- **Entrypoint loaded:** `c:\Code-Repo\DotNet-Network-Documentation-wt-deep-audit-20260409\NetworkDocumentation.sln`
- **Audit mode:** `full-surface`
- **Isolation:** Disposable worktree `c:\Code-Repo\DotNet-Network-Documentation-wt-deep-audit-20260409` (branch `agent/deep-audit-20260409`). Git **`worktree remove`** + branch delete completed from the main repo; if the empty/leftover directory still appears on disk (Windows file lock), delete it manually after closing editors. Session **Phase 6** exercised `rename_apply` with a behavior-preserving rename `LookupMappedRegion` → `LookupRegionFromMap` in `RegionService.cs` (see §5); that change was **reverted** on the product tree — **canonical repo keeps `LookupMappedRegion`.**
- **Client:** Cursor agent loop with `user-roslyn` MCP (stdio host). MCP **prompts** (`prompts/get`) are **not** invokable via this client surface — Phase 16 marked **blocked — client** for per-prompt execution; prompt *descriptors* were not re-run in-session after `workspace_close`.
- **Workspace id (during run):** `3462692f70d64c07926db5e0876d719b` (closed at end)
- **Server:** `roslyn-mcp` **1.8.2+7b4b0ad0f8eba500092a68a4bee90cfd5b0bcecc**
- **Catalog version:** `2026.04` (matches `server_info` and `roslyn://server/catalog`)
- **Roslyn / .NET:** Roslyn `5.3.0.0`, runtime `.NET 10.0.5`, OS `Microsoft Windows 10.0.26200`
- **Scale:** 8 projects, 361 documents (`workspace_load` summary)
- **Repo shape:** Multi-project `net10.0` library/CLI/web/tests; Playwright tests present; analyzers (Meziantou, Puma, IDE) load — **1387 info-severity analyzer diagnostics** solution-wide; **0** errors/warnings in `project_diagnostics` totals; **no** `Directory.Packages.props` / central package management detected in `get_nuget_dependencies`; DI and SQLite present in Web.
- **Prior issue source (`ai_docs/backlog.md`):** open `devicebuilder-extract` + deferred rows — **no MCP regression ids**; Phase 18 used **procedure-level** reruns (diagnostics paging, rename caret ambiguity, script timeout) instead.
- **Debug log channel:** **no** — no verbatim `notifications/message` stream captured in the agent transcript.
- **Plugin skills repo path (Phase 16b):** **blocked — audited repo is DotNet-Network-Documentation; Roslyn-Backed-MCP plugin `skills/*` tree not in workspace**

---

## 2. Coverage summary

Counts are **session** classifications (full catalog = 123 tools + 9 resources + 16 prompts). Many tools were **not** individually invoked in this single session; those are counted here as **`needs-more-evidence` in scorecard** rather than falsifying `exercised`.

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked |
|------|----------|-----------|-----------------|--------------|-------------------|----------------|---------|
| tool | (multiple) | ~48 | ~6 | ~3 | 0 | 0 | 1 (`get_code_actions` initial param error — see issues) |
| resource | server/workspace/analysis | 2 (`catalog`, `resource-templates`) | n/a | n/a | 0 | 0 | 7 not fetched (session scope) |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 (`client does not expose prompt invocation`) |

---

## 3. Coverage ledger (abbreviated)

**Contract note:** A **complete** one-row-per-catalog-entry ledger with fresh `lastElapsedMs` for every tool requires a longer automation pass. Below: **representative** ledger rows (all invoked this session). All other catalog tools/resources/prompts: **`status=needs-more-evidence`** unless marked in §14.

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | server_info | stable | server | exercised | 0 | 7 | counts match catalog |
| tool | workspace_load | stable | workspace | exercised | 0 | 3818 | load gate |
| tool | workspace_list | stable | workspace | exercised | 0–8 | 1 | |
| tool | workspace_status | stable | workspace | exercised | 0–8 | 6 | |
| tool | project_graph | stable | workspace | exercised | 0 | 1 | |
| tool | workspace_reload | stable | workspace | exercised | 8 | 2312 | |
| tool | workspace_close | stable | workspace | exercised | final | 583 | |
| tool | project_diagnostics | stable | analysis | exercised | 1 | 23561 / 375 | unfiltered returned empty page when default path implies Error-only paging — use `severity=Info` when solution is info-heavy |
| tool | compile_check | stable | validation | exercised | 1,6 | 34–2537 | `emitValidation=true` slower as expected |
| tool | security_diagnostics | stable | security | exercised | 1 | 2034 | 0 findings |
| tool | security_analyzer_status | stable | security | exercised | 1 | 1925 | |
| tool | nuget_vulnerability_scan | stable | security | exercised | 1 | 4501 | 0 CVE |
| tool | list_analyzers | stable | analysis | exercised | 1 | 11 | paging `offset` exercised |
| tool | diagnostic_details | stable | analysis | exercised | 1 | 4 | `IDE0305` — `SupportedFixes: []` |
| tool | get_complexity_metrics | stable | advanced-analysis | exercised | 2 | 291 | |
| tool | get_cohesion_metrics | stable | analysis | exercised | 2 | (large) | output offloaded |
| tool | find_unused_symbols | stable | advanced-analysis | exercised | 2 | 1813–1923 | |
| tool | get_namespace_dependencies | stable | advanced-analysis | exercised | 2 | (large) | |
| tool | get_nuget_dependencies | stable | advanced-analysis | exercised | 2 | 1691 | |
| tool | symbol_search | stable | symbols | exercised | 3 | — | |
| tool | symbol_info | stable | symbols | exercised | 3 | 4 | |
| tool | document_symbols | stable | symbols | exercised | 3 | — | |
| tool | type_hierarchy | stable | analysis | exercised | 3 | 6 | |
| tool | find_references | stable | symbols | exercised | 3,8b | 10 | |
| tool | find_consumers | stable | analysis | exercised | 3 | 6 | |
| tool | impact_analysis | stable | analysis | exercised | 3 | 5 | |
| tool | enclosing_symbol | stable | symbols | exercised | 6 | 2 | |
| tool | analyze_data_flow | experimental | advanced-analysis | exercised | 4 | 8 | |
| tool | analyze_control_flow | experimental | advanced-analysis | exercised | 4 | 4 | |
| tool | analyze_snippet | stable | analysis | exercised | 5 | 42–77 | CS0029 column user-relative |
| tool | evaluate_csharp | experimental | scripting | exercised | 5 | 20–13010 | infinite loop → forced abandon per UX-002 |
| tool | rename_preview | stable | refactoring | exercised | 6 | 39 | wrong caret resolves tuple field — use `symbolHandle` |
| tool | rename_apply | stable | refactoring | exercised-apply | 6 | 33 | `LookupMappedRegion`→`LookupRegionFromMap` |
| tool | organize_usings_preview | stable | refactoring | exercised | 6 | 7 | |
| tool | organize_usings_apply | stable | refactoring | exercised-apply | 6 | 1 | no-op changes |
| tool | fix_all_preview | experimental | refactoring | exercised | 6 | 328 | `IDE0007` doc scope 0 fixes; `IDE0060` guidance only |
| tool | format_document_preview | stable | refactoring | exercised | 6 | 114 | |
| tool | format_range_preview | experimental | refactoring | exercised | 6 | 12 | apply failed — stale token after other applies |
| tool | get_code_actions | experimental | code-actions | exercised | 6 | 236 | first call errored without required keys |
| tool | create_file_preview | experimental | file-operations | exercised | 10 | 4 | |
| tool | create_file_apply | experimental | file-operations | exercised-apply | 10 | 2182 | |
| tool | delete_file_preview | experimental | file-operations | exercised | 10 | 5 | |
| tool | delete_file_apply | experimental | file-operations | exercised-apply | 10 | 2018 | |
| tool | move_type_to_file_preview | experimental | refactoring | exercised-preview-only | 10 | 248 | `RegionMapEntry` — **not applied** (would be product refactor) |
| tool | apply_multi_file_edit | experimental | editing | exercised-apply | 6 / rev | 16 | then **revert_last_apply** |
| tool | apply_text_edit | experimental | editing | exercised-apply | 9 | 7 | whitespace probe |
| tool | revert_last_apply | experimental | undo | exercised-apply | 9, 6 | 2100 | undid text edit; earlier undid multi-file |
| tool | build_workspace | stable | validation | exercised | 8 | 34008 | `dotnet build` ok |
| tool | test_discover | stable | validation | exercised | 8 | (large) | |
| tool | test_related_files | stable | validation | exercised | 8 | 13 | |
| tool | semantic_search | experimental | advanced-analysis | exercised | 11 | (large)688d8 | |
| tool | get_editorconfig_options | experimental | configuration | exercised | 7 | 9 | |
| resource | server_catalog | stable | server | exercised | 0 | — | via `fetch_mcp_resource` |
| resource | resource_templates | stable | server | exercised | 0 | — | |
| prompt | * (16 live) | experimental | prompts | blocked | 16 | — | Cursor tool channel has no `prompts/get` in this session |

---

## 4. Verified tools (working) — sample

- `workspace_load` / `project_graph` — clean load, 8 projects.
- `compile_check` — 0 CS diagnostics after Phase 6 rename; `emitValidation` path slower (~2.5s vs ~34ms).
- `build_workspace` — succeeded (includes frontend npm build on fresh worktree).
- `rename_preview`+`rename_apply` — solution-consistent rename; `RegionServiceTests` pass.
- `create_file_apply` / `delete_file_apply` — round-trip scratch file ok.
- `revert_last_apply` — restored botched `apply_text_edit`.
- `evaluate_csharp` — runtime error and **script abandon** paths behave as documented.

---

## 5. Phase 6 refactor summary (session-only; product tree reverted)

- **Target repo:** DotNet-Network-Documentation (disposable worktree; now removed)
- **Scope:** **6b** rename only (6a fix-all produced 0 applicable fixes for chosen IDs; 6c–6i not exercised end-to-end to limit blast radius).
- **Session changes:** Private helper `LookupMappedRegion` was renamed to **`LookupRegionFromMap`**; call site in `BuildRegionsPayload` updated (behavior-preserving). **Retention:** reverted — canonical `RegionService.cs` again uses **`LookupMappedRegion`**; this subsection records MCP exercise evidence only.
- **Tools:** `enclosing_symbol`, `rename_preview`, `rename_apply`
- **Verification (while rename applied in worktree):** `compile_check` clean; `dotnet test` `--filter FullyQualifiedName~RegionServiceTests` → **5 passed**; `dotnet test` with `--filter "Category!=Playwright"` → **8041 passed**, 9 skipped; `build_workspace` **0 errors**.

---

## 6. Performance baseline (`_meta.elapsedMs`) — sample

| Tool | Tier | Calls | p50_ms (approx) | Budget | Notes |
|------|------|-------|-----------------|--------|-------|
| workspace_load | stable | 1 | 3818 | within | load gate |
| project_diagnostics | stable | 2 | 375–23561 | warn | full sweep ~23.5s |
| compile_check | stable | 4 | 34–2537 | within/warn | emit path |
| get_complexity_metrics | stable | 1 | 291 | within | |
| find_unused_symbols | stable | 2 | ~1900 | within | |
| build_workspace | stable | 1 | 34008 | warn | includes npm/vite |
| evaluate_csharp | experimental | 4 | 20–13010 | exceeded | abandon path by design |

---

## 7. Schema vs behaviour drift

| tool | mismatch_kind | expected | actual | severity | notes |
|------|---------------|----------|--------|----------|-------|
| project_diagnostics | return_shape | Unfiltered page returns diagnostic page matching totals | `totalInfo=1387` but `returnedDiagnostics=0` until `severity=Info` | FLAG | May be implicit default min-severity — document or align with `totalDiagnostics` |
| rename_preview | description_stale | Caret on method renames method | Caret at wrong column renamed tuple `Source` → proposed name | FLAG | **Use `symbolHandle` from `enclosing_symbol`** |
| fix_all_preview | return_shape | fix-all available for IDE rules | `IDE0060` returns guidance, 0 changes | FLAG | Expected when no FixAll provider — text is actionable |

**Otherwise:** No schema/behaviour drift observed.

---

## 8. Error message quality

| tool | probe_input | rating | suggested_fix | notes |
|------|-------------|--------|---------------|-------|
| rename_preview | column on tuple field `Source` | actionable | `Invalid operation: Cannot rename metadata...` | Clear |
| organize_usings_apply / format_range_apply | stale preview token | actionable | “Preview token not found or expired” | Expected after intervening applies |
| apply_multi_file_edit | EndColumn past EOL | actionable | ArgumentException with line length | Excellent for editors |
| evaluate_csharp | `while(true){}` timeout 3s | actionable | Abandon message cites budget + grace + thread pool hint | Matches UX-002 docs |
| get_code_actions | wrong JSON keys (`line` vs `startLine`) | unhelpful | “error invoking” without schema hint | Surface parameter names in client error |

---

## 9. Parameter-path coverage

| family | non_default_path_tested | status | notes |
|--------|---------------------------|--------|-------|
| project_diagnostics | `severity=Info`, `project=Core` (empty page) | flag | project-only filter without severity returned 0 rows while `totalInfo` non-zero for Core |
| compile_check | `emitValidation=true` | pass | |
| list_analyzers | `offset=100, limit=50` | pass | |
| evaluate_csharp | `timeoutSeconds=3` | pass | watchdog + grace fired |

---

## 10. Prompt verification (Phase 16)

**N/A — blocked — client does not expose MCP `prompts/get` / `prompts/list` execution in this agent tool surface** (only tool `call_mcp_tool` + resources were used).

---

## 11. Skills audit (Phase 16b)

**Phase 16b blocked — Roslyn-Backed-MCP plugin repo (`skills/*/SKILL.md`) not present in this workspace.**

---

## 12. Experimental promotion scorecard (rollup)

**Method:** Live catalog lists **61 experimental tools** + **16 experimental prompts**. This session **did not** execute every experimental tool individually.

| Recommendation | Count (approx) | Evidence |
|----------------|----------------|----------|
| keep-experimental | semantic_search, analyze_data_flow, analyze_control_flow, create/delete file ops, apply_text_edit, revert_last_apply | worked; some UX rough edges |
| needs-more-evidence | Majority of experimental surface (orchestration, scaffolding, project mutation previews not run; prompts blocked) | session budget |
| promote | *none* | insufficient breadth + prompts blocked |

Per-entry rows are **omitted here** to avoid a false-complete scorecard; treat this section as **rollup-only** until a tooling pass exports `_meta.elapsedMs` for all 77 experimental rows.

---

## 13. Debug log capture

`client did not surface MCP log notifications`

---

## 14. MCP server issues (bugs)

1. **get_code_actions — first invocation** — called with `line`/`column` instead of required `startLine`/`startColumn`: host returned generic **“error occurred invoking”** without parameter hint → **error-message-quality** / **schema discoverability** (severity: cosmetic / error-message-quality, reproducibility: always with wrong contract).

2. **project_diagnostics — default paging vs info-only diagnostics** — `totalErrors/totalWarnings/totalInfo` consistent, but first page empty without explicit `severity` when only Info issues exist → **FLAG** (see §7).

**No new crash-level defects observed.**

---

## 15. Improvement suggestions

- Emit **schema-shaped hints** when MCP tool JSON fails validation (mirror required property names).
- Clarify **`project_diagnostics` default severity / paging** in tool description when analyzers only produce Info.
- **Preview token lifetime:** document ordering hazard (interleaved applies invalidate prior preview tokens) — observed on `format_range_apply`.

---

## 16. Concurrency matrix (Phase 8b)

**8b.2–8b.4:** **N/A — client serializes MCP tool calls** (Cursor agent cannot issue overlapping in-flight requests).

**8b.1 sequential baselines (representative):**

| Probe | Tool | elapsedMs (_meta) |
|-------|------|-------------------|
| R1 | find_references (RegionService) | 10 |
| R2 | project_diagnostics (first call) | 23561 |
| R3 | symbol_search | — (not re-benchmarked post-close) |
| R4 | find_unused_symbols | 1923 |
| R5 | get_complexity_metrics | 291 |

**8b.5 writer verification:** `rename_apply`, `create_file_apply`, `delete_file_apply`, `apply_multi_file_edit`, `revert_last_apply`, `apply_text_edit`, `organize_usings_apply` exercised; `set_editorconfig_option` / `set_diagnostic_severity` / `add_pragma_suppression` **not** run in this session (needs-more-evidence).

---

## 17. Writer reclassification verification (Phase 8b.5) — partial

| tool | status | notes |
|------|--------|-------|
| apply_text_edit | pass | Phase 9 probe |
| apply_multi_file_edit | pass | then reverted |
| revert_last_apply | pass | |
| set_editorconfig_option | not run | |
| set_diagnostic_severity | not run | |
| add_pragma_suppression | not run | |

---

## 18. Response contract consistency

**N/A — no cross-tool field naming contradictions beyond `project_diagnostics`/`compile_check` diagnostic DTO shapes already documented (analyzer vs CS-only).**

---

## 19. Known issue regression check (Phase 18)

Prior backlog lacked MCP tool ids. **Procedural reruns:**

| Check | Result |
|-------|--------|
| `evaluate_csharp` infinite loop + timeout | **Still reproduces** abandon path (~13s for 3s budget + grace) — consistent with server docs |
| `rename_preview` tuple-field mis-click | **Still reproduces** confusing rename target without `symbolHandle` |
| `project_diagnostics` info-heavy empty first page | **Still reproduces** without explicit `severity` |

---

## 20. Known issue cross-check

**N/A** — no new finding mapped to a prior backlog MCP id.

---

### Final surface closure checklist

- [x] Disposable worktree recorded; `dotnet restore` run before semantic tools.
- [x] Phase 6 product mutation isolated (rename); audit-only mutations reverted or deleted.
- [x] `workspace_close` called.
- [ ] **Full** promotion scorecard rows for all 77 experimental catalog entries — **deferred** (honesty note in §12).
- [ ] **Full** coverage ledger 123+9+16 — **deferred**; representative rows in §3.

---

*Generated as part of `ai_docs/prompts/deep-review-and-refactor.md` full-surface execution against **NetworkDocumentation** in a disposable worktree.*
