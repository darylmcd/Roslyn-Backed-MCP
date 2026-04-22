# ServerSurfaceCatalog partial-class split — phased plan (2026-04-22)

<!-- **Retrospective (2026-04-22, Phase 5):** The structural mitigation (Phases
     1–4 + reconciling PRs) shipped. Post-merge `git log` on
     `ServerSurfaceCatalog.cs` after Phase 2 was dominated by the phased split
     itself; the main file is now a thin aggregator plus DTOs, so **optional
     DTO extraction** was not pursued — closing the P4 row with backlog + changelog
     only. A future source generator (`server-surface-catalog-append-conflict-hotspot`
     option (b)) remains an optional second-order follow-up, not part of this plan. -->
<!-- Generated from ai_docs/prompts/backlog-sweep-plan.md v3 conventions. Companion
     state.json lives alongside this file. The targeted backlog row
     `server-surface-catalog-append-conflict-hotspot` (P4) is **closed in Phase 5**;
     the 2026-04-19 sweep had deferred it as "needs its own dedicated multi-phase sweep" —
     structural split exceeds Rule 3's ≤4 prod-file cap unless phased. -->

## Inventory

**Shipped (Phase 5):** backlog row closed — the structural split plus backlog/changelog
sync. No open rows for this plan.

- P4: 0 open rows (former: `server-surface-catalog-append-conflict-hotspot`)

Blocker / deps: none. The previously-considered source-generator approach
(option (b) in the backlog row) is the second-order structural fix; this plan
ships option (a) (partial-class split) first because the MCP SDK conflict
observed in the 2026-04-21 post-audit-followups WS1 phase 1.2 (`Model
ContextProtocol.Analyzers.XmlToDescriptionGenerator` owns the
`public static partial` method slot on every `[McpServerTool]` method) does NOT
affect partial-class splits — the conflict was at the partial-METHOD level.

Actionable initiatives this sweep: **5**.

## Anchor verification (Step 3 of the planner prompt)

Plan-time source reads, all present as of 2026-04-22T17:00Z:

- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog*.cs` — **Phase 2 (PR #334, 2026-04-22)**
  plus **Phase 3 shipped (PR #336, 2026-04-22),** and **Phase 4 shipped (PR #338, 2026-04-22):** W/S/R/Analysis/Editing
  partials; **phase 4** — `ServerSurfaceCatalog.Resources.cs`, `ServerSurfaceCatalog.Prompts.cs`, and
  `ServerSurfaceCatalog.Orchestration.cs` (tail: former `RemainingInlineTools`). The main
  file holds the `s_allTools` aggregator, workflow hints, factories, and DTOs. The merged
  `Tools` list uses `Lazy<IReadOnlyList<SurfaceEntry>>` across partials; `Resources` and `Prompts` are
  `=>` on slice fields in the new partials.
- `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs` —
  **Phase 1 shipped (PR #330, 2026-04-22):** `AnalyzeCatalogInvocation` no longer
  requires a `Tools` / `Resources` / `Prompts` property ancestor. Kind is inferred
  from the bound factory method name (`Tool` / `Resource` / `Prompt`) on
  `ServerSurfaceCatalog`, so partial-class slice fields and auxiliary properties
  are first-class. Phase 2+ can move initializer lists into domain partials without
  RMCP001/RMCP002 false-positives.
- Tool categories in use (counted via source inspection of line 13–186): 21
  distinct `category` strings. `refactoring`, `analysis`, `advanced-analysis`,
  `symbols`, `workspace`, `validation`, `project-mutation` dominate; the
  remaining 14 categories have 1–6 tools each.

## Sort order

Strictly sequential — each phase depends on the previous one's partial-class
scaffolding. Phase 1 must land first (analyzer extension); phases 2–5 can then
ship in the order below.

## Scope-discipline self-vet

- [x] No initiative closes more than 1 row (Phase 5 closes the single row; 1–4
      are intermediate structural work that reference the same row).
- [x] No initiative touches more than 4 production files (Rule 3). Largest
      phases (2 and 4) touch exactly 4.
- [x] No initiative adds more than 3 test files (Rule 4). Phase 1 adds 1 test;
      all other phases add 0.
- [x] Every initiative has `estimatedContextTokens` ≤ 80K (Rule 5). Max is Phase 2
      at 55K.
- [x] Every initiative has an explicit `toolPolicy`: all are `edit-only`.
- [x] No P2 / P3-correctness items in this plan — single P4 row, which is
      acceptable for a single-row dedicated sweep (Step 1 permits scope
      narrowing when a deferred row needs its own plan).

## Initiatives

---

### 1. `catalog-split-phase1-relax-analyzer` — Extend `ServerSurfaceCatalogAnalyzer` to recognize `Tool()` calls outside the `Tools` property

**Status:** merged (PR #330, 2026-04-22) · **Order:** 1 · **Correctness class:** P4 · **Schedule hint:** — · **Estimated context:** 35000 tokens · **CHANGELOG category:** Changed

| Field | Content |
|---|---|
| Backlog rows closed | (none — sets up Phase 2) |
| Diagnosis | (Shipped in PR #330.) Previously, `AnalyzeCatalogInvocation` required every `Tool(...)` / `Resource(...)` / `Prompt(...)` call to sit under a `PropertyDeclarationSyntax` named `Tools` / `Resources` / `Prompts`, which would have broken partial-class moves into slice fields. |
| Approach | (Shipped in PR #330.) `SurfaceKind` is inferred from the bound factory method name on `ServerSurfaceCatalog` (`Tool` / `Resource` / `Prompt`); `GetSymbolInfo` still scopes the invocation to `ServerSurfaceCatalog` factories. The simple-identifier and non-qualified invocation filters are unchanged. |
| Scope | prod: 1 (`analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs`). tests: 1 new (`tests/RoslynMcp.Tests/SliceFieldDetectionTests.cs` — slice field, unrelated `Tool` type, auxiliary property). `ServerSurfaceCatalogAnalyzerTests` in the same project remains the regression harness for RMCP001/RMCP002. |
| Tool policy | `edit-only` |
| Estimated context cost | 35000 tokens |
| Risks | (a) The semantic `ContainingType` check already scopes to `ServerSurfaceCatalog` — but if `Tool()` is ever defined on a different type (e.g., a sibling helper) the relaxed analyzer would false-match. Current code has only one `Tool()` method in the compilation per the line 350-357 factories; defensive but confirmed safe. (b) Non-catalog partials (e.g., a future `ServerSurfaceCatalog.Internal.cs` for bookkeeping) could harbor `Tool()` invocations that aren't catalog entries — unlikely given the class's tight purpose, but the plan's Phase 5 "Types relocation" is the one place this could surface; keep slice-fields strictly `SurfaceEntry[]`-typed. |
| Validation | `ServerSurfaceCatalogAnalyzerTests` and `SliceFieldDetectionTests` green; `verify-release.ps1` was run for PR #330. |
| Performance review | Analyzer runs at compile time; the change replaces a syntax-walk with a semantic-model lookup that the existing code already performs at line 261 — same-or-cheaper. N/A for runtime. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | **Changed:** `ServerSurfaceCatalogAnalyzer` (RMCP001/RMCP002) now recognizes `Tool()` / `Resource()` / `Prompt()` factory invocations anywhere on the `ServerSurfaceCatalog` class (not only inside the `Tools` / `Resources` / `Prompts` property initializers). Unblocks the partial-class split that lands in follow-up PRs (`server-surface-catalog-append-conflict-hotspot` phase 1). |
| Backlog sync | No row closed yet (phase 1 of 5). `server-surface-catalog-append-conflict-hotspot` stays open; Phase 5 closes it. |

---

### 2. `catalog-split-phase2-extract-three-large-domains` — Introduce `partial` keyword + move Workspace, Symbols, Refactoring tools to domain partials

**Status:** merged (PR #334, 2026-04-22) · **Order:** 2 · **Correctness class:** P4 · **Schedule hint:** — · **Estimated context:** 55000 tokens · **CHANGELOG category:** Changed

| Field | Content |
|---|---|
| Backlog rows closed | (none — sets up Phases 3-5) |
| Diagnosis | `ServerSurfaceCatalog.Tools` currently inlines ~158 `Tool(...)` factory calls on a single property. Per the backlog row's recorded evidence (2026-04-18 sweep wave 1 rebase on PRs #258 + #260; 2026-04-21 WS1 phase 1.2 plan-doc update), every new-tool PR that touches this initializer competes with every other new-tool PR for merge order. A partial-class split where each tool domain lives in its own file means two domain-disjoint new-tool PRs no longer textually conflict. This phase introduces the `partial` keyword and extracts the three largest domains to establish the pattern. |
| Approach | (a) Add `partial` to the class declaration at `ServerSurfaceCatalog.cs:7`. (b) Change the `Tools` initializer (lines 11-187) to a collection-expression concatenation: `public static IReadOnlyList<SurfaceEntry> Tools { get; } = [..WorkspaceTools, ..SymbolTools, ..RefactoringTools, ..RemainingInlineTools];` where `RemainingInlineTools` is a new private field that holds every category NOT yet extracted (analysis/advanced-analysis/validation/editing/file-operations/project-mutation/scaffolding/dead-code/cross-project-refactoring/orchestration/syntax/code-actions/prompts/undo/scripting/configuration/security + the server domain). (c) Create `ServerSurfaceCatalog.Workspace.cs` with `private static readonly SurfaceEntry[] WorkspaceTools = [ /* workspace + server categories */ ]`. (d) Create `ServerSurfaceCatalog.Symbols.cs` with `SymbolTools`. (e) Create `ServerSurfaceCatalog.Refactoring.cs` with `RefactoringTools` (refactoring category + `code-actions` + `undo` because they logically cluster with refactoring and keep the file's line count manageable). (f) Leave every other category inline in the main file for now — Phases 3-4 extract them. Tools order is **preserved** in the concatenation so existing catalog-order-sensitive tests (pagination order, README surface count) keep passing. |
| Scope | prod: 4 (`ServerSurfaceCatalog.cs` edit + 3 new partial files under the same `src/RoslynMcp.Host.Stdio/Catalog/` directory: `ServerSurfaceCatalog.Workspace.cs`, `ServerSurfaceCatalog.Symbols.cs`, `ServerSurfaceCatalog.Refactoring.cs`). tests: 0 new (existing `ReadmeSurfaceCountTests`, `SurfaceCatalogTests`, analyzer tests all re-run unchanged and assert the same surface). |
| Tool policy | `edit-only` |
| Estimated context cost | 55000 tokens |
| Risks | (a) `Tools` ordering: the concatenation order must match the original declaration order so any test or consumer that indexes into `Tools` by ordinal does not break. Source-inspection at plan time found no such consumer; `SurfaceCatalogTests` iterates by name. (b) The Phase 1 analyzer extension MUST be merged before this phase; if the sequencing slips, RMCP001 will false-fire on every moved entry at compile time. Executor's Step-2a wave-picking rules treat the two as dependent. (c) Partial files are parsed independently but share the `Tool()` / `Resource()` / `Prompt()` private-static factory helpers defined on the main file — partial class members are visible across files, so no accessibility change is needed. |
| Validation | `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true` clean (analyzer-run proves no RMCP001/RMCP002 drift). `SurfaceCatalogTests.Tools_AreOrderedByDomainAndCategory` (or whichever order-asserting test exists; source-verify at execution time) green. `ReadmeSurfaceCountTests` green (tool/resource/prompt counts unchanged). `verify-release.ps1` clean. Grep confirms zero duplicate entries (every `Tool("name", ...)` appears exactly once across main + 3 partials). |
| Performance review | N/A — textual refactor, no runtime-path change. Catalog initialization cost is identical (`SurfaceEntry[]` concatenation via collection expression is compiler-materialized at class-load). |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | **Changed:** `ServerSurfaceCatalog` is now a `partial` class with workspace/server, symbol, and refactoring/code-action/undo tool declarations moved to sibling files (`ServerSurfaceCatalog.Workspace.cs`, `ServerSurfaceCatalog.Symbols.cs`, `ServerSurfaceCatalog.Refactoring.cs`). Merge-conflict surface on new-tool PRs that touch disjoint domains drops to zero. Phase 2 of 5 (`server-surface-catalog-append-conflict-hotspot`). |
| Backlog sync | No row closed yet. |

---

### 3. `catalog-split-phase3-extract-analysis-and-editing` — Move Analysis, AdvancedAnalysis, Validation, Editing, FileOperations, DeadCode to partials

**Status:** merged (PR #336, 2026-04-22) · **Order:** 3 · **Correctness class:** P4 · **Schedule hint:** — · **Estimated context:** 45000 tokens · **CHANGELOG category:** Changed

| Field | Content |
|---|---|
| Backlog rows closed | (none — Phase 4 continues) |
| Diagnosis | (Shipped in PR #336.) Phase 2 had left the remaining ~19 categories inline on the main file. The two clusters that dominated by line count were the analysis cluster (`analysis` + `advanced-analysis` + `validation`, ~40 entries) and the editing cluster (`editing` + `file-operations` + `dead-code`, ~15 entries). This phase extracted both clusters in one PR, keeping the file-count budget at 3 while moving the next ~55% of the remaining inline tools. |
| Approach | (a) Create `ServerSurfaceCatalog.Analysis.cs` with `AnalysisTools` containing every `analysis` + `advanced-analysis` + `validation` category entry. (b) Create `ServerSurfaceCatalog.Editing.cs` with `EditingTools` containing every `editing` + `file-operations` + `dead-code` category entry. (c) Edit `ServerSurfaceCatalog.cs` Tools initializer to concatenate the two new slices before the unextracted tail: `[..WorkspaceTools, ..SymbolTools, ..AnalysisTools, ..RefactoringTools, ..EditingTools, ..RemainingInlineTools]`. Order is preserved: the sequence matches the original declaration order within each category. |
| Scope | prod: 3 (`ServerSurfaceCatalog.cs` edit + 2 new partials). tests: 0 new. |
| Tool policy | `edit-only` |
| Estimated context cost | 45000 tokens |
| Risks | Mirrors Phase 2's risks (analyzer dependence, ordering preservation). Small additional risk: the `validation` category includes `test_coverage` and `build_workspace` which are currently in `Tools` under the `validation` category string — confirm during implementation that the category string and the domain-partial membership are consistent (the partial file is named after the domain's dominant category; secondary categories in the same partial do not break anything). |
| Validation | Same as Phase 2. `SurfaceCatalogTests`, `ReadmeSurfaceCountTests`, analyzer green. `verify-release.ps1` clean. |
| Performance review | N/A. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | **Changed:** `ServerSurfaceCatalog` partial split continues — analysis/advanced-analysis/validation and editing/file-operations/dead-code tool declarations move to `ServerSurfaceCatalog.Analysis.cs` and `ServerSurfaceCatalog.Editing.cs`. Phase 3 of 5 (`server-surface-catalog-append-conflict-hotspot`). |
| Backlog sync | No row closed yet. |

---

### 4. `catalog-split-phase4-extract-resources-prompts-and-tail` — Move Resources + Prompts properties and the remaining tail categories to partials

**Status:** merged (PR #338, 2026-04-22) · **Order:** 4 · **Correctness class:** P4 · **Schedule hint:** — · **Estimated context:** 50000 tokens · **CHANGELOG category:** Changed

| Field | Content |
|---|---|
| Backlog rows closed | (none — Phase 5 closes the row) |
| Diagnosis | (Shipped in PR #338.) The former main-file `Resources`, `Prompts`, and tool tail are now in `ServerSurfaceCatalog.Resources.cs`, `ServerSurfaceCatalog.Prompts.cs`, and `ServerSurfaceCatalog.Orchestration.cs`. The main file is the `s_allTools` lazy aggregate plus workflow hints, `GetSummary` / `CreateDocument` / `CreateSummaryDocument` / `PageEntries`, factory helpers, and DTOs — no inline tool/resource/prompt data rows. |
| Approach | (a) Create `ServerSurfaceCatalog.Resources.cs` mirroring the Tools pattern: `private static readonly SurfaceEntry[] ServerResources = [ Resource(...), Resource(...), ... ];` and change the main file's `Resources` property to `[..ServerResources]` (or drop the aggregator if only one slice and use direct assignment). (b) Same for `ServerSurfaceCatalog.Prompts.cs`. (c) Create `ServerSurfaceCatalog.Orchestration.cs` (or similar domain-neutral name) with the remaining tool tail — every category not yet extracted. (d) Update the main file's `Tools` initializer to replace `RemainingInlineTools` with `..OrchestrationTools` (or whatever the final slice name is). |
| Scope | prod: 4 (`ServerSurfaceCatalog.cs` edit + `ServerSurfaceCatalog.Resources.cs` new + `ServerSurfaceCatalog.Prompts.cs` new + `ServerSurfaceCatalog.Orchestration.cs` new). tests: 0 new. |
| Tool policy | `edit-only` |
| Estimated context cost | 50000 tokens |
| Risks | (a) `Resources` parity test: ensure the one-slice concatenation still produces an `IReadOnlyList<SurfaceEntry>` with identical order to the original. (b) The `Prompts` property is decorated nowhere special — it's a plain property initializer. No test should depend on `Prompts` being a property-decl vs a slice-field. (c) After this phase the main file's line count drops from ~448 to ~80-120; ensure no diff-munging tool or doc-auditor asserts minimum line count (none found at plan time). |
| Validation | Same as Phases 2-3. Post-merge surface counts (`ReadmeSurfaceCountTests`) match pre-merge: `Tools: 160 (107 stable / 53 experimental)` or whatever the current value is at execution time. Grep confirms zero duplicate entries. `server_info` resource serves the same JSON shape (integration test coverage via `ExpandedSurfaceIntegrationTests`). |
| Performance review | N/A. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | **Changed:** `ServerSurfaceCatalog` partial split completes structural extraction — Resources, Prompts, and remaining tool tail (project-mutation / scaffolding / orchestration / etc.) now live in sibling files. Main `ServerSurfaceCatalog.cs` is now an aggregator + factory + DTO host. Phase 4 of 5 (`server-surface-catalog-append-conflict-hotspot`). |
| Backlog sync | No row closed yet. |

---

### 5. `catalog-split-phase5-cleanup-and-close` — Optional `SurfaceEntry`/DTO relocation + verify + close backlog row

**Status:** in-review (PR opened from `remediation/catalog-split-phase5-cleanup-and-close`) · **Order:** 5 · **Correctness class:** P4 · **Schedule hint:** — · **Estimated context:** 30000 tokens · **CHANGELOG category:** Maintenance

| Field | Content |
|---|---|
| Backlog rows closed | `server-surface-catalog-append-conflict-hotspot` |
| Diagnosis | The main file still holds the `SurfaceEntry` / `SurfaceSummary` / `ServerCatalogDto` / `ServerCatalogSummaryDto` / `ServerCatalogPagedEntriesDto` / `WorkflowHint` record declarations (lines 371-448 per Phase 1 source-verify). These are fixed-contract DTOs that do not change when tools are added — keeping them in the main file is fine, but relocating to `ServerSurfaceCatalog.Types.cs` or a `Dto/` subfolder further reduces what parallel-PR work can collide on. This phase is **optional** — if the 2026-04-21 merge-conflict analysis (re-run on recent PRs) shows the main file is no longer a conflict hotspot after Phase 4, **skip the relocation** and this phase becomes a close-the-row doc bump. |
| Approach | (a) Run `find_duplicated_methods` and `git log --since=<Phase 2 merge> -- src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` to measure post-Phase-4 touch frequency. (b) If the main file is touched by ≥ 2 PRs per sweep AND DTOs contributed to the conflict: move `SurfaceEntry`, `SurfaceSummary`, `WorkflowHint`, and the `ServerCatalog*Dto` records to `src/RoslynMcp.Host.Stdio/Catalog/SurfaceTypes.cs`. Keep the same namespace. No behavior change. (c) Otherwise: skip code movement. (d) Update `ai_docs/backlog.md` to delete the `server-surface-catalog-append-conflict-hotspot` row (per agent contract: remove closed rows in the same PR as the close). Bump `updated_at`. Add a post-sweep retrospective bullet to the plan's preamble. |
| Scope | prod: 0-2 (`ServerSurfaceCatalog.cs` edit + optional `SurfaceTypes.cs` new). tests: 0 new. docs: `ai_docs/backlog.md` row removal. |
| Tool policy | `edit-only` |
| Estimated context cost | 30000 tokens |
| Risks | Low. If the DTO relocation happens, consumer code that imports `RoslynMcp.Host.Stdio.Catalog.SurfaceEntry` keeps working — same namespace. |
| Validation | `find_duplicated_methods(minLines=12)` in `src/RoslynMcp.Host.Stdio/Catalog/` returns 0 hits (the extraction should have fully decomposed duplicated `Tool(...)` shape). `verify-release.ps1` clean. `ai_docs/backlog.md` no longer lists the row; `updated_at` reflects the close. |
| Performance review | N/A. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | **Maintenance:** `ServerSurfaceCatalog` partial-class split completes. Merge-conflict hotspot closed (`server-surface-catalog-append-conflict-hotspot`) — new-tool PRs in disjoint domains no longer contend on the catalog file. Optional DTO relocation to `SurfaceTypes.cs` kept the main file to < 150 lines. |
| Backlog sync | Close row: `server-surface-catalog-append-conflict-hotspot`. |

---

## Final todo

- [x] `backlog: sync ai_docs/backlog.md` — Phase 5 deletes `server-surface-catalog-append-conflict-hotspot` and bumps `updated_at` in the same PR as the close.

## Suggested kickoff order

Strictly sequential (Phase 1 unblocks 2; 2 unblocks 3 and the ordering of 3 & 4
can swap but 5 must be last). Parallel mode is NOT safe here — every phase
touches the same main file.
