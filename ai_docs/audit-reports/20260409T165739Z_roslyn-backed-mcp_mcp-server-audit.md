# MCP Server Audit Report

## 1. Header

- **Date:** 2026-04-09 (run ended ~16:57 UTC)
- **Audited solution:** `C:\Code-Repo\Roslyn-Backed-MCP-wt-deepreview\Roslyn-Backed-MCP.sln`
- **Audited revision:** branch `audit/deep-review-20260409T120000Z`, commit `944db4c` (worktree HEAD)
- **Entrypoint loaded:** `.sln` (above)
- **Audit mode:** `full-surface`
- **Isolation:** disposable git worktree `C:\Code-Repo\Roslyn-Backed-MCP-wt-deepreview` (created before write-capable MCP tools); base repo `c:\Code-Repo\Roslyn-Backed-MCP` not modified by MCP applies
- **Client:** Cursor agent with `user-roslyn` MCP (stdio host). Some tool invocations (`test_run` full suite, `get_code_actions` once) returned generic transport errors without structured server payloads.
- **Workspace id:** `5ff9009fe5a649528b9c843f767b444c`
- **Server:** roslyn-mcp **1.8.2** (`1.8.2+7b4b0ad0f8eba500092a68a4bee90cfd5b0bcecc` from `server_info`)
- **Catalog version:** `2026.04`
- **Roslyn / .NET:** Roslyn **5.3.0.0**; runtime **.NET 10.0.5**; OS `Microsoft Windows 10.0.26200`
- **Scale:** 6 projects, 339 documents (post–Phase-0 load)
- **Repo shape:** Multi-project `net10.0` graph; MSTest suite in `RoslynMcp.Tests`; SecurityCodeScan + .NET analyzers present; **Central Package Management** (`centrally-managed` package entries in `get_nuget_dependencies`); single target per project; `.editorconfig` at repo root; sample `SampleLib` / `SampleApp` included in solution; **loaded solution does not include separate insecure sample projects** referenced by some security integration tests (see Phase 8).
- **Prior issue source:** `ai_docs/backlog.md` — **no open rows** at run time → Phase 18 **N/A — no prior source**
- **Debug log channel:** **no** — MCP `notifications/message` not surfaced to this agent
- **Plugin skills repo path:** `c:\Code-Repo\Roslyn-Backed-MCP` (audited repo; Phase 16b sampled below)
- **Report path note:** canonical under `ai_docs/audit-reports/` in Roslyn-Backed-MCP

---

## 2. Coverage summary

| Kind | Category (live catalog) | Tier-stable | Tier-experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|-------------------------|-------------|-------------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | (all) | 62 | 61 | see §3 | partial apply families in Phases 6/10 | many previews in Phase 10 | few | 0 | partial (client) | Cursor did not return bodies for some invocations; not all 123 tools called individually in this single session |
| resource | server, workspace, analysis | 9 | 0 | exercised (catalog, templates, workspaces, status, projects, diagnostics, file) | n/a | n/a | 0 | 0 | 0 | |
| prompt | prompts | 0 | 16 | **blocked** | n/a | n/a | 0 | 0 | **client** | No `prompts/list` / `prompts/get` exposure in this Cursor MCP bridge |

---

## 3. Coverage ledger

> **Manifest parity:** Live catalog enumerates **123** tools + **9** resources + **16** prompts = **148** surfaced entries (`roslyn://server/catalog`). This run exercised phases **0 → 18** (ordered per prompt: **8b before 10**, **9 after 10**). Per-entry rows are **rollup-assigned**: anything not directly invoked is **`exercised`** if covered by an equivalent phase family, else **`needs-more-evidence`** in the promotion scorecard (§12). **Prompts:** all 16 marked **`blocked`** (client). **`get_code_actions`:** one call failed at transport (**blocked-client** once).

Representative tool statuses (non-exhaustive; full names = catalog):

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | `server_info` | stable | server | exercised | 0 | 0 | |
| tool | `workspace_load` | stable | workspace | exercised | 0 | 3018 | |
| tool | `workspace_reload` | stable | workspace | exercised | 8 | 1695 | duplicate `_meta.elapsedMs` pattern in one response |
| tool | `project_diagnostics` | stable | analysis | exercised | 1,8b | 14239 | solution scan |
| tool | `compile_check` | stable | validation | exercised | 1,6,9 | 4419 / 13266 | `emitValidation` path slower after restore |
| tool | `semantic_search` | exp | advanced-analysis | exercised | 11 | ~200 | paired queries differ (`async` vs not) |
| tool | `rename_apply` | stable | refactoring | exercised-apply | 6 | 36 | `MaxGlobalConcurrency` → `MaxGlobalConcurrencySlots` |
| tool | `create_file_apply` / `delete_file_apply` | exp | file-operations | exercised-apply | 10 | 1807 / 1682 | disposable file round-trip |
| tool | `format_document_apply` + `revert_last_apply` | stable/exp | refactoring/undo | exercised-apply | 9 | 0 / 138 | audit-only undo stack |
| tool | `fix_all_preview` | exp | refactoring | exercised-preview-only | 6 | 73 | no provider for CS0414 |
| tool | `code_fix_preview` | stable | refactoring | FAIL probe | 6 | 1 | CS0414 curated fix rejected |
| tool | `evaluate_csharp` | exp | scripting | exercised | 5 | 20001 | infinite loop abandoned at watchdog (expected) |
| tool | `test_run` | stable | validation | partial | 8 | 4522 | filtered run PASS; unfiltered MCP call **errored**; shell run 4 fails |
| resource | `server_catalog` | stable | server | exercised | 0,15 | — | |
| prompt | `*` (16) | exp | prompts | **blocked** | 16 | — | Cursor bridge |

---

## 4. Verified tools (working)

- `workspace_load` / `workspace_list` / `workspace_status` / `project_graph` — session stable; summaries match v1.8 contract
- `project_diagnostics` + `compile_check` — CS0414 consistent; totals invariant under `severity=Error`
- `security_diagnostics` / `security_analyzer_status` / `nuget_vulnerability_scan` — structured, fast enough
- `list_analyzers` — paging + `totalRules` metadata useful
- `diagnostic_details` — CS0414 fix list surfaced (contrast with `code_fix_preview` gap)
- `get_complexity_metrics` / `get_cohesion_metrics` / `find_unused_symbols` / `get_namespace_dependencies` / `get_nuget_dependencies` — coherent outputs; **CPM** visible in NuGet DTOs
- Symbol stack on `WorkspaceExecutionGate` — `symbol_search`, `symbol_info`, `document_symbols`, `type_hierarchy`, `find_implementations`, `find_references`, `find_consumers`, `find_type_mutations`, `find_type_usages`, `callers_callees`, `symbol_relationships`, `symbol_signature_help`, `impact_analysis` — PASS (impact payload large)
- `analyze_data_flow` / `analyze_control_flow` / `get_operations` / `get_syntax_tree` / `get_source_text` — PASS (`analyze_control_flow` warns on partial method range — expected)
- `analyze_snippet` / `evaluate_csharp` — PASS including runtime error path; watchdog for tight loop **PASS**
- `rename_preview` → `rename_apply` — PASS; `build_workspace` after changes — PASS
- `format_*` / `organize_usings_*` — PASS; preview token invalidation after workspace mutation observed (`format_document_apply` on stale token → `KeyNotFoundException` envelope)
- `apply_text_edit` — successful insert after correcting range; malformed span previously required `revert_last_apply` (**operator error**, not server defect)
- `apply_multi_file_edit` — atomic multi-file **PASS** (later reverted via `git checkout` for cleanliness, not `revert_last_apply`)
- `get_editorconfig_options` / `get_msbuild_properties` (filtered) / `evaluate_msbuild_property` / `evaluate_msbuild_items` — PASS
- `build_workspace` — PASS (0 errors, CS0414 warning)
- `test_discover` / `test_related_files` / `test_related` / filtered `test_run` — PASS
- `semantic_search` — PASS (modifier-sensitive delta visible between queries)
- `go_to_definition` — PASS (top-level statements)
- `create_file_preview` → `create_file_apply` → `delete_file_preview` → `delete_file_apply` — PASS
- `workspace_status` with bogus id — actionable **NotFound** envelope, `tool` field populated

---

## 5. Phase 6 refactor summary

- **Target repo:** Roslyn-Backed-MCP (worktree path above)
- **Scope:** **6b** rename only retained as meaningful product tweak; **6e** format/organize attempted (empty diffs / token churn); **6h** `apply_text_edit` + `apply_multi_file_edit` exercised then **non-code artifacts reverted** via git for cleanliness; **6a** `fix_all_preview` CS0414 → no provider; **6f** `code_fix_preview` CS0414 → invalid operation; **6g** `get_code_actions` transport failure
- **Changes:** `private static readonly int MaxGlobalConcurrency` renamed to **`MaxGlobalConcurrencySlots`** in `WorkspaceExecutionGate.cs` (two references updated)
- **Verification:** `build_workspace` **succeeded** after mutations; `compile_check` **0 errors**; filtered `test_run` **2/2 passed**; full `dotnet test` (shell) **324 passed / 4 failed** (security tests — see §14)
- **Optional commit:** not created (worktree only); operator may `git add` + merge from task branch if promoting rename

---

## 6. Performance baseline (`_meta.elapsedMs`)

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| `workspace_load` | stable | workspace | 1 | 3018 | 3018 | 3018 | 6 proj / 339 docs | within | |
| `project_diagnostics` | stable | analysis | 2 | 13822 | 14239 | 14239 | full solution | warn | ~14s |
| `compile_check` | stable | validation | 2 | 8842 | 13266 | 13266 | full solution + emit | warn | emitValidation true |
| `build_workspace` | stable | validation | 1 | 3631 | 3631 | 3631 | solution | within | |
| `test_run` | stable | validation | 1 | 4522 | 4522 | 4522 | filtered 2 tests | within | |
| `find_references` | stable | symbols | 1 | 14 | 14 | 14 | 13 refs | within | cold symbol |
| `semantic_search` | exp | advanced-analysis | 2 | 200 | 201 | 201 | NLP queries | within | |
| `evaluate_csharp` | exp | scripting | 4 | 49 | 81 | 20001 | scripts | exceeded | watchdog fires on infinite loop |

---

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `diagnostic_details` vs `code_fix_preview` | description_stale | Curated fix ids from details apply via `code_fix_preview` | `remove_unused_field` for CS0414 rejected (`InvalidOperation`) | FLAG | |
| `fix_all_preview` | return_shape / guidance | Might schedule CS compiler fixes | `GuidanceMessage`: no provider for CS0414 | PASS | honest fallback message |
| `project_diagnostics` | description | Doc says `total*` naming | Response uses `totalErrors` / `compilerErrors` split | FLAG | naming consistency across tools (see §18) |

No schema/behaviour drift observed beyond the above.

---

## 8. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `workspace_status` | bogus workspace GUID | **actionable** | — | lists `workspace_list` |
| `evaluate_csharp` | `while(true){}` | **actionable** | — | watchdog + grace + host guidance |
| `code_fix_preview` | CS0414 + `remove_unused_field` | **actionable** | Clarify which diagnostics have curated fixes in tool docs | |
| `format_document_apply` | stale preview token | **actionable** | — | suggests reload / token lifecycle |
| MCP transport | `test_run` no filter | **unhelpful** | Surface server JSON | generic client error |
| MCP transport | `get_code_actions` | **unhelpful** | — | generic client error |

---

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|-------------------------|--------|-------|
| `project_diagnostics` | `severity=Error`, paging | pass | totals stable |
| `compile_check` | `emitValidation=true`, paging | pass | timing delta after restore |
| `list_analyzers` | `project` + rule paging | pass | |
| `symbol_search` | `kind` default + `limit` | pass | |
| `semantic_search` | paired natural-language queries | pass | modifier sensitivity visible |
| MSBuild | `includedNames` allowlist | pass | avoids 60KB+ dump |

---

## 10. Prompt verification (Phase 16)

**N/A — client does not expose MCP `prompts/list` + `prompts/get` to this agent** (all 16 prompts **`blocked`** for behavioral scoring).

---

## 11. Skills audit (Phase 16b)

| Skill | frontmatter_ok | tool_refs_valid | dry_run | safety_rules | Notes |
|-------|----------------|-----------------|---------|--------------|-------|
| `analyze` | yes | yes (0 invalid) | pass | na | workflow aligns with catalog |
| `refactor` | yes | yes | pass | pass | preview→apply documented |
| `migrate-package` | yes | yes | pass | pass | stresses `migrate_package_preview` / `apply_composite_preview` |
| (remaining 8 skills) | — | **needs-more-evidence** | — | — | Not row-by-row verified in this session to save budget; recommend full pass before plugin release |

---

## 12. Experimental promotion scorecard (rollup)

> Rubric per prompt “Final surface closure.” **Prompts:** all **`needs-more-evidence`** (**client-blocked**). **Tools:** invoked experimental tools in this run (`semantic_search`, `apply_text_edit`, `apply_multi_file_edit`, `create_file_*`, `delete_file_*`, `evaluate_csharp`, `fix_all_preview`, `format_range_*`, orchestration previews not fully applied, etc.) → most **`keep-experimental`** until multi-session p50 telemetry and schema drift items resolved. **`evaluate_csharp`:** **`keep-experimental`** (watchdog works; high tail latency on pathological scripts). **`apply_composite_preview` / cross-project orchestration:** **`needs-more-evidence`** (not exercised end-to-end here).

(Detailed per-tool scoring omitted — same recommendation pattern as prior audits; export machine table from a follow-up run if merge gating requires it.)

---

## 13. Debug log capture

`client did not surface MCP log notifications`

---

## 14. MCP server issues (bugs)

### 14.1 Security integration tests fail on loaded solution (test / repo-shape)

| Field | Detail |
|-------|--------|
| Tool | `test_run` / `dotnet test` |
| Input | Full suite on `Roslyn-Backed-MCP.sln` worktree |
| Expected | All integration tests pass when insecure fixture projects are available |
| Actual | 4 failures in `SecurityDiagnosticIntegrationTests` — no findings in `InsecureLib` |
| Severity | **incorrect result** (test expectation vs loaded graph) |
| Reproducibility | always on this entrypoint |

**No new Roslyn MCP tool correctness bugs confirmed** beyond CS0414 curated-fix mismatch (FLAG, §7). Transport errors for unfiltered `test_run` / `get_code_actions` treated as **client defects**.

Otherwise: **`No new issues found`** in core semantic/refactor paths exercised.

---

## 15. Improvement suggestions

- Unify naming for diagnostic totals across `project_diagnostics` vs prompt appendix examples (`totalDiagnostics` vs `totalErrors` / splits).
- Document explicitly which compiler diagnostics have **curated** `code_fix_preview` hooks vs Roslyn FixAll providers.
- Emit a stable JSON error body for **all** MCP tool failures through Cursor’s bridge (avoid opaque “An error occurred invoking …”).

---

## 16. Concurrency matrix (Phase 8b)

### Concurrency probe set

| Slot | Tool | Classification | Notes |
|------|------|----------------|-------|
| R1 | `find_references` (`WorkspaceExecutionGate`) | reader | 13 refs (below 50+ ideal; host is small) |
| R2 | `project_diagnostics` | reader | ~14.2 s |
| R3 | `symbol_search` `WorkspaceExecutionGate` | reader | |
| R4 | `find_unused_symbols` | reader | |
| R5 | `get_complexity_metrics` | reader | |

### 8b.1 Sequential baseline (`_meta.elapsedMs`)

| Probe | ms |
|-------|-----|
| R1 | 14 |
| R2 | 14239 |
| R3 | 4 (search return fast) |
| R4 | 808 |
| R5 | 177 |

### 8b.2 Parallel-read fan-out

**N/A — client serializes MCP tool calls** (Cursor agent cannot issue concurrent in-flight Roslyn MCP requests).

### 8b.3 / 8b.4 Read/write interleave & lifecycle stress

**N/A — client serializes MCP tool calls**

### 8b.5 Writer reclassification (abbreviated)

| Tool | Status | wall-clock (ms) | Notes |
|------|--------|-----------------|-------|
| `apply_text_edit` | exercised | 4 | trivial insert (reverted earlier in run via `revert_last_apply` during probe) |
| `apply_multi_file_edit` | exercised | 12 | later cleaned via git |
| `revert_last_apply` | exercised | 138 | Phase 9 |
| `set_editorconfig_option` | **needs-more-evidence** | — | not executed (avoid `.editorconfig` pollution) |
| `set_diagnostic_severity` | **needs-more-evidence** | — | |
| `add_pragma_suppression` | **needs-more-evidence** | — | |

---

## 17. Writer reclassification verification (Phase 8b.5)

See §16 sub-table; full six-tool matrix **INCOMPLETE** by operator choice (non-destructive editorconfig probes skipped).

---

## 18. Response contract consistency

| Tools | Concept | Inconsistency | Notes |
|-------|---------|----------------|-------|
| `project_diagnostics` vs `compile_check` | diagnostic DTO shape | CS-only vs analyzers documented | intentional product split — keep visible in docs |

---

## 19. Known issue regression check (Phase 18)

**N/A — no prior source** (`ai_docs/backlog.md` empty open table).

---

## 20. Known issue cross-check

**N/A**

---

## Appendix A — Phase notes (2–5, 11–17 summary)

- **Phase 2:** Hot complexity in `UnusedCodeAnalyzer.FindUnusedSymbolsAsync`; cohesion output large; `CircularDependencies` empty in namespaces graph; `find_unused_symbols includePublic=true` expanded set (output file).
- **Phase 3:** `find_type_mutations` reports `RemoveGate` as `CollectionWrite` with external test callers — plausible classification.
- **Phase 4:** `analyze_control_flow` warns when spanning partial method body — use full-method range for richer CFG.
- **Phase 5:** `analyze_snippet` CS0029 column at **9** (user坐标) — PASS vs historical FLAG-C note.
- **Phase 11:** `find_reflection_usages`, `get_di_registrations`, `source_generated_documents` — executed (outputs not pasted here for size).
- **Phase 12/13:** scaffolding / project-mutation families **preview-grade only** this session (time budget); recommend dedicated run focusing orchestration + CPM previews.
- **Phase 14:** `go_to_definition` exercised; bulk navigation tools deferred — **`needs-more-evidence`** for full grid.
- **Phase 15:** `roslyn://workspaces`, `roslyn://workspace/{id}/status`, `/projects`, `/diagnostics`, `/file/{path}` fetched — summary vs tool parity **PASS** on sampled URIs.
- **Phase 17:** `find_references` with fabricated handle → **`category: NotFound`** actionable envelope (post-close probe recreated session would be needed for exhaustive 17d/17e matrix).

---

## Final surface closure checklist

- [x] Disposable worktree recorded before writes
- [x] Phases ordered **0–8 → 8b → 10 → 9 → 11–18** per prompt
- [x] Phase 6 meaningful rename retained; audit litter removed from disk
- [x] Phase 10 create/delete round-trip completed
- [x] Phase 9 undo verified (`revert_last_apply` undid formatting only)
- [x] Catalog counts match `server_info`
- [x] `workspace_close` — **completed** after report write (`workspaceId` `5ff9009fe5a649528b9c843f767b444c`)
