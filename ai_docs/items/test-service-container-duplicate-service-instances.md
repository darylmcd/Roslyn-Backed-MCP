# test-service-container-duplicate-service-instances — Container hands out services it did not use

**row:** `test-service-container-duplicate-service-instances` · **pri:** `Medium` · **size:** `S` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`

## Acceptance

- [ ] `RefactoringSuggestionService` is constructed from the container's own `CodeMetricsService`, `CohesionAnalysisService` and `UnusedCodeAnalyzer` instances, not fresh ones.
- [ ] A regression asserts reference identity between what the container exposes and what `RefactoringSuggestionService` was given.

## Evidence

Verified against `main` on 2026-09-03. `TestServiceContainer.Create` builds
`RefactoringSuggestionService` at `:295-299` with **fresh** collaborators —
`:296 new CodeMetricsService(workspaceManager)`,
`:297 new CohesionAnalysisService(workspaceManager, ...)`,
`:298 new UnusedCodeAnalyzer(workspaceManager, compilationCache, ...)` —
while the container separately exposes its own at `:203`, `:248` and `:199`.

A test asserting through `RefactoringSuggestionService` therefore observes different objects than
the ones `CodeMetricsService` / `CohesionAnalysisService` / `UnusedCodeAnalyzer` hand it, and the
fixture graph silently diverges from production DI where these are singletons. Copy-paste
duplication, ~4 lines.

## Context

Surfaced by the executor of PR #1431 while working in this file (Directive #3); not caused by that
PR, which only moved ownership of the container into `TestAssemblyFixture`.

[source: 2026-09-03 backlog-remediate PR #1431]
