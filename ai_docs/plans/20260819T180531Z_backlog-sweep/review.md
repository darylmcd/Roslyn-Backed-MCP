# Plan review — 2026-08-19 (cycle 2)

**Plan reviewed:** ai_docs/plans/20260819T180531Z_backlog-sweep
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 2
**Outcome:** passed-with-warnings
**Initiative count:** 16 pending
**Findings:** block: 0, warn: 4, info: 12
**Anchor verification:** partial (scoped re-review — 1.01/1.02 anchors re-verified live; global checks carried forward under `skipGlobalChecks`)

## Summary

Both cycle-1 Rule 5b blocks are **resolved**: `resource-read-protocol-error-semantics-workspace` now records `fanoutEstimate: 4` and `-server-catalog` records `fanoutEstimate: 2`, matching the counted `grep -rn ExecuteResource` probes their Risks cells already stated, and matching `productionFilesTouched` on both (4 and 2). Remediation was field-only: both stanzas' production/test file sets are byte-identical to the cycle-1 review's, so the conflict graph, hotspot table, and stale-row check carry forward unchanged per `skipGlobalChecks`.

Re-checked both children against Rules 1/3/3b/4/5/5b from source: 1.01 is 4 production files at the base Rule 3 cap with no exemption claimed, 3 modified test files exactly at the Rule 4 cap, 55K vs the 80K Rule 5 cap; 1.02 is 2/2/40K. Load-bearing anchors re-verified live — `ToolErrorHandler.ExecuteResource` (:141) and `ExecuteResourceAsync` (:175) still return `InjectMetaIfPossible(FormatErrorResponse(...))` as the resource body, `ResourceReadResultFilter.Create` still has no `try`/`catch` around `next(...)`, `RequestProtocolFeatureGate.SupportsJuly2026Features` exists with the documented "SDK helper is internal" comment, and `ServerResources.cs` still has exactly 3 `ToolErrorHandler.ExecuteResource` call sites. The backlog row `resource-read-protocol-error-semantics` is present (backlog.md:59).

Deduplicated block count fell 2 -> 0; no oscillation signal.

**Post-review driver action:** the two `state-contract` warns below were closed before finalize — both split children now carry `parentRow: resource-read-protocol-error-semantics`. This matters beyond traceability: `prepare-args` resolves a pre-split child's backlog row text/size/detail through `parentRow` and fails closed when it is unresolved.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| resource-read-protocol-error-semantics-workspace | warn (CLOSED) | state-contract | Split child with synthetic id and `backlogRowsClosed: []` carried no `parentRow`. Set to `resource-read-protocol-error-semantics`. |
| resource-read-protocol-error-semantics-server-catalog | warn (CLOSED) | state-contract | Same. Set to `resource-read-protocol-error-semantics`. |
| resource-read-protocol-error-semantics-workspace | warn | C2-wave-conflict | Adjacent-order 1.01/1.02 share `ToolErrorHandler.cs`; mitigated by `dependsOn: [1.01]` on 1.02. |
| prompt-call-error-filter-boundary | warn | C2-wave-conflict | Adjacent-order 3/4 both edit `IUnexpectedExceptionReporter.cs`. |
| resource-read-protocol-error-semantics-workspace | info | anchor-stale | `McpProtocolVersions.UseInvalidParamsForMissingResource` unresolvable; SDK helper is internal. Stanza self-declares and substitutes a local equivalent. |
| resource-read-protocol-error-semantics-workspace | info | C2-wave-conflict | Degree 2 (peers 1.02, 7) on `ToolErrorHandler.cs`; expect serial scheduling. |
| resource-read-protocol-error-semantics-server-catalog | info | C2-wave-conflict | Degree 2 (peers 1.01, 7); `dependsOn: [1.01]` forces a later generation. |
| semantic-grep-pattern-error-detail-redaction | info | C2-wave-conflict | Degree 2 after the split (peers 1.01, 1.02). |
| prompt-call-error-filter-boundary | info | C2-wave-conflict | Degree 2 (peers 4, 6). |
| analysis-flow-error-detail-redaction | info | C2-wave-conflict | Degree 2 (peers 3, 6). |
| test-runner-timeout-error-detail-redaction | info | C2-wave-conflict | Degree 2 (peers 3, 4). |
| prompt-call-error-filter-boundary | info | 5b | 15 `CreateErrorMessage` sites vs `fanoutEstimate: 4`; signature-preserved rationale stated and checkable. |
| preview-apply-token-write-path-toctou | info | 5b | 16 `ApplyByTokenAsync` callers vs `fanoutEstimate: 4`; default-interface-method rationale stated. |
| analysis-flow-error-detail-redaction | info | anchor-stale | `FlowAnalysisTests.cs` absent; re-targeted to `FlowAnalysisServiceTests.cs`. |
| test-runner-timeout-error-detail-redaction | info | anchor-stale | `TestRunnerTimeoutTests.cs` absent; re-targeted to `TestRunFailureEnvelopeTests.cs`. |
| semantic-grep-pattern-error-detail-redaction | info | anchor-stale | `SemanticGrepIntegrationTests.cs` absent; re-targeted to `SemanticGrepServiceTests.cs`. |
| ci-merge-publish-runner-os-parity | info | acceptance-non-diff | Branch-protection requirement cannot land in the PR diff; must be recorded post-merge. |

## Conflict graph

Reviewer-computed; agreement with orchestrator: **yes** (carried forward from cycle 1 under `skipGlobalChecks: true`; both scoped initiatives' production file sets unchanged).

Edges: 1.01-1.02, 1.01-7, 1.02-7 on `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`; 3-4, 3-6, 4-6 on `src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs`.
Zero-degree: 2, 5, 8, 9, 10, 11, 12, 13, 14, 15.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | 10 | no (single toucher) |
| `ServerSurfaceCatalog.cs` partials | none | n/a |
| `ServiceCollectionExtensions.cs` | none | n/a |
| `ParameterObjectService.cs` | none | n/a |

## Recommended next step

`passed-with-warnings` — proceed to `/backlog-sweep:execute`. The two remaining conflict warns are already serialized in practice by `dependsOn` (1.01 -> 1.02) and by the shared-file edges the executor's wave batcher reads.
