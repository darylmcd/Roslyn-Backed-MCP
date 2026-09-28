# workspace-close-drain-pid-reuse-between-resolve-and-kill — workspace_close drain can kill a recycled pid

**row:** `workspace-close-drain-pid-reuse-between-resolve-and-kill` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Runtime/ProcessExecutablePathResolver.cs`
- `src/RoslynMcp.Host.Stdio/Tools/DetachedTestHostDrain.cs`
- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs`

## Acceptance

- [ ] Windows: the `PROCESS_QUERY_LIMITED_INFORMATION` handle the resolver opens stays open until the drain has terminated (or skipped) the candidate, so the pid cannot be reissued between the path check and `Process.Kill`.
- [ ] The resolver seam returns a disposable lease (or equivalent); the drain disposes it on every outcome (exited, outside, unavailable, cancelled, terminated, terminate-failed).
- [ ] Linux: choose pidfd (`pidfd_open` + `pidfd_send_signal`, kernel 5.3+) or a documented residual window, and record the choice here.
- [ ] Existing `WorkspaceCloseDrainTests` stay green; a stub-seam test proves the lease is disposed for each outcome.

## Evidence

- `DetachedTestHostDrain.cs:209` resolves through the seam, whose Windows path opens and closes its own handle (`ProcessExecutablePathResolver.cs:92`). `DetachedTestHostDrain.cs:241` then calls `process.Kill(entireProcessTree: true)`, which re-opens the process by pid. `Process` objects from `GetProcessesByName` hold no handle, so nothing pins the pid between the two opens. Pre-existing since #1013; listed as a follow-up in PR #1666.

## Context

Low: the candidate must exit and its pid be reissued to an unrelated process inside a very short window. The blast radius is `Kill(entireProcessTree: true)` on that process tree.
