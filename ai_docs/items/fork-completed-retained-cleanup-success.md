# fork-completed-retained-cleanup-success — Preserve successful fork result for completed POSIX retained cleanup

**row:** `fork-completed-retained-cleanup-success` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`: ApplyAsync, CleanupFork.
- `tests/RoslynMcp.Tests/SolutionFileMutationEscapingLifecycleTests.cs`.

## Acceptance

- [ ] Represent completed retained skeleton separately from actual cleanup failure.
- [ ] Successful drop returns Success=true, Retained=false and explicit retained-name warning on POSIX.
- [ ] Actual close/delete failures preserve warnings and Success=false; never infer failure from warning text/count.
- [ ] Use real correctly composed fork lifecycle controls with genuine old failure.

## Evidence

- Independent full-diff source review rederived this defect in the current retained restore implementation. Required fix in this PR; this row does not authorize deferral.
