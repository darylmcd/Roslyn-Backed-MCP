# workspace-readiness-fixture-ready-state-isolation — Establish readiness fixtures explicitly

**row:** `workspace-readiness-fixture-ready-state-isolation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceReadinessReportIntegrationTests.cs`
- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs`

## Acceptance

- [ ] Make the ready-verdict case establish every build/restore artifact required by its own precondition instead of inheriting another test's side effects.
- [ ] Preserve the restore-needed and build-needed scenarios with isolated fixture state.
- [ ] Add one regression shape that runs the readiness class alone and in a deliberately different method order, repeatedly, with identical outcomes.
- [ ] Confirm no shared sample `bin`/`obj` state is required before or left behind after the class.

## Evidence

PR #1326's correctly prepared full run observed `SampleSolution_ReadinessReport_ReturnsReadyVerdict` returning `build-needed`. The same one-of-three class failure reproduced on untouched base SHA `a518151b` in a fresh clone, proving a pre-existing order-dependent fixture rather than a dependency regression: the ready case relies on another test having built the sample first.
