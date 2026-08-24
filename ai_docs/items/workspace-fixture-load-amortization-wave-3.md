# workspace-fixture-load-amortization-wave-3 — Reuse scaffolding and cross-project fixtures safely

**row:** `workspace-fixture-load-amortization-wave-3` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Reuse a class-private workspace only for cases whose scaffold/refactor state can be reset completely; preserve isolated loads for lifecycle-sensitive cases.
- [ ] Restore project references, generated files, namespaces, compilation state, and preview/undo state before each case.
- [ ] Keep class-level parallel safety explicit; shared fixtures never become suite-global static state.
- [ ] One randomized-order regression repeats representative scaffolding and cross-project mutations and proves identical disk and workspace state.

## Evidence

The baseline TRX attributed 3m51 summed duration across 31 `ScaffoldingIntegrationTests` cases and 3m27 across 12 `CrossProjectRefactoringIntegrationTests` cases. Their roughly 25 and 12 full isolated workspace loads dominate setup, while the asserted behaviors remain distinct and valid.
