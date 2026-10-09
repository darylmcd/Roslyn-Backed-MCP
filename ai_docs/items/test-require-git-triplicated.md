# test-require-git-triplicated — RequireGit is duplicated in three test classes

**row:** `test-require-git-triplicated` · **pri:** `Low` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/ChangelogFragmentRequirementTests.cs`
- `tests/RoslynMcp.Tests/CodexChangelogHookTests.cs`
- `tests/RoslynMcp.Tests/ReleaseLagGuardTests.cs`
- `tests/RoslynMcp.Tests/Support/GitFixtureRunner.cs`

## Acceptance

- [ ] One `RequireGit` (in `Support/` beside `GitFixtureRunner`) serves the three classes; `rg -c "void RequireGit" tests/RoslynMcp.Tests` → 1.
- [ ] The inconclusive message is identical for all callers and names the failure reason.

## Evidence

- `tests/RoslynMcp.Tests/ChangelogFragmentRequirementTests.cs:363`, `tests/RoslynMcp.Tests/CodexChangelogHookTests.cs:229`, `tests/RoslynMcp.Tests/ReleaseLagGuardTests.cs:223` — `private static void RequireGit()`; :366 of the first: `Assert.Inconclusive($"git is required for changelog requirement fixtures: {failureReason}")`.
- Related: `test-inconclusive-count-unbudgeted` (the skip these helpers raise is unbounded). Land this first or with it.
- Found by a read-only syntactic probe of `tests/RoslynMcp.Tests` at 6cf842a8 on 2026-10-09 (ast-grep plus a string/comment mask; no test executed); counts re-derived with `rg` at e52c1241 before filing.

## Context

- regression_shape: a precondition helper copied per test class.
