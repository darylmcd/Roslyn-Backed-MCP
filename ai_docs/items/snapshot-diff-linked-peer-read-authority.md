# snapshot-diff-linked-peer-read-authority — Check every linked participant before invoking a snapshot-only loader

**row:** `snapshot-diff-linked-peer-read-authority` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffReadContext.cs`
- `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs`
- `tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs`

## Acceptance

- [ ] Snapshot-only diff preparation checks all unchanged linked participants at each changed key before the planner can invoke any lazy text loader.
- [ ] Rooted FilePath and virtual document-name keys both refuse a lazy peer with zero loader invocations; already materialized peers remain accepted.
- [ ] A later content-conflict refusal cannot satisfy the no-read assertion; count actual physical-loader attempts in the regression fixture.

## Evidence

- Localized removal of the linked-peer preflight initially passed a refusal-only fixture because the planner eventually refused conflicting loader output.
- Strengthened fixture observed six physical-loader attempts for each lazy key variant on the old mechanism: two failed, two materialized controls passed. Exact corrected production source restored by SHA-verified finally.

## Context

- Complete-fix companion of the active restore initiative; required read authority applies to every planner participant, not just changed document IDs.
