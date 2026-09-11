# structured-call-pipeline-collaborator-cohesion — Further decompose structured pipeline collaborators

**row:** `structured-call-pipeline-collaborator-cohesion` · **pri:** `Low` · **size:** `M` · **deps:** `structured-call-tool-filter-pipeline-decomposition`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredWorkspaceResolver.cs` — `ResolveAsync` request-state, loaded-workspace, auto-load, and terminal-outcome decisions.
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredDispatchPipeline.cs` — `DispatchAsync` normalization, pre-bind path recovery, workspace resolution, and bound-dispatch orchestration.
- `tests/RoslynMcp.Tests/StructuredCallToolFilterResolutionTests.cs` — resolver decision matrix.
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs` — legacy/modern recovery and retry wire matrix.

## Acceptance

- [ ] Split the resolver's request-state, loaded-workspace, and zero-workspace auto-load decisions into focused, named operations with one explicit outcome type.
- [ ] Split the dispatcher's pre-bind recovery/terminal-result decisions from ordinary bound dispatch without duplicating error classification or terminal projection.
- [ ] Preserve arguments, cancellation, auto-resolution metrics, elicitation protocol behavior, and public result envelopes.
- [ ] Add a compact table-driven resolver/recovery regression matrix that covers request-state, explicit, single/file-path, fast-fail, auto-load, and elicitation-retry outcomes.

## Evidence

- 2026-09-10 cold review of the initial pipeline decomposition measured `StructuredWorkspaceResolver.ResolveAsync` at 108 LOC / cyclomatic complexity 15 / maintainability index 35.17 and `StructuredDispatchPipeline.DispatchAsync` at 116 LOC / cyclomatic complexity 12 / maintainability index 34.92. The parent row appropriately reduced the 299-LOC filter first; this follow-up bounds the remaining coordinator complexity without expanding that PR.
