# workspace-fixture-load-amortization-wave-2 — Reuse cache and mutation fixtures safely

**row:** `workspace-fixture-load-amortization-wave-2` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs`
- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs`
- `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs`

## Acceptance

- [ ] Reuse a class-private workspace only where cases do not require process/load isolation; retain separate fixtures for lifecycle-specific cases.
- [ ] Restore every mutated source, project, cache, and preview/undo state before each case and prove no state crosses test boundaries.
- [ ] Keep class-level parallel safety explicit; shared fixtures never become suite-global static state.
- [ ] One randomized-order regression runs representative mutating and cache-sensitive cases twice and produces identical state and results.

## Evidence

The baseline TRX attributed 4m36 summed duration across 33 `CompilationCacheAdoptionTests` cases and 4m27 across 30 `ProjectMutationIntegrationTests` cases. The suites make roughly 31 and 28 isolated workspace loads respectively. The tests cover distinct contracts; repeated full copy/load setup, not duplicate assertions, is the performance smell.
