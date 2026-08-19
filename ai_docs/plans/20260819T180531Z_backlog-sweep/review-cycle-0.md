# Plan review — 2026-08-19 (cycle 0)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260819T180531Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** failed
**Initiative count:** 15 pending
**Findings:** block: 1, warn: 1, info: 10
**Anchor verification:** performed (first 3 initiatives spot-checked live; plus targeted probes on 5, 8, 15)

## Summary

The plan is unusually well-sourced: every initiative sits inside the Rule 3 (<=4 production files), Rule 4 (<=3 test files) and Rule 5 (<=80K tokens) ceilings on its declared numbers, every initiative carries an explicit `edit-only` toolPolicy, every `fanoutEstimate` is within the Rule 5b margin, and all 15 `backlogRowsClosed` ids still resolve in `ai_docs/backlog.md`. My independently rebuilt conflict graph matches the orchestrator's exactly (4 edges, degrees identical). One block: initiative 1 (`resource-read-protocol-error-semantics`) declares `testFilesAdded: 3` and states in Scope that "no further test file may be touched", but an independent probe found a fourth test file whose assertions the change necessarily breaks — `tests/RoslynMcp.Tests/FetchMcpResourceReadinessTests.cs:131-144` calls `WorkspaceResources.GetWorkspaceStatus` directly and asserts `error: true` / `category: "NotFound"` in the returned resource BODY, which is precisely the envelope initiative 1 deletes. That takes the forced test-file edit set to 4 and breaks Rule 4. One warn: initiatives 3 and 4 are adjacent in order and both add a member to `IUnexpectedExceptionReporter.cs`, which Step 6 should have separated. The rest is advisory: four self-declared stale detail-file anchors (all already re-targeted by the plan), three degree-2 serialization notices on the shared enum file, two fanout under-records with sound-but-fragile rationales, and one acceptance criterion in initiative 13 that structurally cannot land in a PR diff (branch-protection settings).

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| resource-read-protocol-error-semantics | block | 4 | `FetchMcpResourceReadinessTests.cs:131-144` asserts the resource-body error envelope this initiative removes -> forced test edits = 4 (ErrorResponseObservabilityTests, Top10V2RegressionTests, FetchMcpResourceReadinessTests, new ResourceReadWireContractTests) > Rule 4 cap of 3. Scope lists FetchMcpResourceReadinessTests only as a keep-green run. |
| resource-read-protocol-error-semantics | info | anchor-stale | Acceptance cites `McpProtocolVersions.UseInvalidParamsForMissingResource`; verified unavailable (SDK helper internal, per `RequestProtocolFeatureGate.cs:20-21`). Plan substitutes a local equivalent. |
| prompt-call-error-filter-boundary | warn | C2-wave-conflict | Adjacent-order initiatives 3 and 4 share `src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs`. |
| prompt-call-error-filter-boundary | info | C2-wave-conflict | Degree 2 (peers 4, 6) on the shared enum file; expect serial scheduling. |
| analysis-flow-error-detail-redaction | info | C2-wave-conflict | Degree 2 (peers 3, 6) on the shared enum file. |
| test-runner-timeout-error-detail-redaction | info | C2-wave-conflict | Degree 2 (peers 3, 4) on the shared enum file. |
| prompt-call-error-filter-boundary | info | 5b | Probe measured 15 `CreateErrorMessage` use sites; `fanoutEstimate: 4` justified by signature preservation. Re-probe if the signature changes. |
| analysis-flow-error-detail-redaction | info | anchor-stale | `tests/RoslynMcp.Tests/FlowAnalysisTests.cs` missing; re-targeted to `FlowAnalysisServiceTests.cs`. |
| test-runner-timeout-error-detail-redaction | info | anchor-stale | `tests/RoslynMcp.Tests/TestRunnerTimeoutTests.cs` missing; re-targeted to `TestRunFailureEnvelopeTests.cs`. |
| semantic-grep-pattern-error-detail-redaction | info | anchor-stale | `tests/RoslynMcp.Tests/SemanticGrepIntegrationTests.cs` missing; re-targeted to `SemanticGrepServiceTests.cs`. |
| ci-merge-publish-runner-os-parity | info | acceptance-non-diff | "Linux leg is a required check" is repo settings, not a file; needs a post-merge `gh api` action recorded in the PR, or the leg ships advisory-only. |
| preview-apply-token-write-path-toctou | info | 5b | Probe measured 16 `ApplyByTokenAsync` call sites; `fanoutEstimate: 4` justified by the default-interface-method + unchanged-signature design. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes — exact match on all 4 edges and all 15 degrees)

```json
{
  "edges": [
    { "a": 1, "b": 7, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs"] },
    { "a": 3, "b": 4, "sharedFiles": ["src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs"] },
    { "a": 3, "b": 6, "sharedFiles": ["src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs"] },
    { "a": 4, "b": 6, "sharedFiles": ["src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs"] }
  ],
  "degrees": { "1": 1, "2": 0, "3": 2, "4": 2, "5": 0, "6": 2, "7": 1, "8": 0, "9": 0, "10": 0, "11": 0, "12": 0, "13": 0, "14": 0, "15": 0 },
  "zeroDegreeInitiatives": [2, 5, 8, 9, 10, 11, 12, 13, 14, 15]
}
```

## Hotspot scheduling

Addenda hotspots: `ServerSurfaceCatalog.cs` (+partials), `ServiceCollectionExtensions.cs`, `WorkspaceManager.cs`, `ParameterObjectService.cs`.

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | 10 | no (sole toucher) |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` | none | n/a |

No hotspot-adjacency violation. Initiatives 4, 6 and 8 each explicitly designed around avoiding a `ServiceCollectionExtensions.cs` touch (optional-ctor-parameter pattern) — that avoidance is real and verified against the cited sibling patterns.

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

## Notes on non-finding observations

- `state.json.addendaLoaded` is `false` on disk while `ai_docs/prompts/backlog-sweep-addenda.md` exists and was loaded by this review. Skeleton-state artifact; worth correcting when the deepened state is written, since hotspot and structural-unit rules key off it.
- Verified live and matching the plan's claims: `ToolErrorHandler.ExecuteResource` at `:141` / `ExecuteResourceAsync` at `:175` with `InjectMetaIfPossible(FormatErrorResponse(...))` on the catch arms; `ResourceReadResultFilter.cs:15-28` has no try/catch; `Program.cs:78-86` registers list/read-resource/call-tool filters and no `AddGetPromptFilter`; `PromptMessageBuilder.CreateErrorMessage` interpolates raw `ex.Message`; `TypeExtractionService.InjectFieldAndCtorParameter` at `:445` uses `FirstOrDefault()` at `:456` and the `Body is not null` guard at `:481`; `UnexpectedExceptionCategory.CompositeApply` is present (`IUnexpectedExceptionReporter.cs:15`) and `GetPrompt`/`FlowAnalysis`/`TestRun` are genuinely absent, so initiatives 3/4/6 each really do add one member to that one file.
- Initiative 1's Rule 3 exemption posture is clean (3 files, none claimed). The block is Rule 4 only; the cheapest remediation is to split the wire-contract test file out into a separate follow-on initiative, or drop one of the two direct-call test flips into the new file so the modified-file count lands at 3.

## Recommended next step

`failed` — Phase E should spawn a remediator for the single Rule 4 block on `resource-read-protocol-error-semantics`. The other 14 initiatives are clean and need no remediation; scope the re-review accordingly.