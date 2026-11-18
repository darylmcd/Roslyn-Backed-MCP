# workspace-close-fixture-launcher-copy-lifetime — Bound launcher copy cleanup

**row:** `workspace-close-fixture-launcher-copy-lifetime` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs:1309-1346` — CopyLauncherUnder and the unbounded copy wait.

## Acceptance

- [ ] Reproduce a stopped owned copy child with a deterministic fence. Bound the copy separately and observe termination and disposal before releasing its destination fixture.
- [ ] Preserve the original startup or copy failure and any cleanup failure. Keep the out-of-process copy boundary that protects the immediate exec from inherited write descriptors.

## Evidence

- At c705e2e1, the Linux CopyLauncherUnder path runs /bin/cp and calls unbounded copy.WaitForExit(). A stalled copy can hold test startup and its destination indefinitely. This is a source finding from the exec fixture review.

## Context

- Independent of Linux exec image publication. Reuse existing fixture process ownership and cancellation helpers after verifying their current contracts.
