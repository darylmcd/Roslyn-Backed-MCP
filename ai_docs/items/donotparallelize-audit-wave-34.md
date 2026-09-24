# donotparallelize-audit-wave-34 — Re-audit serialization opt-outs wave 34

**row:** `donotparallelize-audit-wave-34` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ValidateWorkspaceSummaryTests.cs`
- `tests/RoslynMcp.Tests/ValidationIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ValidationToolsIntegrationTests.cs`

## Acceptance

- [ ] For every `[DoNotParallelize]` in the anchored test files, either remove it after repeated concurrent evidence or retain it with a source-adjacent comment naming the concrete process-global/shared-workspace dependency.
- [ ] Run each affected class repeatedly before and after the decision; a retained opt-out cites the observed failure mechanism, while a removed opt-out stays green across the bounded repetition.
- [ ] Do not weaken assertions, add sleeps, or classify unrelated concurrency failures as success.

## Evidence

Parent row `test-assembly-donotparallelize-audit` found 124 attributes across 120 files after PR #1431 retired TestBase mutable statics. This wave limits review to at most three attributed test files; the final wave owns the aggregate comment and timing evidence.

## Context

One bounded slice of `test-assembly-donotparallelize-audit`. Children are intentionally independent except the final aggregate wave, which depends on waves 01–39.
Coupling from donotparallelize-audit-wave-14 (PR #1603): `HighValueCoverageIntegrationTests.CompileCheck_BuildFailureSolution_Reports_Errors` now calls `WorkspaceManager.Close` on the BuildFailureSolution session. `WorkspaceManager.LoadAsync` deduplicates by path (`src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` skipDedup/FindSessionByLoadedPath), so that session is shared with `ValidationIntegrationTests`' load (`tests/RoslynMcp.Tests/ValidationIntegrationTests.cs:154`). Removing `[DoNotParallelize]` from `ValidationIntegrationTests` must first give one side a private session (e.g. non-empty globalProperties, which skips dedup).
