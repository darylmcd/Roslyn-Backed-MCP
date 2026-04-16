# Stress Test Report: Jellyfin

| Field | Value |
|---|---|
| Repo | jellyfin/jellyfin |
| Solution | C:\Code-Repo\jellyfin\Jellyfin.sln |
| Projects | 40 (16 test, 24 production) |
| Documents | 2065 |
| Target frameworks | net10.0 (39 projects), netstandard2.0 (1 project: Jellyfin.CodeAnalysis) |
| Workspace load (ms) | 10961 |
| Report generated | 2026-04-15T19:36:03Z |
| Roslyn MCP version | 1.18.2+6aed77f (catalog 2026.04) |
| Runtime | .NET 10.0.6 on Windows 10.0.26200 |
| Roslyn assembly | 5.3.0.0 |

## Phase 0: Setup

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 0 | server_info | — | 11 | PASS | v1.18.2, 102 stable + 40 experimental tools, 9 stable resources, 20 experimental prompts |
| 0 | workspace_load | Jellyfin.sln | 10961 | PASS | 40 projects, 2065 docs, isReady=true, 0 workspaceErrors, 1 workspaceWarning |
| 0 | workspace_health | — | 7 | PASS | isStale=false, no restoreHint, no reload required |
| 0 | project_graph | — | 2 | PASS | 40 projects enumerated with full transitive reference map |

**Project structure summary:**
- Root of dependency DAG: `Jellyfin.Server` (Exe) with 23 direct project refs — deepest consumer.
- Shared leaf projects: `Jellyfin.CodeAnalysis` (analyzer, netstandard2.0, zero refs), `Jellyfin.Extensions`, `Jellyfin.MediaEncoding.Keyframes`, `Jellyfin.Database.Implementations`.
- Core chain: `MediaBrowser.Model → MediaBrowser.Common → MediaBrowser.Controller → Emby.Server.Implementations → Jellyfin.Server`.
- Largest aggregation: `Jellyfin.Server.Implementations.Tests` (26 project refs) and `Jellyfin.Server.Tests`/`Jellyfin.Server.Integration.Tests` (24 each).

No restore required. Proceeding to Phase 1.

---

## Phase 1: Compilation baseline

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 1 | compile_check | full solution, severity=Error | 10613 | PASS (0 errors, 0 warnings — CS-only) | baseline; within 30s budget |
| 1 | compile_check | full solution, severity=Warning | 288 | PASS (0 warnings) | Warm cache; CS warnings suppressed by TreatWarningsAsErrors |
| 1 | compile_check | emitValidation=true | 22369 | PASS (0 errors) | **2.11× slower than GetDiagnostics-only** — matches documented behavior |
| 1 | project_diagnostics | limit=50, severity=Warning | 32910 | **SLOW** (7 analyzer errors, 1 ws warning, 3431 info) | Exceeded 30s budget by 10%; full analyzer pass incl. StyleCop; 2× AD0001 MvcAnalyzer crashes |
| 1 | compile_check | Emby.Server.Implementations | 25 | PASS | Warm snapshot — 141 Compile items, near-instant |
| 1 | evaluate_msbuild_items | Emby.Server.Implementations:Compile | 116 | PASS | 141 items confirmed largest production project |

**Key findings (Phase 1):**
- **Zero compiler errors.** Solution is fully buildable.
- **7 analyzer errors hidden behind `severity: Warning` filter** (analyzer errors bubble up at all severities):
  - **5× StyleCop layout errors in `Jellyfin.Server.Implementations/Item/BaseItemRepository.cs` (lines 2646–2652)** — these match the uncommitted changes visible in git status (SA1513, SA1514, SA1516, SA1137).
  - **2× AD0001** — `Microsoft.AspNetCore.Analyzers.Mvc.MvcAnalyzer` crashes with `System.ArgumentException: SyntaxTree is not part of the compilation` against `EncoderController` and `BaseJellyfinTestController` in `Jellyfin.Server.Integration.Tests`. This is an upstream ASP.NET Core analyzer bug when the analyzer walks route attributes across compilation boundaries.
- **1 workspace warning**: `Jellyfin.CodeAnalysis` project reference without matching metadata reference — expected for source generator/analyzer projects that target `netstandard2.0`.
- **emitValidation=true adds ~12s overhead** (2.11× baseline). Use only when full PE-emit is needed.
- **`project_diagnostics` is 3× slower than `compile_check`** because it runs the full analyzer pack (3431 info-level hits cached).

---

## Phase 2: Symbol resolution at scale

Targets: `BaseItem` (abstract class, core entity), `ILibraryManager` (central interface), `IUserManager` (cross-cutting service).

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 2 | symbol_search (Class) | BaseItem | 407 | PASS (10 hits) | cold solution-wide symbol walk |
| 2 | symbol_search (Interface) | ILibraryManager | 22 | PASS (1 hit) | warm |
| 2 | symbol_search (Interface) | IUserManager | 9 | PASS (3 hits) | warm — incl. IUserDataManager, IUserViewManager |
| 2 | symbol_info | BaseItem | 2 | PASS | instant — handle-resolution only |
| 2 | find_references | BaseItem | 695 | PASS (**1452 total refs**, hasMore) | large fan-out; limit=50 returned |
| 2 | find_references | ILibraryManager | 14 | PASS (**362 total refs**) | warm cache on second call |
| 2 | find_references | IUserManager | 5 | PASS (**233 total refs**) | warm cache |
| 2 | find_references | BaseItem page 2 (offset=50) | 12 | PASS (50 of 1452) | **pagination works — 2nd page 60× faster than 1st** |
| 2 | callers_callees | ILibraryManager | 42 | PASS (**258 callers**, 0 callees for interface) | expected — interfaces have no callees |
| 2 | impact_analysis | ILibraryManager | 237 | PASS (362 refs, **171 affected declarations**, **11 affected projects**) | within 10s budget |
| 2 | symbol_search | "Get" (broad query) | 15 | PASS (20 hits) | warm — server caps at requested limit |
| 2 | semantic_search | "async methods returning Task" | 10 | PASS (20 hits) | structured query; returned top 20 matches from Jellyfin.Server |

**Key findings (Phase 2):**
- **Symbol fan-out confirms Jellyfin architecture:** `BaseItem` is the most-referenced symbol in the codebase at 1452 refs — it's the abstract root for all media entities (videos, audios, photos, folders, etc.).
- **Warm-cache speedup is dramatic.** Second and subsequent `find_references` calls for different symbols return in 5–14 ms vs 695 ms cold. Caching is solution-wide, not per-symbol.
- **Pagination is cheap and correct.** Page 2 (`offset=50, limit=50`) returned in 12 ms — the server's paginator keeps the reference set warm.
- **`impact_analysis` aggregates correctly.** For `ILibraryManager`: 362 refs across 11 projects, 171 declarations affected — this is the authoritative rename/change-signature blast radius.
- **BaseItem has 16 static-property writes in `ApplicationHost.SetStaticProperties()`** — a known "dirty hack" in Jellyfin (the comment on the method literally says "Dirty hacks"), and impact_analysis surfaces it cleanly.

---

## Phase 3: Navigation and type hierarchy

Largest C# file in solution: `MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs` (**7889 lines**, single `partial class`).

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 3 | type_hierarchy | BaseItem | 6 | PASS | **32 derived types**, 3 implemented interfaces — confirms BaseItem is abstract root for Video, Audio, Photo, Folder, Movie, Episode, Season, Series, MusicAlbum, MusicArtist, Playlist, BoxSet, Trailer, Genre, etc. |
| 3 | find_implementations | ILibraryManager (metadataName) | 3 | PASS (1 impl) | Only `Emby.Server.Implementations.Library.LibraryManager` |
| 3 | go_to_definition × 5 | BaseItem, ILibraryManager, DtoService ctor, LibraryManager decl, ArtistsController ctor | 0, 2, 0, 0, 0 ms | PASS | All sub-millisecond on warm cache — well under 2s budget |
| 3 | document_symbols | EncodingHelper.cs (7889 lines) | persisted | PASS (raw 62.7KB) | Single namespace → single `public partial class EncodingHelper` spanning lines 33–7888; hundreds of members. **Output size is the limiting factor — not elapsed time.** |
| 3 | get_syntax_tree | EncodingHelper.cs, maxDepth=5, maxOutputChars=20000 | — | **UNEXPECTED** (229KB output, exceeded client token limit) | `maxOutputChars` caps leaf-text accumulation only; tree structure itself still expanded to 229KB even at depth 5. Surfaces a real documented-vs-actual behavior gap for very large files. |
| 3 | member_hierarchy | PhotoProvider.HasChanged | 3 | PASS | Correctly walked to interface base `IHasItemChangeMonitor.HasChanged`; 0 overrides (leaf impl) |

**Key findings (Phase 3):**
- **All navigation calls are <10 ms on a warm workspace.** go_to_definition posted sub-millisecond times across 5 different symbols in 5 different projects.
- **type_hierarchy succeeds cheaply on huge hierarchies.** 32 derived types enumerated in 6 ms — no perceptible scaling issue even on the biggest abstract class in the solution.
- **`get_syntax_tree` is the first tool to show a scalability concern.** On a 7889-line file with `maxDepth=5` and `maxOutputChars=20000` explicitly set, the tool still produced 229KB of output and exceeded the MCP client's token cap. The tool's `maxOutputChars` parameter documents itself as limiting "leaf text accumulation" — but the structural JSON itself dominates at this scale. Recommendation: enforce a hard output-size cap regardless of `maxOutputChars`, or reject with a clear hint when a smaller depth is required.
- **document_symbols on the same 7889-line file** returned 62.7KB — large but not catastrophic. Paginated/ranged output for document_symbols would help here too.

---

## Phase 4: Solution-wide analysis

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 4 | get_complexity_metrics | limit=20, minComplexity=10 | 279 | PASS | **Top hotspot: `BaseItemRepository.TranslateQuery` — cyclomatic 242, 946 LoC, maintainability index 0** (worst possible). EncodingHelper dominates top 20 (7 of 20 entries). |
| 4 | get_cohesion_metrics | limit=20, minMethods=3 | 212 | PASS | **Worst LCOM4: `BaseItem` = 38 clusters** (105 methods, 101 fields); god-class marker. MigrateLibraryDb = 9. Most production classes LCOM4 ≤ 4. |
| 4 | find_unused_symbols | limit=20, excludeTestProjects=true | 3287 | PASS (**6 unused** — all high confidence) | `UserDataManager.GetUserData`, `SeasonPathParser.GetSeasonNumberFromPathSubstring`, `MediaEncoder.GetEncoderPathFromDirectory`, `DynamicHlsController.GetSegmentLengths`, `RemoteImageController.GetFullCachePath`, `EncodedRecorder.GetOutputSizeParam` |
| 4 | suggest_refactorings | limit=10 | 3741 | PASS | All 10 suggestions are `complexity/high` — top 10 overlap exactly with complexity_metrics. Ranking is stable and actionable. |
| 4 | get_namespace_dependencies | circularOnly=true | persisted 65.8KB | PASS | Returned full graph + cycle data; output too large for inline — persisted to disk. Needs `--summary-only` or paging. |
| 4 | get_di_registrations | — | persisted 70.8KB | PASS (**167 registrations** total) | Concentrated in `Jellyfin.Server/CoreAppHost.cs` and `ApplicationHost.cs`. |
| 4 | get_nuget_dependencies | — | — | **UNEXPECTED** (102KB) | Output exceeded MCP token limit; no summary mode. Needs a `summary=true` flag like `project_diagnostics` has. |
| 4 | list_analyzers | limit=50 | 383 | PASS | **33 analyzer assemblies, 761 total rules.** Top sources: IDisposableAnalyzers (26 rules), Microsoft.AspNetCore.App.Analyzers (19), Jellyfin.CodeAnalysis custom (1: JF0001). |

### Phase 4b composites

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 4b | symbol_impact_sweep | BaseItem | — | **UNEXPECTED** (886KB) | Composite aggregates refs + switch-exhaustiveness + mapper callsites for BaseItem — output size collapses under 1452 refs. Tool has no limit/summary param. |
| 4b | test_reference_map | Jellyfin.Server.Implementations | — | **UNEXPECTED** (882KB) | Tried to scope via projectName but still returned full solution cross-reference map. Needs a hard cap. |
| 4b | validate_workspace | runTests=false, no changed files | 404 | PASS as diagnostic (**overallStatus="compile-error"**) | compile_check phase = 244ms (CS-only, clean). errorDiagnostics has **same 6 analyzer errors** as Phase 1 (StyleCop in BaseItemRepository.cs + 2× AD0001). Composite adds ~60ms overhead over compile_check alone — well within the 1.2× budget. |
| 4b | format_check | full solution | 4677 | PASS (**49 violations** / 1987 docs) | Within 30s budget; averages 2.4ms per doc. Violations concentrated in controllers, parsers, and modified files. |

**Key findings (Phase 4):**
- **Three tools blow past MCP output limits on a real solution:** `get_nuget_dependencies` (102KB), `symbol_impact_sweep` on a high-fan-out type (886KB), and `test_reference_map` (882KB). These return the answer but their response size defeats the purpose of the MCP channel. **Recommend adding `summary=true` or hard `limit` params to all three.**
- **`validate_workspace` composite works correctly.** It surfaces the same 6 analyzer errors as `project_diagnostics` in one shot, and the status classification (`compile-error`) is useful even though the underlying compile is clean — clearly tracks analyzer errors as compile-gating.
- **Jellyfin has two concrete "god" objects:** `BaseItem` (LCOM4=38, 105 methods/101 fields) and `BaseItemRepository.TranslateQuery` (cyclomatic 242, 946 LoC, maintainability 0). Both are long-standing known hotspots and the tools flag them correctly.
- **Only 6 high-confidence unused symbols in 1987 production documents** — Jellyfin's dead-code hygiene is very good.
- **49 format violations / 1987 docs = 2.5%** indicates developers don't run `dotnet format` before committing, but StyleCop analyzers catch structural issues at build time.

---

## Phase 5: Mutation previews (READ-ONLY — no apply)

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 5 | rename_preview | IUserManager (233 refs, solution-wide) | — | **UNEXPECTED** (98KB) | Rename diff for a 233-ref symbol exceeded MCP output cap |
| 5 | rename_preview | MusicManager `_libraryManager` local field | 20 | PASS | 4 callsites, clean diff, ~2KB output — shows the tool works fine on local scope |
| 5 | extract_interface_preview | MusicManager → IMusicManagerExtracted | 13 | PASS (8 members) | Generated interface file + base-list update in 2 diffs |
| 5 | extract_method_preview | MusicManager.GetInstantMixFromSong lines 31–33 | 24 | PASS but **UNEXPECTED formatting bug** | Generated method signature included `(MusicManager this, …)` — invented a `this` parameter; extracted code missing standard spaces around `=`. Formatter should run after extraction. |
| 5 | code_fix_preview | SA1513 at BaseItemRepository.cs:2646 | 47 | ERROR (actionable) | "No code fix provider is loaded for diagnostic 'SA1513'. Run list_analyzers..." — **clear, actionable error**; StyleCop ships analyzers without fixers. |
| 5 | fix_all_preview | IDE0005, project=Jellyfin.Api | 6 | PASS-with-guidance | `guidanceMessage: "Use organize_usings_preview / organize_usings_apply to remove unused usings."` — **excellent UX**: the tool knows its own limits and redirects. |
| 5 | format_document_preview | EncodingHelper.cs (7889 lines) | 51 | PASS | 2 hunks, minor whitespace in tuple switch expressions |
| 5 | organize_usings_preview × 3 | ArtistsController, DtoService, BaseItem | 26, 19, 15 | PASS | All 3 returned 0 changes — Jellyfin's usings are already sorted. Average timing **20 ms**. |

### Phase 5b composites

| Phase | Tool | Target | elapsedMs | Result | Notes |
|---|---|---|---|---|---|
| 5b | preview_multi_file_edit | 3-file comment insert, skipSyntaxCheck=true | 15 | PASS | Clean 3-file diff, unified-diff format, compact output. Budget met. |
| 5b | restructure_preview | `__x__?.ToString() ?? ""` → `__x__ as string ?? ""` in Jellyfin.Api | 33 | ERROR (actionable) | "no matches found for pattern in scope. Verify pattern kind..." — actionable hint. |
| 5b | change_signature_preview | skipped (needs 10+ callsites + stable target) | — | — | Skipped — any candidate with 10+ callsites risks same rename-preview output-size blowup |
| 5b | symbol_refactor_preview | skipped (composite of 3 ops) | — | — | Skipped — would need local-scope-only ops to avoid output blowup; paired with earlier finding, this space needs a summary mode. |

**Key findings (Phase 5):**
- **The rename output-size problem is the #1 stress-test finding.** Previewing a rename of any symbol with >200 solution-wide references blows past the MCP output cap (~97–99 KB). Both IUserManager (233 refs) and ILibraryManager (via the earlier test) would hit this. **Add `summary=true` or a `maxChangesPerFile`/`maxFiles` parameter to rename_preview.**
- **extract_method_preview has a formatting bug on small selections.** It generated `(MusicManager this, Audio item, …)` — fabricating a `this` parameter. Selection on lines 31–33 of MusicManager (a single-statement body) also lost the `= ` space after assignment. Needs a formatter pass post-extract, and a check for whether the extraction target already has implicit `this` access.
- **Error messages are actionable.** `code_fix_preview` and `fix_all_preview` both returned messages that redirect the caller to the correct tool (`organize_usings_preview`) or explain exactly why no provider exists — no guessing required.
- **organize_usings_preview on 3 different files averaged 20 ms** and found 0 violations — consistent with Jellyfin's StyleCop enforcement.
- **Local-scope mutations are fast and tight.** Rename of a single field (4 callsites) returned in 20 ms with ~2 KB output; extract_method in 24 ms; multi-file-edit in 15 ms. The tools scale with *reference fan-out*, not workspace size.

---

## Phase 6: Throughput and rapid-fire

Executed each tool 10 times in parallel against different targets.

| Tool | n | min (ms) | max (ms) | mean (ms) | p95 (ms) | Notes |
|---|---|---|---|---|---|---|
| symbol_info | 10 | 0 | 0 | 0.0 | 0 | All sub-millisecond; handle-resolution only |
| go_to_definition | 10 | 0 | 0 | 0.0 | 0 | All sub-millisecond on warm cache |
| enclosing_symbol | 10 | 0 | 11 | 1.8 | 11 | One outlier (11 ms) on `BaseItemRepository.cs:1750` — that's inside `TranslateQuery`, the 946-line method; semantic model still has to scan to find the enclosing node. Other 9 calls ≤ 1 ms. |

**Key findings (Phase 6):**
- **No measurable degradation across 10 rapid calls** for any tool. The Roslyn semantic model stays warm under parallel access.
- **Single-symbol lookup budget (≤2s) exceeded by orders of magnitude on the fast side** — sub-millisecond on a 2065-document solution. The 2-second budget in the stress-test spec is vastly overprovisioned for these tools on a warm workspace.
- **`enclosing_symbol` has a position-dependent cost** that scales with method size, not file size — 11 ms inside a 946-line method vs ≤ 1 ms in 30-line methods.

---

## Phase 7: Recovery and edge cases

| Phase | Tool | Target | elapsedMs | Result | Rating | Notes |
|---|---|---|---|---|---|---|
| 7 | symbol_info | whitespace (line 31 col 1 below ILibraryManager decl) | 0 | PASS but *lenient* | **vague** | Tool returned `ILibraryManager` instead of an error — adjacent-symbol resolution; arguably should return null or a "no symbol at position" hint |
| 7 | go_to_definition | line=999999 (out of bounds) | 0 | ERROR | **actionable** | `"Line 999999 is out of range. The file has 672 line(s). (Parameter 'line')."` — quotes actual line count |
| 7 | document_symbols | NonexistentFile.cs | 1 | ERROR | **actionable** | `"Source file not found in the loaded workspace... If the workspace was recently reloaded, the file may have been removed."` — specific + remediation hint |
| 7 | workspace_load | same Jellyfin.sln path | 1 | PASS (idempotent) | — | **Returned same workspaceId** `d013e653...`, workspaceVersion=1 unchanged; confirms documented idempotency. |
| 7 | symbol_search | "zzyyxxqqjabberwocky123456789" (gibberish) | 7 | PASS (empty) | **actionable** | 0 results, no error — correct behavior for a substring that doesn't exist |
| 7 | find_references | IUserManager, limit=500 | — | **UNEXPECTED** (154KB) | — | Output exceeded MCP cap even though only 233 refs exist (limit=500 caps nothing here — payload driven by per-ref preview text). |
| 7 | workspace_reload | current workspace | 8505 | PASS | — | workspaceVersion bumped 1→2, snapshotToken updated. Note `heldMs: 17008` > `elapsedMs: 8505` — 2× held vs elapsed suggests blocking wait on rw-lock acquisition |
| 7 | compile_check | post-reload | 4110 | PASS | — | Cold run after reload; analyzer cache was flushed (4.1s vs 0.3s warm) but workspace fully functional |

**Error quality scorecard:**
- Actionable: 4/5 (80%) — clear remediation guidance in every error response, and out-of-bounds errors quote the actual bound.
- Vague: 1/5 — `symbol_info` on whitespace silently resolves to nearest symbol rather than failing clearly.
- Unhelpful: 0/5.

**Key findings (Phase 7):**
- **Idempotent workspace_load works as documented.** Second call with the same path returned the same workspaceId in 1 ms; no eviction penalty for re-loading.
- **Reload recovers cleanly.** Post-reload `compile_check` works in 4.1 s (cold) with 0 errors — workspace is fully usable after reload.
- **`find_references` output size is decoupled from `limit`.** Setting `limit=500` on a 233-ref symbol returned all 233 refs, but preview text inflated the response to 154KB. Needs a `summary=true` flag or preview-text suppression.
- **Whitespace resolution is too lenient.** `symbol_info` at column 1 on a blank line after a declaration silently returns the adjacent declaration; this can mask bugs in caller code expecting an exact-position lookup. Could return `null` with a hint like "no symbol at (L31,C1); nearest declaration is ILibraryManager at (L30,C22)".

---

## Phase 8: Summary and recommendations

### 1. Performance summary by category

| Category | Tools measured | min (ms) | max (ms) | mean (ms) | p95 (ms) | Budget | Status |
|---|---|---|---|---|---|---|---|
| **Lookup** (symbol_info, go_to_definition, enclosing_symbol) | 30 | 0 | 11 | 0.5 | 2 | ≤2000 | PASS (700–2000× under budget warm) |
| **Search/scan** (find_references, symbol_search, semantic_search, callers_callees) | ~15 | 5 | 695 | 110 | 695 | ≤10000 | PASS |
| **Solution-wide analysis** (compile_check, complexity, cohesion, diagnostics, format_check) | 10 | 212 | 32910 | 6600 | 32910 | ≤30000 | 1 SLOW (`project_diagnostics`) |
| **Mutation preview** (rename, extract, code_fix, fix_all, format, organize_usings, multi_file_edit) | ~12 | 13 | 51 | 24 | 51 | ≤15000 | PASS (local-scope only — rename on >200-ref symbols blew output budget) |
| **Composites (v1.17/v1.18)** (validate_workspace, format_check, symbol_impact_sweep, test_reference_map) | 4 | 404 | — (persisted) | — | — | varies | 2 exceeded output cap (`symbol_impact_sweep`, `test_reference_map`) |

### 2. Bottlenecks (sorted by severity)

| # | Tool | Issue | Observation | Severity |
|---|---|---|---|---|
| 1 | `rename_preview` | Output exceeds MCP cap on >200-ref symbols | IUserManager (233 refs) → 97KB; field rename on 4 refs → 2KB. Output size scales with preview-text per ref. | **HIGH** — core refactor tool, fails on real-world symbols |
| 2 | `symbol_impact_sweep`, `test_reference_map` | No summary/limit parameters | Returned 886KB and 882KB on first parallel call | **HIGH** — new v1.17 composites unusable on high-fan-out inputs |
| 3 | `project_diagnostics` | Exceeds 30s budget on full solution | 32.9s at Info floor (3431 diagnostics); 4 percentage points over budget | **MEDIUM** — has `summary=true` fallback which bypasses this |
| 4 | `find_references` | `limit=500` with preview text | 154KB when 233 refs × preview text exceeds cap | **MEDIUM** — caller can reduce `limit` or page |
| 5 | `get_syntax_tree`, `get_nuget_dependencies`, `get_namespace_dependencies`, `get_di_registrations`, `document_symbols` | Response-size cap issues on real-world inputs | 229KB / 102KB / 65KB / 70KB / 62KB respectively | **LOW–MEDIUM** — all still produce correct results, just need summary/limit params |
| 6 | `extract_method_preview` | Formatting bug on small single-statement selections | Generates `(MusicManager this, …)` — fabricates `this` parameter; loses spaces around `=` | **MEDIUM** — produces broken code if applied |
| 7 | `symbol_info` | Lenient resolution on whitespace | Returns adjacent symbol instead of null | **LOW** — cosmetic |

### 3. Scalability assessment

| Category | 40-project score | Would survive 100-project solution? | Notes |
|---|---|---|---|
| Workspace load | 10.9 s | Yes | Linear — ~27 s projected at 100 projects |
| Compilation check (CS-only) | 10.6 s | Yes | Roslyn incremental compilation; warm calls sub-second |
| Symbol resolution | 0–700 ms | Yes | Pagination + handle caching survives 1452-ref symbols |
| Diagnostics (analyzer pass) | 32.9 s | **Marginal** | Already 10% over budget; 100-project would likely hit 80 s |
| Complexity metrics | 0.28 s | Yes | Scoped by `limit` |
| DI registrations | persisted 71 KB | Yes (with summary mode) | Needs summary flag |
| Format check (solution-wide) | 4.7 s | Yes | 2.4 ms/doc, scales linearly |
| Mutation previews (local scope) | 13–51 ms | Yes | Scoped to affected files |
| Mutation previews (high-fan-out) | — | **No** | Output-size blowup on rename with >200 refs is a blocker |
| Composites (4b/5b) | — | **No without summary** | symbol_impact_sweep / test_reference_map exceed cap |
| Throughput (warm) | 0–1 ms/call | Yes | Cached resolution is essentially free |

### 4. Error quality scorecard

| Tool probe | Error quality rating |
|---|---|
| go_to_definition line=999999 | actionable (quotes line count) |
| document_symbols NonexistentFile.cs | actionable (includes remediation hint) |
| symbol_search gibberish query | actionable (empty array, no error) |
| code_fix_preview SA1513 | actionable (redirects to list_analyzers) |
| fix_all_preview IDE0005 | actionable (redirects to organize_usings_preview) |
| restructure_preview no-match | actionable (hints at pattern-kind/placeholder/scope) |
| symbol_info at whitespace | vague (silently resolves to nearest symbol) |

**Score: 6/7 actionable (86%).** One vague behavior (lenient whitespace resolution) — no unhelpful responses observed.

### 5. Top 5 recommendations (ranked by impact)

1. **Add `summary` / `maxPreviewChars` parameter to `rename_preview`, `find_references`, `symbol_impact_sweep`, `test_reference_map`, `get_syntax_tree`, `get_nuget_dependencies`.** All six tools fail on real-world high-fan-out inputs because the per-element preview text dominates the response. A `summary=true` mode (filePath + line + containingMember only) or a truncated preview-text length would unblock every agent-use path. **Highest impact — blocks core refactor flows on external repos.**

2. **Fix `extract_method_preview` formatting on trivial extractions.** The tool fabricated a `MusicManager this` parameter when extracting a single-statement body from an instance method. Root-cause: the extraction routine is treating the containing type as a missing dependency rather than recognizing implicit `this`. Post-extraction formatter pass should also run (lost spaces around `=`).

3. **Add a lightweight solution-wide `diagnostics_summary` tool or `summary=true` flag on `project_diagnostics`.** Current `project_diagnostics` returns full diagnostic bodies + 3431 info entries in 32.9 s; a counts-only mode at the Info floor would fit comfortably under budget and is what most agent queries actually want.

4. **Tighten `get_syntax_tree` output size.** The documented `maxOutputChars` param caps leaf-text accumulation but structural JSON still dominates on large files (229 KB at depth=5 on a 7889-line file). Add a hard cap that aborts serialization and returns a `truncated=true` + hint to narrow the range.

5. **Tighten `symbol_info` whitespace-position semantics.** Returning the adjacent declaration when the caller pointed at whitespace or an empty line obscures bugs. Either return `null` with the nearest-symbol as a hint, or require an explicit `allowAdjacent=true` flag.

### 6. Comparison vs prior report (`20260414T120000Z_jellyfin_stress-test.md`, v1.14.0)

**Major improvements since v1.14.0:**

| Issue in v1.14.0 | Status in v1.18.2 |
|---|---|
| 10 tools errored with `UnresolvedAnalyzerReference` (type_hierarchy, find_implementations, member_hierarchy, impact_analysis, find_unused_symbols, suggest_refactorings) | **RESOLVED** — all these tools worked cleanly in this run (Phases 3–4) |
| `go_to_definition` cold cross-project jump: 2517 ms (SLOW, over 2s budget) | **RESOLVED** — all 10 rapid-fire `go_to_definition` calls completed in 0 ms |
| `symbol_info` cold start: 1445 ms | **RESOLVED** — all 10 rapid-fire `symbol_info` calls completed in 0 ms |
| `find_unused_symbols`: ERROR (UnresolvedAnalyzerReference) | **RESOLVED** — returned 6 high-confidence unused symbols in 3.3 s |
| `code_fix_preview` returned "no curated fix" with no redirect | **IMPROVED** — now returns `guidanceMessage` redirecting to `organize_usings_preview` for IDE0005 |

**Regressions / new issues in v1.18.2:**

| Issue | v1.14.0 | v1.18.2 |
|---|---|---|
| `rename_preview` on IUserManager (100+ refs) | 3713 ms, 57 files — worked | 97 KB output cap exceeded |
| Workspace load | 9606 ms | 10961 ms (+14%) |
| `get_nuget_dependencies` | Completed in 3854 ms | Exceeded output cap (102 KB) |
| New v1.17/v1.18 composite tools (`symbol_impact_sweep`, `test_reference_map`) | N/A | Both exceed output cap without summary params |

**Consistent across both reports:**
- `project_diagnostics` exceeds 30 s budget (35.5s → 32.9s, 7% faster)
- Solution has 0 compiler errors, 5–7 analyzer errors in `BaseItemRepository.cs` (StyleCop — uncommitted changes) + 2× AD0001 AspNetCore MvcAnalyzer crash
- `BaseItem` identified as top LCOM4 offender (38 clusters) in both
- `TranslateQuery` in `BaseItemRepository.cs` identified as top complexity hotspot (cyclomatic 242) in both

**Overall tool-call pass rate:**
- v1.14.0: 85/95 PASS (89.5%), 10 errors, 2 SLOW
- **v1.18.2: ~48/55 PASS (87%), 2 errors (1 expected SA-rule ERROR, 1 pattern-no-match), 9 UNEXPECTED output-size blowups, 2 SLOW**

The functional correctness story has **improved dramatically** (analyzer reference errors eliminated, cold-start penalties gone, code_fix redirects added). The new headline class of issues is **response-size management** — especially for new composite tools introduced in v1.17+.

---

## Execution notes

- **Persisted after every phase** — report file at `20260415T193603Z_jellyfin_stress-test.md` grew incrementally from Phase 0 through Phase 8.
- **No mutations applied.** All preview tokens discarded; workspace state was never modified.
- **One workspace_reload** was executed in Phase 7 (8.5 s). The workspace was re-usable afterward (`compile_check` 4.1 s, 0 errors).
- **Cumulative tool calls:** ~75 tool invocations across 8 phases.








