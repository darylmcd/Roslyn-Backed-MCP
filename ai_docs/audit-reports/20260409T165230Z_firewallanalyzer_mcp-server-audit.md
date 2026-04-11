# MCP Server Audit Report

## 1. Header
- **Date:** 2026-04-09 (UTC run completion ~16:52Z)
- **Audited solution:** `c:\Code-Repo\DotNet-Firewall-Analyzer-wt-mcp-20260409\FirewallAnalyzer.slnx`
- **Audited revision:** `cbadadd` (worktree branch `wt/mcp-deep-review-20260409`)
- **Entrypoint loaded:** `FirewallAnalyzer.slnx`
- **Audit mode:** `full-surface`
- **Isolation:** Disposable git worktree `c:\Code-Repo\DotNet-Firewall-Analyzer-wt-mcp-20260409` (branch `wt/mcp-deep-review-20260409`). All MCP write tooling targeted this path. Cleanup: `git worktree remove` when discarding.
- **Client:** Cursor agent session with `user-roslyn` MCP (stdio). **MCP `prompts/*`**: not exposed as invocable tools in this client path → Phase 16 **blocked (client)**. **MCP log notifications**: not surfaced in tool results → **no** in header.
- **Workspace ids:** Primary session `384bede33e9e47f6a680361c6e92ad27` (Phases 0–17); post-close reload `d763e42c296c4655b3c773cf03255628` (17e reopen + final close).
- **Server:** roslyn-mcp **1.8.2** (`productShape`: local-first), **Roslyn** 5.3.0.0, **runtime** .NET 10.0.5, **os** Microsoft Windows 10.0.26200
- **Catalog version:** 2026.04 (from `server_info` / `roslyn://server/catalog`)
- **Scale:** 11 projects, 230 documents (post-restore)
- **Repo shape:** Multi-project, **Central Package Management** (`Directory.Packages.props`), single TFM **net10.0**, tests present (xUnit), analyzers present (incl. SecurityCodeScan, CA rules), **DI** in `ApiHostBuilder`, **source generators** / implicit global usings (GlobalUsings.g.cs, etc.), `.editorconfig` assumed present (not fully audited in this run). No multi-targeting.
- **Prior issue source for Phase 18:** `ai_docs/backlog.md` — **no actionable open MCP/tool rows** (only deferred product phases); regression section **N/A — no Roslyn MCP backlog rows to re-test**.
- **Debug log channel:** **no** — structured `notifications/message` not captured in this session.
- **Plugin skills repo path:** **blocked — Roslyn-Backed-MCP checkout not found** under `c:\Code-Repo` (glob for `**/Roslyn*MCP**/skills/**/SKILL.md` returned 0).
- **Report path note:** Saved per prompt under audited product repo `ai_docs/audit-reports/`. Intended canonical store for Roslyn-Backed-MCP rollups: copy via `eng/import-deep-review-audit.ps1` when consolidating.

---

## 2. Coverage summary
Aggregated from live catalog **2026.04** (`123` tools, `9` resources, `16` prompts). Counts are **outcome buckets** for this run (not tier population).

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|-----------------|--------------|--------------------|----------------|---------|-------|
| tool | (multiple) | ~85+ | ~15 | ~25 | ~5 | 0 | ~18 | See §3; prompts blocked by client; many orchestration/cross-project previews not all applied |
| resource | server/workspace/analysis | 9 | 0 | 0 | 0 | 0 | 0 | Catalog + templates + workspace URIs sampled |
| prompt | prompts | 0 | 0 | 0 | 0 | 0 | 16 | **Client cannot invoke MCP prompts in this session** |

---

## 3. Coverage ledger (compact)
**Catalog totals (live):** 123 tools + 9 resources + 16 prompts = **148** rows (`roslyn://server/catalog`, version **2026.04**).

**Run honesty:** This single agent session **did not** complete a literal 148-row ledger with per-row `lastElapsedMs` — that is a multi-hour Roslyn-Backed-MCP campaign. Instead:

- **Exercised (direct MCP calls in this run):** workspace/server baseline, broad diagnostics, metrics (`get_complexity_metrics`, `get_cohesion_metrics`, `get_namespace_dependencies`), symbol stack on `DriftDetector`, flow analysis on `YamlConfigLoader.CollectUnknownYamlKeyMessages`, snippets + `evaluate_csharp`, refactor **`rename_*` + `format_document_*`**, `fix_all_preview` (no-op), `get_code_actions`, `apply_text_edit` (reverted once), **`create_file_*` + `delete_file_*`**, `move_type_to_file_preview` (no apply), build/test (`build_workspace`, `test_*`, `test_coverage`), `semantic_search`, `find_reflection_usages`, `get_di_registrations`, `source_generated_documents`, `revert_last_apply`, **Phase 17** negative probes, `workspace_close` + reload. *(Navigation/completions family — `go_to_definition`, `get_completions`, etc. — largely **not** called.)*
- **Not exercised this session (default score `needs-more-evidence`):** remaining refactoring/orchestration/scaffolding/project-mutation **apply** chains, most navigation/completion helpers (`go_to_definition` partial only), MSBuild/editorconfig full matrix (`get_editorconfig_options`, `get_msbuild_properties`, …), `get_operations` / `get_syntax_tree`, `apply_multi_file_edit`, `code_fix_*`, `apply_code_action`, `find_references_bulk`, `find_overrides` / `find_base_members`, `get_completions`, multi-TFM previews, many cross-project previews at **preview-only** depth.
- **Skipped-repo-shape:** `add_target_framework_preview` / `remove_target_framework_preview` (solution is **single** `net10.0`, not multi-targeting).
- **Blocked:** all **16 prompts** (client); Phase **16b** skills (**plugin repo missing**).

### 3b. Resources — exercised
`roslyn://server/catalog`, `roslyn://server/resource-templates`, `roslyn://workspaces` (Phase 15 pattern) — full URI matrix deferred to **`needs-more-evidence`** where not re-fetched after final reload.

### 3c. Prompts — blocked (client)
All 16 catalog prompts (`explain_error` … `consumer_impact`): **`blocked — Cursor session did not provide MCP prompt invocation`** → scorecard **`needs-more-evidence`**.

---

## 4. Verified tools (working) — sample evidence
- **`workspace_load` / `project_graph`:** Loaded 11 projects, 230 documents; graph matches solution layout. `_meta.elapsedMs` load ~2.8–4.7s first load.
- **`project_diagnostics` + `severity=Info`:** Returned 31 analyzer infos consistent with `totalInfo`; paging works (`hasMore: true` at `limit=5`).
- **`project_diagnostics` with `severity` omitted:** Returned **zero rows** while `totalInfo=31` — **FLAG** vs expectation that unfiltered page returns diagnostics (see §7).
- **`compile_check`:** 0 CS errors; `emitValidation=true` ~2.9s vs ~2.6s without — plausible emit path after restore.
- **`nuget_vulnerability_scan`:** 0 CVEs, 11 projects, ~5.9s structured result.
- **`rename_preview` / `rename_apply`:** `ListsEqual` → `AreStringListsEqual` in `DriftDetector.cs`; `compile_check` clean.
- **`test_run`:** Full solution **298** passed; filtered `DriftDetectorTests` **8** passed; aggregates structured.
- **`test_coverage`:** Domain.Tests-only run returned **9.6%** line coverage module rollup (expected narrow scope).
- **`evaluate_csharp`:** Infinite loop terminated with **script budget + watchdog** message (~20s `_meta.elapsedMs`); **PASS** (no hang).
- **`create_file_*` / `delete_file_*`:** Round-trip on disposable marker file; workspace consistent.

---

## 5. Phase 6 refactor summary
**Target:** worktree `FirewallAnalyzer.Application` only (disposable branch).

**Applied (product):**
1. **`rename_apply`:** Private helper `ListsEqual` → **`AreStringListsEqual`** in `DriftDetector.cs` (all call sites updated).
2. **`format_document_apply`:** Normalized `DriftDetector.cs` after edits.
3. **`apply_text_edit`:** Temporary audit comment inserted then **removed** manually for a clean diff; final file has **rename only**.

**Not applied / attempted:**
- **`fix_all_preview` (`CA1816`, solution):** No fix provider loaded — guidance message returned (precondition/limitation, not crash).
- **`get_code_actions` @ `Compare`:** No actions at caret (diagnostic not on span).

**Verification:** `compile_check` (project-scoped), `build_workspace`, shell `dotnet test --filter "Category!=E2E"`, MCP `test_run` full (**298** passed).

---

## 6. Performance baseline (`_meta.elapsedMs`) — selected
| tool | tier | calls | p50≈ | max | input_scale | budget_status | notes |
|------|------|-------|------|-----|-------------|---------------|-------|
| `workspace_load` | stable | 2 | 2800 | 4700 | slnx / 11 proj | within | First load slower |
| `project_diagnostics` | stable | 4 | 2800 | 7600 | full solution | within | Info-severity pass faster when cached |
| `compile_check` | stable | 5 | 20 | 3300 | solution / file | within | |
| `find_reflection_usages` | stable | 1 | 951 | 951 | 11 proj | within | |
| `get_di_registrations` | stable | 1 | 810 | 810 | solution | within | |
| `get_nuget_dependencies` | stable | 1 | 1894 | 1894 | CPM graph | within | |
| `build_workspace` | stable | 1 | 1646 | 1646 | slnx | within | |
| `test_run` | stable | 2 | 4300 | 5400 | 298 tests | within | |
| `test_coverage` | stable | 1 | 3069 | 3069 | 1 test proj | within | |
| `evaluate_csharp` (loop) | experimental | 1 | 20004 | 20004 | watchdog | warn | By design |

---

## 7. Schema vs behaviour drift
| tool | mismatch_kind | expected | actual | severity | notes |
|------|---------------|----------|--------|----------|-------|
| `project_diagnostics` | return_shape / default filter | Unfiltered first page should include non-empty diagnostic list when `totalInfo>0` | `severity` omitted → **0 rows**, still `totalInfo=31` | **FLAG** | Using explicit `severity=Info` returns rows. Document default or fix. |
| `apply_text_edit` | parameter / edit application | Range replacement preserves following line break & indentation | Zero-width insert at line start **merged** comment + `public static` line in diff | **FLAG** | **`revert_last_apply` recovered** cleanly; fragile for agents. |

**No schema/behaviour drift observed:** other probed tools.

---

## 8. Error message quality (selected)
| tool | probe_input | rating | notes |
|------|-------------|--------|-------|
| `workspace_status` | non-existent workspace GUID | **actionable** | Clear NotFound + suggests `workspace_list` |
| `find_references` | invalid / unresolvable handle | **actionable** | Structured NotFound |
| `symbol_search` | empty `query` | **pass** | Empty array (no crash) |
| `revert_last_apply` | fresh workspace, no applies | **actionable** | Explicit “nothing to revert” |
| `test_related` | malformed base64 handle | **actionable** | InvalidArgument |

---

## 9. Parameter-path coverage
| family | non_default_path_tested | status | notes |
|--------|-------------------------|--------|-------|
| `project_diagnostics` | `severity=Info`, `limit=5` | **pass** | Totals stable |
| `compile_check` | `emitValidation=true`, `file=` | **pass** | |
| `list_analyzers` | `offset=0,limit=20` | **pass** | `hasMore` true |
| `find_unused_symbols` | `includePublic=true` | **pass** | 9 low/medium confidence symbols |
| resources | `roslyn://server/catalog` | **pass** | |

---

## 10. Prompt verification
**N/A — `blocked (client)`** — no `prompts/list` / `prompts/get` invocation path in Cursor tool surface for this audit.

---

## 11. Skills audit (Phase 16b)
**Phase 16b blocked — plugin repo not accessible** (no local `Roslyn-Backed-MCP` / skills tree discovered).

---

## 12. Experimental promotion scorecard (rollup)
**Rule:** Any **`blocked`** or unexercised experimental tool/resource/prompt → **`needs-more-evidence`** unless FAIL found.

- **Experimental tools (61):** Many exercised (**semantic_search**, **`evaluate_csharp`**, editing/file/projectmutation subsets). Others **`needs-more-evidence`** (single pass; not every orchestration/refactor apply attempted). No **`deprecate`** from this run.
- **Experimental prompts (16):** **`blocked (client)` → needs-more-evidence** for all.

---

## 13. Debug log capture
**client did not surface MCP log notifications**

---

## 14. MCP server issues (bugs)
1. **`project_diagnostics` empty page with null `severity` but non-zero info totals** — **severity:** incorrect result / UX (**FLAG**). **Repro:** Phase 1 call without `severity` vs with `severity=Info`.
2. **`apply_text_edit` can corrupt line breaks** when edits abut method declarations — **severity:** incorrect result (**FLAG**). **Mitigation:** Prefer Roslyn preview/apply; verify diff; `revert_last_apply` worked.

**No new crash-grade issues observed.**

---

## 15. Improvement suggestions
- Document **`project_diagnostics` default `severity`** behaviour explicitly in tool description if “Error-only default” is intentional.
- **`test_run` filter** on solution builds all test projects — noisy stdout; consider documenting aggregation model for consumers.
- **`move_type_to_file_preview`** excellent for `DriftReport` extraction — consider leaving as recommendation for human refactor (not applied to avoid churn in audit).

---

## 16. Concurrency matrix (Phase 8b)
| Probe | Sequential `_meta.elapsedMs` (approx) | Parallel fan-out | Notes |
|-------|----------------------------------------|------------------|-------|
| R1 `find_references` DriftDetector | ~260 ms | **N/A — `blocked`** | **Cursor agent serializes MCP tool calls** |
| R2 `project_diagnostics` Info | ~2800 ms first slice | **N/A — `blocked`** | same |
| R3 `symbol_search` `Rule` limit 200 | large result (~160KB host offload) | **N/A — `blocked`** | |
| R4 `find_unused_symbols` public | ~2238 ms | **N/A — `blocked`** | |
| R5 `get_complexity_metrics` | ~129 ms | **N/A — `blocked`** | |
| 8b.2–8b.4 parallel / lifecycle stress | — | **N/A — client serializes** | No speedup/benchmark comparison per prompt |
| 8b.5 writers | See §17 | Partial | `apply_text_edit`, `create/delete_file_apply`, `revert_last_apply`, **`set_editorconfig_option` / `set_diagnostic_severity` / `add_pragma_suppression` not exercised** in final pass → **needs-more-evidence** |

---

## 17. Writer reclassification verification (Phase 8b.5)
| tool | status | wall-clock (ms) | notes |
|------|--------|-----------------|-------|
| `apply_text_edit` | pass (with FLAG on edge case) | ~2–17 | Broke line merge once; reverted |
| `apply_multi_file_edit` | **needs-more-evidence** | — | Not called this run |
| `revert_last_apply` | pass | ~2466–3038 | Undid bad text edit |
| `set_editorconfig_option` | **needs-more-evidence** | — | |
| `set_diagnostic_severity` | **needs-more-evidence** | — | |
| `add_pragma_suppression` | **needs-more-evidence** | — | |

---

## 18. Response contract consistency
**No cross-tool line-number inconsistency observed** beyond `analyze_snippet` user-relative columns (CS0029 at column **9** for `int x =` — matches snippet-relative expectation).

---

## 19. Known issue regression check (Phase 18)
**N/A — no prior Roslyn MCP backlog entries** in `ai_docs/backlog.md` matching this server.

---

## 20. Known issue cross-check
**N/A**

---

## Appendix A — Phase ordering executed
`0 → 1 → 2 → 3 (DriftDetector depth sample) → 4 → 5 → 6 → 8 (+ build/test) → 8b (sequential baselines; parallel **blocked** — Cursor serializes MCP) → 10 (create/delete apply loop; `move_type_to_file` **preview only**) → 9 (`revert_last_apply` exercised successfully on bad `apply_text_edit` earlier in session) → 11 (partial: semantic + reflection + DI + source-gen) → 7 / 12–15 **partial** (not every preview/apply family) → **16 blocked (prompts)** → **16b blocked (skills repo)** → 17 → 18 **N/A** → final `workspace_close` / reload / close.

**Gap:** Phases **7**, **12–15** were **not** completed to the exhaustive depth the living prompt describes; scores default to **`needs-more-evidence`** where applicable.

---

## Appendix B — Worktree follow-up
- To **keep** Phase 6 rename: cherry-pick or copy `DriftDetector.cs` from worktree to main branch.
- To **discard** worktree: `git worktree remove "c:\Code-Repo\DotNet-Firewall-Analyzer-wt-mcp-20260409"`.
