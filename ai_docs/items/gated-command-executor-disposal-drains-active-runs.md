# gated-command-executor-disposal-drains-active-runs — Retain gate ownership through disposal

**row:** `gated-command-executor-disposal-drains-active-runs` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs`: Dispose and ExecuteCoreAsync/ExecuteAsync finally.
- `tests/RoslynMcp.Tests/Services/GatedCommandExecutorTests.cs`

## Acceptance

- [ ] Stop new admission and coordinate active/queued command ownership before disposing command gates.
- [ ] Deterministic active and queued disposal tests preserve cancellation or command result without replacement ObjectDisposedException from release.
- [ ] Preserve workspace-close pruning and bounded per-workspace gate ownership; avoid undisposed lifetime accumulation.

## Evidence

- Source-only inspection2026-10-15: Dispose immediately disposes global and dictionary semaphores while executing commands can still wait/release them in finally. RemoveWorkspaceGate explicitly avoids concurrent semaphore disposal for this reason.
- Separate preexisting executor lifecycle mechanism; no runtime reproduction or completion claim.
