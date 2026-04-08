# MCP Server Audit — DotNet-Firewall-Analyzer (Roslyn MCP)

**generated_at (UTC):** 2026-04-08T03:40:00Z  
**repo-id:** `dotnet-firewall-analyzer`  
**entrypoint:** `c:\Code-Repo\DotNet-Firewall-Analyzer\FirewallAnalyzer.slnx`  
**workspace_session:** `4769c188593641658af9bd3f5cd12142` (through most phases)  
**audit_mode:** `conservative` — mutations kept minimal; working tree is the main repo (no disposable worktree created for this session).  
**expected_lock_mode (env):** `rw-lock` (`ROSLYNMCP_WORKSPACE_RW_LOCK=true`, confirmed via `evaluate_csharp` reading the env var).  
**server:** `roslyn-mcp` **1.6.1+f16452924ef03cd3ed17dc36b53efab29c72217a**, catalog **2026.03**, Roslyn **5.3.0.0**, runtime **.NET 10.0.5**, OS **Microsoft Windows 10.0.26200**  
**live_surface:** 56 stable + 67 experimental tools; 7 stable resources; 0 stable + 16 experimental prompts (matches `server_info` and `roslyn://server/catalog`).  
**debug_log_channel:** Not verified from this agent loop — Cursor did not surface `notifications/message` payloads in tool results; treat as **client limitation** for this run.

**completeness:** This file is a **representative deep-review pass** (Phases 0–3, 5, partial 1–2, 8 test execution). It does **not** satisfy the living prompt’s exhaustive per-tool ledger or Phase **8b** wall-clock matrix (second lock-mode lane not run). Remaining catalog entries are marked **`skipped-session-depth`** below.

**auto-pair (Phase 0.16):** Run **1** of a dual-mode pair. No matching `*_*_legacy-mutex_mcp-server-audit.md` partial found under `ai_docs/audit-reports/`.

**Report path note:** This is the mode-1 (`rw-lock`) partial. For a full concurrency matrix, run `./eng/flip-rw-lock.ps1` (or equivalent), restart the MCP host so it picks up the flipped env var, and re-invoke `ai_docs/prompts/deep-review-and-refactor.md` so Phase **8b** Session B columns can be filled.

---

## Repo shape

| Constraint | Value |
|------------|--------|
| Projects | 11 (5 product + 1 CLI + 5 test + E2E) |
| Multi-targeting | No (`net10.0` only) |
| Central Package Management | Yes (`centrally-managed` + `ResolvedCentralVersion` in `get_nuget_dependencies`) |
| Tests | Yes — xUnit; `test_run` **297 passed** with filter `Category!=E2E` |
| Analyzers | Yes — **25** assemblies, **492** rules (`list_analyzers` totals) |
| Source generators | Not specialized in this pass (`source_generated_documents` not called — **skipped-session-depth**) |
| DI | Yes (minimal Web API wiring; `get_di_registrations` not called — **skipped-session-depth**) |
| `.editorconfig` | Present (not re-verified in Phase 7 this session) |
| Restore | `dotnet restore FirewallAnalyzer.slnx` **PASS** before semantic tools |

---

## Phase checkpoints (evidence summary)

### Phase 0 — PASS
- `server_info`, `roslyn://server/catalog`, `roslyn://server/resource-templates`: consistent counts.
- `workspace_load` / `workspace_list` / `workspace_status` / `project_graph`: **PASS**; workspace clean (`WorkspaceDiagnostics` empty).
- Tool `_meta.gateMode` on workspace calls: **`rw-lock`** (useful signal; complements env-based tracking).

### Phase 1 — PASS (diagnostic_details N/A)
- `project_diagnostics` (paged + filtered): **0** issues; paging behaved (`hasMore: false`).
- `compile_check` default + `emitValidation: true`: **0** CS diagnostics; emit path ~2.5s vs ~1.9s for default page (expected magnitude gap per tool description).
- `security_diagnostics` / `security_analyzer_status`: structured; **0** findings; SecurityCodeScan present.
- `nuget_vulnerability_scan`: **0** CVEs, 11 projects, ~4.4s; `IncludesTransitive: false` noted.
- `list_analyzers`: totals **25** / **492**; first page `hasMore: true` **PASS**.
- **`diagnostic_details`:** **skipped-repo-shape** (no error/warning instances to target).

### Phase 2 — PASS
- `get_complexity_metrics`: plausible hotspots (`CollectUnknownYamlKeyMessages` CC=20, nesting 4).
- `get_cohesion_metrics`: high LCOM on xUnit test classes expected; `JobProcessor` / `FileSnapshotStore` clusters plausible.
- `find_unused_symbols` `includePublic=false`: **0**; `includePublic=true`: **50** hits — many are **false-positive-prone** (tests, middleware `InvokeAsync`, enum members, DTO properties) — **aligns with `ai_docs/backlog.md` standing rule** to verify before deletion.
- `get_namespace_dependencies`: **exercised** (large JSON; ~52 KB agent spill file).
- `get_nuget_dependencies`: **PASS**; CPM surfaced correctly.

### Phase 3 — PASS (subset)
- Focus type: **`DriftDetector`**
- `symbol_search` → `symbol_info`, `document_symbols`, `type_hierarchy`, `find_references` (10 refs), `find_consumers`, `impact_analysis`: **PASS**; reference counts consistent with `impact_analysis` (10 direct refs, 10 affected declarations).
- **FLAG (minor):** `find_consumers` reported `DependencyKinds: ["Other"]` for all three consumers — less granular than constructor/field/parameter taxonomy for this static type.
- Not exercised this session: `find_implementations`, `find_type_usages`, `find_type_mutations`, `find_property_writes`, `member_hierarchy`, `symbol_relationships`, `symbol_signature_help`, `callers_callees`, etc. → **`skipped-session-depth`**.

### Phase 4 — skipped-session-depth
- Flow tools not invoked to save scope.

### Phase 5 — PASS
- `analyze_snippet` `kind=expression` `1+2`: valid.
- `analyze_snippet` broken `kind=statements`: **CS0029** at line 1, columns **9–16** (points into assignment / value region — acceptable versus old wrapper-column bug).
- `evaluate_csharp` `Enumerable.Range(1,10).Sum()`: **55** (**PASS**).
- `evaluate_csharp` `while(true){}`: **FAIL** as expected after **~20s** with explicit abandonment message (10s script budget + 10s watchdog grace), `ProgressHeartbeatCount: 10` — **PASS** (no unbounded hang).

### Phase 6 refactor summary
- **`organize_usings_preview`** on `DriftDetector.cs`: **empty `Changes`** → no apply.
- **`fix_all_*` / `rename_*` / extractions:** not executed — solution has **zero** analyzer/compiler issues to drive batched fixes; applying large refactors would be out-of-scope cosmetic churn.
- **Net repo diff from Phase 6:** none.

### Phase 7–18
- **skipped-session-depth** except: `test_discover` (large output), `test_run` (below), and env lock probe via `evaluate_csharp`.
- **Phase 8b concurrency matrix:** Session B columns **`skipped-pending-second-run`**; no parallel R1–R5 timings or lifecycle stress in this session.

### Phase 8 — PASS (filtered tests)
- `test_discover`: exercised (full listing large).
- `test_run` `Category!=E2E`: **ExitCode 0**, **297 passed**, 0 failed. E2E assembly reported no tests matching filter (expected).

---

## MCP server issues (FAIL / FLAG)

| id | severity | area | observation |
|----|----------|------|-------------|
| DOC-001 | FLAG | prompt vs binary | Living prompt footer references **server v1.7+** pagination/`analyze_snippet` fixes; this host is **1.6.1** — docs/prompt revision lag, not necessarily a runtime bug. |
| UX-001 | FLAG | `find_consumers` | Dependency kind granularity coarse (`Other` only) for `DriftDetector` static usage. |
| COV-001 | INFO | process | Exhaustive catalog coverage + dual-mode Phase **8b** not completed in one agent session. |

No tool crashes, empty success on failure, or schema contradictions observed in exercised calls.

---

## Coverage ledger (abbreviated)

**Legend:** `exercised` · `exercised-preview-only` · `skipped-repo-shape` · `skipped-session-depth` · `skipped-safety` · `blocked`

### Exercised (this run)
`server_info`, `workspace_load`, `workspace_list`, `workspace_status`, `project_graph`, `project_diagnostics`, `compile_check`, `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`, `list_analyzers`, `get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols` (×2), `get_namespace_dependencies`, `get_nuget_dependencies`, `symbol_search`, `symbol_info`, `document_symbols`, `type_hierarchy`, `find_references`, `find_consumers`, `impact_analysis`, `analyze_snippet` (×2), `evaluate_csharp` (×3), `organize_usings_preview`, `test_discover`, `test_run`.

### Resources
`roslyn://server/catalog`, `roslyn://server/resource-templates` — **exercised**.  
Workspace-scoped resources (`roslyn://workspaces`, `…/status`, `…/projects`, `…/diagnostics`, `…/file/…`) — **`skipped-session-depth`**.

### Prompts (×16 experimental)
MCP **prompt** invocation from `call_mcp_tool` not used in this session — all prompts **`skipped-session-depth`** / **`blocked`** depending on client capability (prompts are MCP prompt templates, not JSON tools in this Cursor wiring).

### All other catalog tools
Default **`skipped-session-depth`** unless listed above.

---

## Concurrency mode matrix (Phase 8b) — partial

| Probe | Session A (`rw-lock`) | Session B (`legacy-mutex`) |
|-------|------------------------|-----------------------------|
| Lock mode evidence | `evaluate_csharp` → env `"true"`; `_meta.gateMode` `rw-lock` on tools | `skipped-pending-second-run` |
| R1–R5 sequential baselines (ms) | not measured | `skipped-pending-second-run` |
| Parallel fan-out speedup | not measured | `skipped-pending-second-run` |
| Read/write exclusion | not measured | `skipped-pending-second-run` |
| Lifecycle stress | not measured | `skipped-pending-second-run` |
| Writer reclassification (8b.5) | not measured | `skipped-pending-second-run` |

---

## Regression / backlog cross-check

- `ai_docs/backlog.md`: no open Roslyn-MCP defect rows; **standing rule** on `find_unused_symbols` public results matches observed need for human verification before removal.

---

## Performance notes

| Tool | Approx heldMs / duration | Note |
|------|--------------------------|------|
| `project_diagnostics` | ~4.2–4.6s | Clean solution; acceptable cold path |
| `compile_check` emitValidation | ~2.5s | Empty diagnostic set |
| `nuget_vulnerability_scan` | ~4.4s | Network/metadata dependent |
| `find_unused_symbols` public | ~560ms | 50-symbol cap in response |
| `test_run` filtered | ~11.9s | Local workstation |

---

## Prior findings alignment

N/A — no Roslyn-Backed-MCP issue ids provided for this workspace; backlog MCP-specific rows absent.
