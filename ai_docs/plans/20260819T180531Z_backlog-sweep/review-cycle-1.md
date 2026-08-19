# Plan review — 2026-08-19 (cycle 1)

**Plan reviewed:** C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260819T180531Z_backlog-sweep
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 1
**Outcome:** failed
**Initiative count:** 16 pending
**Findings:** block: 2, warn: 2, info: 12
**Anchor verification:** performed (first 3 initiatives, plus re-verification of the cycle-0 block evidence)

## Summary

The cycle-0 Rule 4 block is **resolved**. The split of `resource-read-protocol-error-semantics` into `-workspace` (1.01) and `-server-catalog` (1.02) correctly partitions the five compile/assert-forced test files: part 1 takes `ErrorResponseObservabilityTests` (resource tests re-verified live at lines 134-160), `Top10V2RegressionTests` (three `SourceFileLines` resource tests at 88-140), and `FetchMcpResourceReadinessTests:131-144` — exactly 3, at the Rule 4 cap; part 2 takes `SurfaceCatalogTests` (the direct `ServerResources.GetServerCatalogVersionDiff` body-envelope assertion re-verified at 218-230) plus the new wire-matrix file — 2. I independently checked the two files the plan claims need no edit: `WorkspaceToolsIntegrationTests.cs:321/340` and the remaining `ErrorResponseObservabilityTests` error assertions are **tool**-path calls (`WorkspaceTools.GetSourceText`, `find_references`, `symbol_info`, `ClassifyAndFormat`), not resource calls, so the claim holds and no 4th/5th test file is forced. Rule 3 (4 / 2), Rule 4 (3 / 2), Rule 5 (55K / 40K vs the 80K cap) all pass on both children.

However, remediation introduced **two new Rule 5b blocks**: both children set `fanoutEstimate: null` while their Risks cells record an explicit, counted `grep -rn ExecuteResource --include=*.cs` probe result (4 files for 1.01, 2 for 1.02). Rule 5b's consumer-side check makes that a block by construction — the fix is one field per stanza (record 4 and 2), not a re-design. The conflict graph was rebuilt from scratch after the split and matches the orchestrator's exactly, including the three new `ToolErrorHandler.cs` edges (1.01–1.02, 1.01–7, 1.02–7); the split raised initiative 7's degree from 1 to 2 and created one new adjacent-order warn (1.01/1.02), which `dependsOn: [1.01]` on 1.02 already serializes in practice. No hotspot adjacency (only initiative 10 touches an addenda-listed hotspot, `WorkspaceManager.cs`). All 14 `backlogRowsClosed` ids still exist in `ai_docs/backlog.md` — no stale rows.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| resource-read-protocol-error-semantics-workspace | block | 5b | NEW since cycle 0. `fanoutEstimate: null` but Risks (g) records a counted grep probe ("exactly 3 production files … = 4"). Record `fanoutEstimate: 4`. |
| resource-read-protocol-error-semantics-server-catalog | block | 5b | NEW since cycle 0. `fanoutEstimate: null` but Risks (e) records a counted grep probe ("… only production files left after part 1 — 2"). Record `fanoutEstimate: 2`. |
| resource-read-protocol-error-semantics-workspace | warn | C2-wave-conflict | Adjacent-order 1.01/1.02 share `ToolErrorHandler.cs`; mitigated by `dependsOn`. |
| prompt-call-error-filter-boundary | warn | C2-wave-conflict | Adjacent-order 3/4 both edit `IUnexpectedExceptionReporter.cs`. |
| resource-read-protocol-error-semantics-workspace | info | anchor-stale | `McpProtocolVersions.UseInvalidParamsForMissingResource` still unresolvable; SDK helper is internal (re-verified at `RequestProtocolFeatureGate.cs:19-21`). Self-declared. |
| resource-read-protocol-error-semantics-workspace | info | C2-wave-conflict | Degree 2 (peers 1.02, 7). |
| resource-read-protocol-error-semantics-server-catalog | info | C2-wave-conflict | Degree 2 (peers 1.01, 7). |
| semantic-grep-pattern-error-detail-redaction | info | C2-wave-conflict | Degree 2 after the split (peers 1.01, 1.02) — was degree 1 in cycle 0. |
| prompt-call-error-filter-boundary | info | C2-wave-conflict | Degree 2 (peers 4, 6). |
| analysis-flow-error-detail-redaction | info | C2-wave-conflict | Degree 2 (peers 3, 6). |
| test-runner-timeout-error-detail-redaction | info | C2-wave-conflict | Degree 2 (peers 3, 4). |
| prompt-call-error-filter-boundary | info | 5b | 15 `CreateErrorMessage` sites vs `fanoutEstimate: 4`; signature-preserved rationale stated. |
| preview-apply-token-write-path-toctou | info | 5b | 16 `ApplyByTokenAsync` callers vs `fanoutEstimate: 4`; default-interface-method rationale stated. |
| analysis-flow-error-detail-redaction | info | anchor-stale | `FlowAnalysisTests.cs` absent; re-targeted to `FlowAnalysisServiceTests.cs`. |
| test-runner-timeout-error-detail-redaction | info | anchor-stale | `TestRunnerTimeoutTests.cs` absent; re-targeted to `TestRunFailureEnvelopeTests.cs`. |
| semantic-grep-pattern-error-detail-redaction | info | anchor-stale | `SemanticGrepIntegrationTests.cs` absent; re-targeted to `SemanticGrepServiceTests.cs`. |
| ci-merge-publish-runner-os-parity | info | acceptance-non-diff | Branch-protection requirement cannot land in the diff; must be recorded post-merge. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes)

```json
{
  "edges": [
    { "a": 1.01, "b": 1.02, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs"] },
    { "a": 1.01, "b": 7, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs"] },
    { "a": 1.02, "b": 7, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs"] },
    { "a": 3, "b": 4, "sharedFiles": ["src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs"] },
    { "a": 3, "b": 6, "sharedFiles": ["src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs"] },
    { "a": 4, "b": 6, "sharedFiles": ["src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs"] }
  ],
  "degrees": { "1.01": 2, "1.02": 2, "2": 0, "3": 2, "4": 2, "5": 0, "6": 2, "7": 2, "8": 0, "9": 0, "10": 0, "11": 0, "12": 0, "13": 0, "14": 0, "15": 0 },
  "zeroDegreeInitiatives": [2, 5, 8, 9, 10, 11, 12, 13, 14, 15]
}
```

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | 10 | no (single toucher) |
| `ServerSurfaceCatalog.cs` partials | none | n/a |
| `ServiceCollectionExtensions.cs` | none | n/a |
| `ParameterObjectService.cs` | none | n/a |

## Stale-row spot check

| Row id | Present? |
|---|---|
| resource-read-protocol-error-semantics | yes |
| type-extraction-composition-constructor-coverage | yes |
| prompt-call-error-filter-boundary | yes |
| analysis-flow-error-detail-redaction | yes |
| atomic-file-cleanup-error-detail-redaction | yes |
| test-runner-timeout-error-detail-redaction | yes |
| semantic-grep-pattern-error-detail-redaction | yes |
| scaffolding-project-parse-diagnostic-redaction | yes |
| mcp-mrtr-dispatch-contract | yes |
| analyzer-shadow-loader-lifecycle | yes |
| publish-tag-version-parity | yes |
| manual-nuget-publish-provenance | yes |
| ci-merge-publish-runner-os-parity | yes |
| tool-call-error-envelope-wire-contract | yes |
| preview-apply-token-write-path-toctou | yes |

## Recommended next step

`failed` — Phase E should spawn remediation for the two Rule 5b block findings only. Both are single-field state edits (set `fanoutEstimate` to the counts the Risks cells already measured: 4 on 1.01, 2 on 1.02); no stanza narrative, scope, or file-set change is warranted, and no other initiative needs re-work. Note the deduplicated block count rose 1 → 2 versus cycle 0, which nominally trips the oscillation heuristic — but the increase is bookkeeping introduced by an otherwise-correct split, so a single narrow remediation pass is the right call rather than an immediate exit.