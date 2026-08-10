# Plan review — 2026-08-10 (cycle 2)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260810T175048Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 2
**Outcome:** passed-with-warnings
**Initiative count:** 8 pending (orders 1, 2, 3, 4, 5, 6, 8, 9)
**Findings:** block: 0, warn: 6, info: 6
**Anchor verification:** partial (order 2 re-verified live at HEAD this cycle; orders 1/3 carried from cycle 1)
**Scoped re-review:** `scopeInitiativeIds: [file-snapshot-capture-helper-consolidation]`. Global checks were NOT skipped — order 2's production file set changed (2 files → 1, `RefactoringService.cs` dropped), which triggers the mandatory conflict-graph/hotspot rebuild per the skip-gate exception. All non-scoped findings are carried forward verbatim from cycle 1, with one addition on order 3 (order 2's conflict-graph neighbor, explicitly in scope per the launcher's course correction).

## Summary

The cycle-1 **block is resolved.** Order 2 (`file-snapshot-capture-helper-consolidation`) is back on the orchestrator's pre-split charter: 1 production file (`FileSnapshotCapture.cs`), `rowsClosed: []`, and the parent row left OPEN for order 3 to close. The two defects that made cycle 1 a block — identical production-file sets and two pending initiatives closing the same row (`ai_docs/backlog.md:60`) — are both gone. Every anchor order 2 cites was re-verified live against HEAD and is exact: `CaptureAsync` at `FileSnapshotCapture.cs:60-72` with the inline ternary at `:65-71`, the sibling `FromBytesOrFallback` at `:43-46`, the overclaiming class doc at `:5-19` with the caller list at `:11-13` omitting `RefactoringService`, `RefactoringService.AddFileSnapshotAsync` at `:1057-1089` with the last hand-rolled `FromExistingBytes` at `:1072` and the genuinely-unreachable `if (document is not null)` at `:1084`, all 8 existing test methods at exactly the cited lines (87/105/117/129 for the four `CaptureAsync` branches, 44-84 for `FromBytesOrFallback`), and both named regression tests verbatim. The `eng/verify-changelog-fragments.ps1` claim is verified byte-exact at script lines 27-29 — a fragment-less PR does print `changelog.d/ — no fragments (ok)` and exits 0, so the zero-rows-closed/zero-fragment shape is shippable. Fanout is confirmed real: exactly 2 production callers of `CaptureAsync` (`EditService.cs:84`, `:145`) and 4 test callers, signature unchanged, so `fanoutEstimate: 1` is honest.

What remains are two ordering-hygiene warnings, both on the order-2/order-3 pair. First, the remediation restored the charter but did NOT update order 3's stanza, whose Approach step (1) still instructs the executor to rewrite `CaptureAsync`'s delegation and edit the same class doc — order 2's entire delta. Under the intended sequence that is a benign idempotent no-op (order 3's real deltas remain `RefactoringService` + the doc flip to unconditional single-ownership), but the duplication is now inverted rather than eliminated for the third consecutive cycle. Second, the landing direction is asserted rather than encoded: order 2's Risks(4) relies on the shared-file conflict edge to run order 2 before order 3, but a conflict edge only guarantees *different waves*, not *which wave first*. If order 3 landed first, order 2's doc edit (b) would actively regress the doc back to "one remaining hand-rolled copy tracked by row `file-snapshot-capture-helper-consolidation`" — a false claim naming a row order 3 just closed. One `dependsOn` field on order 3 makes that machine-readable; this is the same failure class cycle 1 already warned about on the order-4/order-5 pair.

Conflict graph agrees with the orchestrator exactly. No hotspot-touching initiative in the plan (no `ServerSurfaceCatalog*`, `ServiceCollectionExtensions.cs`, or `WorkspaceManager.cs` in any Scope), so no hotspot-adjacency finding. All six referenced backlog rows still exist. Block count 1 → 0: no oscillation.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| file-snapshot-capture-helper-consolidation | warn | dependsOn-missing | Risks(4) asserts "no `dependsOn` is needed … enforced by the shared-file edge", but a conflict edge only guarantees order 2 and 3 land in DIFFERENT waves, not that 2 lands first. If 3 lands first, order 2's doc edit (b) regresses `FileSnapshotCapture.cs:5-19` back to "one remaining hand-rolled copy tracked by row `file-snapshot-capture-helper-consolidation`" — false, and naming a row order 3 just closed. Set `dependsOn: ["file-snapshot-capture-helper-consolidation"]` on order 3 (same class as cycle 1's order-4/5 finding). |
| file-snapshot-capture-callsite-migration | warn | scope-accuracy | Order 3's stanza was not updated alongside the cycle-2 remediation: Approach step (1) still instructs "rewrite `CaptureAsync` to delegate to `FromBytesOrFallback`" + the class-doc caller-list edit, i.e. order 2's ENTIRE delta, and its CHANGELOG draft re-claims "fixed `FileSnapshotCapture.CaptureAsync` itself to delegate". Benign under the intended sequence (idempotent no-op; order 3's genuine deltas are `RefactoringService.AddFileSnapshotAsync` + the doc flip to unconditional single-ownership) but the overlap is now inverted, not removed — third consecutive cycle. Trim order 3's Approach (1) to the doc flip + `see cref` addition. |
| file-snapshot-capture-callsite-migration | warn | C2-wave-conflict | Adjacent-order initiatives 2 and 3 share src/RoslynMcp.Roslyn/Services/FileSnapshotCapture.cs (and the same method + same doc comment); planner Step 6 should have separated them — see the block finding on order 2 for the underlying duplication. |
| file-snapshot-capture-callsite-migration | warn | scope-accuracy | Closes parent row file-snapshot-capture-helper-consolidation as 'the last remaining hand-rolled ternary', but a 6th hand-rolled site survives at src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1105 (AddProjectFileSnapshotAsync builds FileSnapshotDto.FromExistingBytes inline) — outside the row's '5 call sites' survey and outside this Scope; the stanza's own grep-validation ('RefactoringService.cs no longer constructs FromExistingBytes directly') would fail as written. |
| compilation-cache-adoption-read-side | warn | dependsOn-missing | Order 5 closes parent row compilation-cache-adoption-read-side, while order 4 (a child of the SAME row) declares that row stays open and must amend ai_docs/items/compilation-cache-adoption-read-side.md; closing the row deletes that item file (backlog agent contract). Both are zero-degree with dependsOn: [], so a parallel wave can land 5 first and orphan 4's backlog sync. Set dependsOn: ["compilation-cache-cancellation-coverage-and-entry-guard"] on order 5, or move the row close to the later-landing initiative. |
| compilation-cache-adoption-read-side | warn | C2-wave-conflict | Adjacent-order initiatives 4 and 5 both extend tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs; the production-file-only conflict graph reports both zero-degree/parallelizable, so a parallel wave forces a rebase on the shared test file. |
| promotion-tier-scorecard-refresh | info | scope-accuracy | Initiative id still reads '-scorecard-refresh' but the remediated stanza ships no refresh — it delivers the durability prerequisite and explicitly leaves promotion-tier-execution-batch acceptance bullet 1 open; a reader of the generated status table will mis-infer that the v1.38.1 -> v2.3.x refresh shipped. Consider renaming. |
| promotion-tier-scorecard-refresh | info | scope-accuracy | The '<audited-repo-root> = primary checkout' rule lands only in skills/mcp-server-surface-test/prompts/phases/output-and-close.md; a second shipped-prompt site names both artifact write targets with no root qualifier at skills/mcp-server-surface-test/prompts/phases/setup-and-analysis.md:69. Adding it would be a 4th production file, still inside Rule 3's cap. Info-only because output-and-close.md owns the canonical 'where to save' instructions, so the evidence-loss channel does close. |
| promotion-tier-scorecard-refresh | info | anchor-stale | Self-reported stale anchor confirmed: skills/promote-tier/ does not exist; the maintainer skill lives at .claude/skills/promote-tier/ (cited by ai_docs/items/promotion-tier-execution-batch.md:9). Now explicitly disclosed in the stanza's Risks(6) and out of edit scope — carried for the next intake pass only. |
| compilation-cache-cancellation-coverage-and-entry-guard | info | 3 | Budget headroom (2/4 production files, 1/3 test files) exists to also guard the verified-identical defect at src/RoslynMcp.Roslyn/Services/CompilationCache.cs:117 (GetCompilationWithAnalyzersAsync starts the shared build before checking the caller's ct); scoping to GetCompilationAsync only leaves a known twin defect needing a follow-on for one if-statement. |
| hoststdio-middleware-tools-namespace-cycle | info | anchor-stale | Diagnosis claims the cycle has 'exactly two using-directive edges'; there is a third — src/RoslynMcp.Host.Stdio/Middleware/StructuredCallContentProjector.cs also carries 'using RoslynMcp.Host.Stdio.Tools;'. Non-load-bearing: breaking the Tools->Middleware direction still kills the cycle, and SymbolTools.cs references no Middleware type other than StructuredCallToolFilter (verified), so the using removal is clean. |
| shipped-skills-hardcode-bare-roslyn-tool-prefix | info | 5b | fanoutEstimate is null while the same bare-prefix pattern spans ~20 shipped skill files plus eng/verify-skills-are-generic.ps1; not under-scoped-and-closing (rowsClosed: [], parent row stays open by explicit orchestrator narrowing), but the known fanout should be recorded for the deferred follow-on. |

### Resolved this cycle

| Initiative | Prior severity | Rule | Resolution |
|---|---|---|---|
| file-snapshot-capture-helper-consolidation | block | 1 | RESOLVED. `productionFiles` is now `["src/RoslynMcp.Roslyn/Services/FileSnapshotCapture.cs"]` (was identical to order 3's 2-file set); `rowsClosed: []` (was `["file-snapshot-capture-helper-consolidation"]`), so only order 3 closes the row and no close targets a deleted row. Charter items (a) `CaptureAsync` delegation and (b) doc truthing only; `RefactoringService.cs` explicitly out of scope. Verified against HEAD, not taken on the stanza's word. |

## Per-rule verdict — order 2 (scoped)

| Rule | Value | Verdict |
|---|---|---|
| 1 | `rowsClosed: []` (count 0) | pass — no bundling claim to cite |
| 3 | `productionFilesTouched: 1` | pass (1 of 4, no exemption claimed or needed; no mandatory addenda file applies — not a new-tool shape) |
| 3b | `edit-only` | pass — one-file textual edit; no solution-wide symbolic work; addenda `*_apply` PreToolUse hook not engaged |
| 4 | `testFilesAdded: 0` | pass — Rule 4 is a cap, not a floor; all 4 `CaptureAsync` branches verified pinned at `FileSnapshotCaptureTests.cs:87/105/117/129`, plus null-vs-empty discrimination at `:44-84` |
| 5 | `estimatedContextTokens: 20000` | pass — below the fix/refactor band floor (~25K) but honest for a 74-line file with zero test authoring; not "suspiciously low" in the Rule 5 sense |
| 5b | `fanoutEstimate: 1`, `fanoutOversize: false` | pass — verified: 2 production callers (`EditService.cs:84`, `:145`), 4 test callers, signature unchanged |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: **yes** — exact match on edges, degrees, and zero-degree set)

```json
{
  "edges": [ { "a": 2, "b": 3, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/FileSnapshotCapture.cs"] } ],
  "degrees": { "1": 0, "2": 1, "3": 1, "4": 0, "5": 0, "6": 0, "8": 0, "9": 0 },
  "zeroDegreeInitiatives": [1, 4, 5, 6, 8, 9]
}
```

Rebuilt from the deepened Scope file lists (mandatory this cycle — order 2's file set changed). Near-miss checked and correctly excluded: orders 6 and 9 both live under `skills/mcp-server-surface-test/` but touch disjoint files (`prompts/phases/output-and-close.md` vs `SKILL.md`) — no edge. No `changelog.d/*`, `CHANGELOG.md`, or `ai_docs/backlog.md` entries appear in any Scope, so no virtually-shared exclusion was needed. Max degree is 1, so no conflict-degree ≥ 2 info fires. Order 7 is absent from both the deepened plan and the orchestrator graph — consistent, not a gap.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none | n/a |

No initiative in this plan touches an addenda-listed hotspot (order 8 touches `Host.Stdio/Middleware`, `Tools`, and the new `Elicitation` folder — not the Catalog partials; order 6 explicitly states no `src/**` touch). No `hotspot-adjacency` findings.

## Stale-row spot check

| Row id | Present? |
|---|---|
| `adr-0001-location-migration-corrections` | yes (`ai_docs/backlog.md:65`) |
| `file-snapshot-capture-helper-consolidation` | yes (`:60`) |
| `compilation-cache-adoption-read-side` | yes (`:58`) |
| `promotion-tier-execution-batch` | yes (`:55`) |
| `hoststdio-middleware-tools-namespace-cycle` | yes (`:76`) |
| `shipped-skills-hardcode-bare-roslyn-tool-prefix` | yes (`:81`) |

No `stale-row` findings.

## Recommended next step

Proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the 6 warnings in the run summary. Zero blocks — Phase E remediation is not required. Two cheap, non-blocking hygiene edits worth folding in before execute, both on the order-2/order-3 pair:

1. Add `dependsOn: ["file-snapshot-capture-helper-consolidation"]` to order 3 so the landing direction is machine-readable instead of inferred from the conflict edge (and consider the analogous `dependsOn` on order 5 for the order-4/5 row-close hazard).
2. Trim order 3's Approach step (1) down to the doc flip + `see cref` addition, since order 2 now owns the `CaptureAsync` delegation.

If they are not applied, execute in strictly serial order for orders 2→3 and 4→5, and expect order 3's PR diff to be doc-only in `FileSnapshotCapture.cs`.