# Plan review — 2026-04-28T12:50:00Z

**Plan reviewed:** `ai_docs/plans/20260428T124405Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:review
**Outcome:** **passed**
**Initiative count:** 7
**Findings:** block: 0, warn: 0, info: 1
**Anchor verification:** performed (spot-check of all 7 initiatives' Diagnosis citations against the live tree)

## Summary

Plan vets cleanly. All 7 initiatives are single-row (no Rule 1 bundling concerns), each within Rule 3 file-count caps (max 4 production files; initiative #5 is exactly at the cap; one initiative invokes the addenda's tool-surface-only exemption with the cap and citation correctly recorded), each within Rule 4 test-count cap (all at 1 test file), each well under the Rule 5 80K context budget (max 55K on the perf initiative). Tool policy is explicit `edit-only` on every initiative — no nulls, no preview-then-apply work in this batch. Hotspot adjacency is correctly distributed: hotspot-touching initiatives at orders 2/4/6 alternate with non-hotspot initiatives at 1/3/5/7, so a parallel-mode wave will not force serial rebase.

The one info-level finding is upstream of the plan: backlog row `mcp-error-category-workspace-evicted-on-host-recycle` cites two anchor paths that are stale relative to the live tree. The plan's Diagnosis section already corrects the citations, so the executor has the right paths — but the underlying backlog row should be cleaned up by an anchor-audit pass.

## Findings

| Initiative | Severity | Rule / Topic | Evidence |
|---|---|---|---|
| `mcp-error-category-workspace-evicted-on-host-recycle` | info | anchor-drift (upstream) | Backlog row cites `Tools/StructuredCallToolFilter.cs` (actual: `Middleware/StructuredCallToolFilter.cs`) and `Tools/HeartbeatTools.cs` (actual: `Tools/ServerTools.cs` — `server_heartbeat` is a method, not a separate file). Plan's Diagnosis already corrected. Recommend running `backlog-anchor-auditor` agent on the backlog as a separate, low-priority cleanup. Not blocking — executor will use the corrected paths from the plan. |

## Per-rule walk

### Rule 1 — Bundling discipline

All 7 initiatives have `rowsClosedCount == 1`. No bundling. Trivially passes.

### Rule 3 — File-count budget

| Initiative | productionFilesTouched | Cap | Verdict |
|---|---|---|---|
| `change-signature-preview-metadataname-shape-error-actionability` | 2 | 4 | pass |
| `mcp-error-category-workspace-evicted-on-host-recycle` | 3 | 4 | pass |
| `symbol-info-not-found-message-locator-vs-location` | 2 | 4 | pass |
| `apply-composite-preview-name-friction` | 2 | **2** (tool-surface-only exemption — addenda) | pass; exemption cited in Scope |
| `dead-logger-fields-roslyn-services-batch-1` | 4 | 4 | pass (at cap) |
| `di-graph-triple-registration-cleanup` | 3 | 4 | pass |
| `coupling-metrics-p50-borderline-on-15s-solution-budget` | 2 | 4 | pass |

No mandatory-addenda under-counting concerns: none of the initiatives are new-MCP-tool initiatives (the only shape that triggers the test-fixture / README addenda counting rule).

### Rule 3b — Tool policy

| Initiative | toolPolicy | Approach shape | Verdict |
|---|---|---|---|
| change-signature | edit-only | Resolver pre-check + parameter `[Description]` text | matches |
| workspace-evicted | edit-only | New exception type + catch arm + small set field | matches |
| symbol-info-locator | edit-only | Error-message branch | matches |
| apply-composite | edit-only | Catalog summary + `[Description]` text | matches |
| dead-logger-batch-1 | edit-only | Field/parameter deletion across 4 files | matches |
| di-graph-cleanup | edit-only | Deletion of duplicate `services.Add*` calls | matches |
| coupling-metrics | edit-only | Localized scoping change in one method, optional `Parallel.ForEachAsync` | matches (local mutation, not solution-wide refactor) |

No `null` policies. No solution-wide rename/extract/bulk-replace shapes hidden behind `edit-only`. The PreToolUse hook on `mcp__roslyn__*_apply` is non-blocking for this batch because no initiative uses `preview-then-apply`.

### Rule 4 — Test budget

All 7 initiatives at `testFilesAdded == 1`. Cap is 3. Trivially passes.

### Rule 5 — Context budget

| Initiative | estimatedContextTokens | Shape | Verdict |
|---|---|---|---|
| change-signature | 28000 | Fix/refactor 1–2 files | reasonable (band: 25–40K) |
| workspace-evicted | 38000 | Fix/refactor 3 files + new exception | reasonable (band: 25–40K, slightly above mid) |
| symbol-info-locator | 25000 | Fix/refactor 1–2 files | reasonable (band: 25–40K, low end) |
| apply-composite | 18000 | Tool-surface-only | reasonable (band: 20–30K, below — small text edits) |
| dead-logger-batch-1 | 28000 | Fix/refactor 4 files (mechanical) | reasonable (band: 40–60K low end is generous; mechanical drop is genuinely cheap) |
| di-graph-cleanup | 32000 | Fix/refactor 3 files | reasonable (band: 25–40K) |
| coupling-metrics | 55000 | Fix/refactor 2 files + profile + perf-test | high but within 80K cap; profiling step legitimately costs context |

Highest at 55K — comfortably under the 80K ceiling. No splits required.

### Anchor freshness

Spot-checked all 7 initiatives' Diagnosis citations during this review:

- `ChangeSignatureTools.cs`, `ChangeSignatureService.cs`, `SymbolLocator.cs` — exist ✓
- `WorkspaceManager.cs`, `ToolErrorHandler.cs`, `Middleware/StructuredCallToolFilter.cs`, `Diagnostics/HostProcessMetadataStore.cs`, `Tools/ServerTools.cs` — exist ✓
- `SymbolTools.cs`, `SymbolNavigationService.cs` — exist ✓
- `ServerSurfaceCatalog.Orchestration.cs:31`, `OrchestrationTools.cs` — exist ✓; line 31 currently reads *"Apply a previously previewed orchestration operation."* (the text the plan proposes to replace)
- `BulkRefactoringService.cs:16`, `CodeMetricsService.cs:14`, `CompletionService.cs:13`, `ConsumerAnalysisService.cs:14` — exist at the cited lines ✓
- `RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`, `RoslynMcp.Host.Stdio/Program.cs` — exist ✓
- `CodeMetricsService.cs`, `AdvancedAnalysisTools.cs` — exist ✓

All 19 anchor citations resolve. The plan's `notes` for initiative #2 already records the two upstream backlog drifts; nothing new for the executor to discover.

### Hotspot scheduling (parallel-mode pre-flight)

| Order | Initiative | Hotspot | Adjacent? |
|---|---|---|---|
| 1 | change-signature | none | — |
| 2 | workspace-evicted | `WorkspaceManager.cs` | non-adjacent to other hotspots |
| 3 | symbol-info-locator | none | — |
| 4 | apply-composite | `ServerSurfaceCatalog.Orchestration.cs` | non-adjacent to other hotspots |
| 5 | dead-logger-batch-1 | none | — |
| 6 | di-graph-cleanup | `ServiceCollectionExtensions.cs` | non-adjacent to other hotspots |
| 7 | coupling-metrics | none | — |

Alternation is correct. A parallel wave of `(1, 3, 5, 7)` is safe (zero hotspot overlap). A parallel wave of `(2, 4, 6)` would force serial rebase per the addenda's "≤1 hotspot per wave" rule — **not recommended for parallel mode**; either run those serially or interleave with non-hotspot initiatives.

## Recommended next step

**`passed`** — proceed with `/backlog-sweep:execute` whenever ready.

Suggested first batch:
- **Serial first run** (recommended for proving the new global commands): `/backlog-sweep:execute count=1` ships initiative #1 (change-signature, P2, 28K, no hotspot).
- **Or parallel wave 1**: `/backlog-sweep:execute mode=parallel count=2` ships initiatives #1 and #3 in parallel (both edit-only, both no-hotspot, file sets disjoint).
