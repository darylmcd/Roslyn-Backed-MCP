# MCP Server Audit Report

## 1. Header

- **Date:** 2026-04-09 (UTC run stamp `20260409T164306Z`)
- **Audited solution:** `c:\Code-Repo\IT-Chat-Bot-worktrees\mcp-audit-20260409T164306Z\ITChatBot.sln`
- **Audited revision:** branch `mcp-audit/20260409T164306Z` @ `c728daf` (merge PR #47)
- **Entrypoint loaded:** `ITChatBot.sln`
- **Audit mode:** `full-surface`
- **Isolation:** disposable git worktree `c:\Code-Repo\IT-Chat-Bot-worktrees\mcp-audit-20260409T164306Z` (created before any Roslyn write-capable tool calls)
- **Client:** Cursor agent session (`user-roslyn` MCP); MCP prompt invocation (`prompts/get`) not verified in this client
- **Workspace id:** `43708f2d27a445459f690fd2d9e1f2bd` (`WorkspaceVersion` **6** after 2026-04-09 continuation `workspace_reload`)
- **Server:** `roslyn-mcp` **1.8.2+7b4b0ad0f8eba500092a68a4bee90cfd5b0bcecc**
- **Catalog version:** `2026.04`
- **Roslyn / .NET:** Roslyn 5.3.0.0; runtime **.NET 10.0.5**; OS Windows 10.0.26200
- **Scale:** 34 projects, 750 documents
- **Repo shape:** multi-project, tests present, analyzers present (663 rules / 34 analyzer assemblies in `list_analyzers` sample), **no** `Directory.Packages.props` (Central Package Management previews → `skipped-repo-shape`), single target **net10.0** per project, DI-heavy, source generators likely (implicit usings / ASP.NET)
- **Prior issue source for Phase 18:** `ai_docs/backlog.md` — no Roslyn-MCP-specific rows; Phase 18 marked **N/A** for backlog cross-walk
- **Debug log channel:** `no` — structured `notifications/message` not captured in the agent transcript
- **Plugin skills repo path:** `C:\Users\daryl\.claude\plugins\cache\roslyn-mcp-marketplace\roslyn-mcp\1.7.0\skills` (13 `SKILL.md` files observed vs 10 in prompt snapshot → **drift**)
- **Report path note:** audit target is IT-Chat-Bot workspace; *intended* canonical store for raw server audits is Roslyn-Backed-MCP `ai_docs/audit-reports/` per prompt — copy if merging into server backlog
- **Run completeness (honest):** Continuation pass added **Phase 4** (Slack `HandleSlackPostAsync`: `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree`, `get_source_text`), **Phase 5** (full `analyze_snippet` matrix + `evaluate_csharp` including script timeout ~13s for `timeoutSeconds=3`), **Phase 7** (`get_editorconfig_options`, `set_editorconfig_option`, `get_msbuild_properties` allowlist, `evaluate_msbuild_property`, `evaluate_msbuild_items`), **Phase 8** (`workspace_reload`, `build_workspace`, `build_project`, `test_discover` totalCount **834** / page, `test_related_files`, `test_related`, `test_run` filtered + **full solution** aggregated **905** tests / **797** passed / **108** failed, `test_coverage` no coverlet), **Phase 8b** (sequential reader baselines only — see §16), **Phase 10** (`move_type_to_file_preview` for `PlanningStartedPayload`, `create_file_preview` audit marker — **preview only**), **Phase 9** (`apply_text_edit` marker + `revert_last_apply` — **reverted**), **Phase 11** (paired `semantic_search`, `find_reflection_usages`, `get_di_registrations` @ `Program.cs`, `source_generated_documents`). **Phase 3** `find_shared_members` on `ChatOrchestrationPipeline` still **InvalidArgument** (prior); retry on nested `FakeRecordHandler` → **0 shared** (PASS). **148-row ledger**, **Phases 12–16**, full **16b** per-skill table, full **17** negative matrix, **18** regressions, parallel **8b.2–8b.4**, and **final closure** remain **out of scope** for this checkpoint.

---

## 2. Coverage summary (session-only; not full ledger)

| Kind | Category (from catalog) | Exercised this session | Notes |
|------|-------------------------|-------------------------|-------|
| tool | server | `server_info` | |
| tool | workspace | `workspace_load`, `workspace_list`, `workspace_status`, `workspace_reload`, `project_graph` | |
| tool | analysis | `project_diagnostics`, `compile_check`, `diagnostic_details`, `list_analyzers`, `analyze_snippet` | |
| tool | advanced-analysis | `get_complexity_metrics`, `get_cohesion_metrics` (large output), `find_unused_symbols`, `get_nuget_dependencies` (large output), `semantic_search` | `get_namespace_dependencies` not called |
| tool | security | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` | 0 CVEs; ~10s scan |
| tool | validation | `test_run` | SysLog adapter tests only |
| tool | refactoring | `organize_usings_preview`, `format_document_preview` | empty previews (already clean) |
| tool | editing | `apply_text_edit` | Phase 6 (×2 corrective edits) |
| tool | symbols | `symbol_search`, `type_hierarchy`, `find_references` | partial Phase 3 |
| tool | scripting | `evaluate_csharp` | expression probe |
| resource | server | `roslyn://server/catalog`, `roslyn://server/resource-templates` | via fetch |
| prompt | prompts | — | **blocked** — client not exercised |

**Catalog totals (authoritative):** 123 tools (62 stable / 61 experimental), 9 resources (stable), 16 prompts (experimental) = **148** surface entries. This session did **not** assign a final `exercised|skipped|blocked` status to each row.

---

## 3. Coverage ledger

**N/A — full 148-row ledger not materialized.** Per `deep-review-and-refactor.md`, a follow-up pass must enumerate `roslyn://server/catalog` and assign each tool/resource/prompt exactly one terminal status. Below is a **session call log** (tools invoked at least once):

`server_info`, `workspace_load`, `workspace_list`, `workspace_status`, `workspace_reload`, `project_graph`, `project_diagnostics`, `compile_check`, `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`, `list_analyzers`, `diagnostic_details`, `get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols` (×2), `get_nuget_dependencies`, `symbol_search`, `type_hierarchy`, `find_references`, `analyze_snippet`, `evaluate_csharp`, `organize_usings_preview`, `format_document_preview` (×2), `code_fix_preview` (error path), `apply_text_edit` (×2), `test_run`, `semantic_search`.

---

## 4. Verified tools (working)

- `workspace_load` — ITChatBot.sln loaded; `_meta.elapsedMs` ~8870
- `project_diagnostics` — 0 errors, 143 warnings, 2076 info; first page ~23.7s (large solution)
- `compile_check` — `ITChatBot.Configuration`: emitValidation false ~5ms vs true ~176ms (restore present; delta visible)
- `nuget_vulnerability_scan` — 0 vulnerabilities, 34 projects, `_meta.elapsedMs` ~10003
- `test_run` — `ITChatBot.Adapters.SysLogServer.Tests`: **30 passed**, exit 0, ~7s
- `semantic_search` — query `async methods returning Task<bool>` → 4 relevant methods
- `find_references` — `ChatOrchestrationPipeline` → 8 refs, consistent with project graph
- `apply_text_edit` — applies and returns unified diff; validates column bounds (error on `EndColumn` past EOL)

---

## 5. Phase 6 refactor summary

- **Target repo / path:** IT-Chat-Bot worktree (see Header)
- **Scope:** `apply_text_edit` only (curated `code_fix_preview` for CA1852 unavailable — see Issues)
- **Changes:** `FakeRecordHandler` in `tests/adapters/SysLogServer/SysLogServerAdapterTests.cs` marked `sealed` (addresses CA1852 for that helper type). A mistaken first edit introduced `FakeRecordHandler : HttpMessageHandlerndler`; corrected by a second edit restoring `HttpMessageHandler`.
- **Tools:** `code_fix_preview` (failed — no curated fix), `apply_text_edit` (success, two edits)
- **Verification:** `workspace_reload`; `test_run` **ITChatBot.Adapters.SysLogServer.Tests** — PASS (30/30)
- **Git:** changes exist **only in the disposable worktree** working tree (not committed to main repo)

---

## 6. Performance baseline (`_meta.elapsedMs`)

| Tool | Calls | p50_ms (approx) | Notes |
|------|-------|-----------------|-------|
| `server_info` | 1 | 7 | |
| `workspace_load` | 1 | 8870 | gateMode `load` |
| `project_diagnostics` | 4+ | 89–23738 | wide spread by scope |
| `nuget_vulnerability_scan` | 1 | 10003 | within solution-scan budget |
| `semantic_search` | 1 | 1241 | |
| `test_run` | 1 | 7031 | single test project |
| `workspace_reload` | 1 | 6326 | heldMs vs elapsedMs differ in payload — **FLAG** for operator review |

---

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `project_diagnostics` | return_shape / description | Clarify meaning of `totalDiagnostics` vs severity-filtered pages | With `severity=Error`, returned page empty but `totalWarnings`/`totalInfo` still populated; `totalDiagnostics` was `0` on filtered call | FLAG | Compare with tool description invariant for totals vs filtered arrays |
| `workspace_reload` | `_meta` timing | `elapsedMs` aligns with wall-clock / `heldMs` | `heldMs` 12652 vs `elapsedMs` 6326 | FLAG | May be intentional parallel split — verify in server docs |

Otherwise: **No additional drift recorded.**

---

## 8. Error message quality

| Tool | Probe input | Rating | Notes |
|------|-------------|--------|-------|
| `code_fix_preview` | CA1852 @ SysLogServerAdapterTests | **actionable** | States no supported curated fix |
| `apply_text_edit` | `EndColumn` past EOL | **actionable** | Reports line length |
| `workspace_status` | bogus workspace id | **actionable** | `NotFound` + hints `workspace_list` |

---

## 9. Parameter-path coverage

| Family | Non-default path tested | Status |
|--------|--------------------------|--------|
| `project_diagnostics` | `severity=Error`, paging | pass (empty page; totals preserved) |
| `compile_check` | `emitValidation=true`, project filter | pass |
| `list_analyzers` | `project=ITChatBot.Api`, paging | pass |

---

## 10. Prompt verification (Phase 16)

**N/A — blocked** (MCP `prompts/list` / `prompts/get` not exercised through this Cursor tool surface).

---

## 11. Skills audit (Phase 16b)

| Skill (dir) | frontmatter_ok | tool_refs_valid | dry_run | notes |
|-------------|----------------|-----------------|--------|-------|
| (aggregate) | partial | not exhaustively validated | — | **13** skills under plugin cache `...\roslyn-mcp\1.7.0\skills\` vs **10** expected in prompt snapshot (`analyze`, `bump`, `complexity`, `dead-code`, `document`, `explain-error`, `migrate-package`, `publish-preflight`, `refactor`, `review`, `security`, `test-coverage`, `update`) |
| `analyze` | yes (sample) | yes | pass | references `workspace_load`, `project_diagnostics`, `compile_check`, etc. — present in live catalog |

---

## 12. Experimental promotion scorecard

**N/A — incomplete.** Final closure requires one recommendation per experimental entry (77 experimental surfaces per 2026.04 snapshot). This session does not satisfy rubric for `promote` / `deprecate` rollups.

---

## 13. Debug log capture

`client did not surface MCP log notifications`.

---

## 14. MCP server issues (bugs)

1. **Curated fix gap** — `code_fix_preview` / `diagnostic_details` for CA1852: details return empty `SupportedFixes`; preview errors with “does not have a supported curated code fix”. **Severity:** missing data / UX gap (analyzer suggests fix path exists in IDE but not exposed here). **Reproducibility:** always (this occurrence).

2. **Operator error surfaced via tool** — `apply_text_edit` allowed a bad replacement that corrupted text (`HttpMessageHandlerndler`); server behaved as specified. **Severity:** cosmetic / workflow — prefer semantic `sealed` fix from Roslyn if available.

3. **`find_shared_members` locator** — `ChatOrchestrationPipeline` via `symbolHandle` / `file:line` → **InvalidArgument** “Syntax node is not within syntax tree” (partial-class / snapshot mismatch hypothesis; not re-resolved).

4. **`get_editorconfig_options` completeness** — After `set_editorconfig_option` wrote `dotnet_separate_import_directive_groups`, a follow-up `get_editorconfig_options` did **not** include that key in `Options[]` (disk `.editorconfig` confirmed the line). **Severity:** FLAG — options enumerator may omit keys Roslyn does not surface to this API.

5. **`semantic_search` query: “classes implementing IDisposable”** — Results included types that are **not** `IDisposable` implementors (e.g. test fixture classes). **Severity:** FLAG — predicate accuracy for interface implementation queries.

6. **`test_coverage`** — Returns `Success: true` with message to add **coverlet.collector**; no line/branch metrics. **Precondition** not met in repo — not a server bug.

7. **`test_run` full solution** — **797 passed / 108 failed / 905 total**; failures cluster in `ITChatBot.Integration.Tests` (**CloudAdapter** DI ambiguous constructors at `Program.cs:218`). **Repo/host configuration issue**, not MCP aggregation error (structured totals match `dotnet test` behavior).

8. **`evaluate_csharp` infinite loop** — With `timeoutSeconds: 3`, error returned at **~13s** (`3s + ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS 10`); message documents abandoned worker / host restart hint — **PASS** vs schema.

**No server crash observed.**

---

## 15. Improvement suggestions

- Expose or document why CA1852 lacks `code_fix_preview` when IDE can apply “Make sealed”.
- Clarify `project_diagnostics` JSON fields when `severity` filter narrows the page but global warning/info counts remain non-zero.
- Align `deep-review-and-refactor.md` Phase 16b expected skill count (**10**) with shipped plugin (**13**).

---

## 16. Concurrency matrix (Phase 8b)

**8b.2–8b.4 — blocked:** Cursor agent host **serializes** MCP tool calls; parallel read fan-out and read/write interleaving were not exercised.

**8b.1 — sequential baselines** (same workspace id; `_meta.elapsedMs`):

| Probe | Tool | elapsedMs (approx) |
|-------|------|-------------------|
| R1 | `find_references` @ `ChatOrchestrationPipeline.cs:26:30` | 332 |
| R2 | `project_diagnostics` `file=SlackWebhookEndpoints.cs` `limit=10` | 11405 |
| R3 | `symbol_search` `query=Test` | large result (~1.3k lines JSON in agent log); **`_meta` not preserved** in export |
| R4 | `find_unused_symbols` `includePublic=false` | 619 |
| R5 | `get_complexity_metrics` (default top 50) | 571 |

---

## 17. Writer reclassification verification (Phase 8b.5)

| # | Tool | Status |
|---|------|--------|
| 1 | `apply_text_edit` | **exercised** — Phase 9 marker on `SlackWebhookEndpoints.cs`; **reverted** via `revert_last_apply` (`heldMs` ~6404) |
| 2 | `apply_multi_file_edit` | **not executed** |
| 3 | `revert_last_apply` | **exercised** — see Phase 9 |
| 4 | `set_editorconfig_option` | **exercised** — appended `[*.{cs,csx,cake}]` `dotnet_separate_import_directive_groups = false` |
| 5 | `set_diagnostic_severity` | **not executed** |
| 6 | `add_pragma_suppression` | **not executed** |

---

## 18. Response contract consistency

**N/A — no cross-tool field-name mismatch recorded** beyond §7.

---

## 19. Known issue regression check (Phase 18)

**N/A — no Roslyn MCP backlog rows in `ai_docs/backlog.md`** for this product repo; no prior IT-Chat-Bot MCP audit cross-reference required.

---

## 20. Known issue cross-check

**N/A.**

---

## Disposable worktree cleanup

When finished inspecting:

```powershell
git worktree remove "c:\Code-Repo\IT-Chat-Bot-worktrees\mcp-audit-20260409T164306Z" --force
git branch -D "mcp-audit/20260409T164306Z"
```

(Optionally merge or cherry-pick the `sealed` test helper change from the worktree into main if desired.)

---

*Draft sibling removed: this file supersedes `20260409T164306Z_itchatbot_mcp-server-audit.draft.md` checkpoint.*
