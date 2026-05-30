# Plan review — 2026-05-30T23:37:00Z (cycle 0)

**Plan reviewed:** ai_docs/plans/20260530T233522Z_backlog-sweep
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 2 pending
**Findings:** block: 0, warn: 1, info: 1
**Anchor verification:** performed

## Summary

Both initiatives are sound, honestly scoped, and well within Rules 1–5 + 5b. Every cited
anchor in Initiative 1 resolves precisely (TypeConsumersService:108/47, CodePatternAnalyzer:34
+ 229 with three in-file callers at 154/166/192, SymbolSearchService:117/57); all three
rewritten helpers are `private static`, so the signature changes stay file-local and the
`fanoutEstimate: 3` is verified honest via find_references (exactly 3 in-file callers on the
one static helper probed). Both initiatives are correctly `edit-only` for the main checkout
(self-edit caveat). The independent conflict-graph recompute matches the orchestrator's
exactly: zero shared production files, both initiatives zero-degree. The one warn is a
Scope under-count: Initiative 1's constructor changes force an edit to the addenda-listed
mandatory test-fixture DI file `TestServiceContainer.cs` (constructs all three services at
lines 141/146/193), which the Scope omits — still within Rule 4 but the executor should
expect it. The one info: Initiative 2's README-repoint step is largely a no-op because the
README contains no reference to the file being deleted; the plan honestly flagged the README
was unread at plan time.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| compilation-cache-adoption-read-side | warn | 4 | Scope declared testFilesAdded=0 and named only CompilationCacheAdoptionTests.cs, but the three services are constructed in the addenda-listed mandatory test-fixture file tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs (lines 141/146/193). Adding ICompilationCache to the constructors forces an edit there or fixtures fail to construct. Within Rule 4 (2 test files ≤ 3) but Scope under-counted the test surface. **Resolved post-review:** stanza Scope + state.json testFilesAdded updated to include TestServiceContainer.cs. |
| promotion-scorecard-execution-batch | info | anchor-stale | ai_docs/audit-reports/README.md contains no reference to _latest-promotion-scorecard.json, so Approach step 2 ("remove any reference") is a no-op and the README edit is discretionary; productionFilesTouched=2 likely overcounts (only the JSON delete is certain). Within budget. Plan's Risks #2 honestly flagged README was unread at plan time. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes)

```json
{
  "edges": [],
  "degrees": { "1": 0, "2": 0 },
  "zeroDegreeInitiatives": [1, 2]
}
```

Initiative 1 files: TypeConsumersService.cs, CodePatternAnalyzer.cs, SymbolSearchService.cs
(+ test CompilationCacheAdoptionTests.cs, TestServiceContainer.cs). Initiative 2 files:
ai_docs/audit-reports/README.md, ai_docs/audit-reports/_latest-promotion-scorecard.json (deleted).
Intersection empty.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs | none (Initiative 2 explicitly excludes; tier-promotion follow-on deferred) | n/a |
| src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs | none | n/a |
| src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs | none | n/a |

No hotspot file is touched by either scoped initiative; no adjacency conflict.

## Stale-row spot check

| Row id | Present? |
|---|---|
| compilation-cache-adoption-read-side | yes (backlog.md:55) |
| promotion-scorecard-execution-batch | yes (backlog.md:53) |

## Recommended next step

Proceed to Phase F (handoff-readiness) then /backlog-sweep:execute. Surface the two findings
in the run summary: Initiative 1's executor must also edit TestServiceContainer.cs (test-fixture
DI — now folded into Scope); Initiative 2's README repoint is discretionary, not load-bearing.
