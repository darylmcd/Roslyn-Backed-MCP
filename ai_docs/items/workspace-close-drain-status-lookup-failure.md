# workspace-close-drain-status-lookup-failure — Distinguish workspace lookup failures before close drain

**row:** `workspace-close-drain-status-lookup-failure` · **pri:** `Medium` · **size:** `S` · **verified:** `c705e2e1942602c653cac44e1c2ae17d92e17af3`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:213-240`
- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs`

## Acceptance

- [ ] Reproduce an unexpected status failure for an existing workspace. Qualify the committed close outcome, drain outcome, and output in both supported protocol eras.
- [ ] Distinguish expected absent-workspace lookup from unexpected status failure. Preserve the stable close and ownership guarantees. Classify the complete wire impact before selecting a production fix.

## Evidence

- At c705e2e1, the catch at WorkspaceTools.cs:223–228 absorbs each non-cancellation exception from GetStatus as an expected miss. Close can then succeed while loadedPath is null and the process drain is skipped. This is a source finding from the Linux fixture review.

## Context

- Separate from Linux fixture startup, PID reuse, and the major-only global build-server shutdown row. Qualify an owned path lookup and safe failure reporting within the existing stable workspace_close contract.
