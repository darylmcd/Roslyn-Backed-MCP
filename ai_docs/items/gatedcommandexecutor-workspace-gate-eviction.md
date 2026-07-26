# gatedcommandexecutor-workspace-gate-eviction — Evict idle per-workspace command gates

**row:** `gatedcommandexecutor-workspace-gate-eviction` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs`
- `tests/RoslynMcp.Tests/Services/GatedCommandExecutorTests.cs`

## Acceptance

- [ ] A per-workspace semaphore is retired, removed, and disposed after its final queued or running caller exits.
- [ ] Acquisition and retirement are race-safe: same-workspace calls remain serialized and no caller observes a disposed semaphore.
- [ ] A regression issuing commands for many one-shot workspace IDs proves the gate map returns to an empty or explicitly bounded state.

## Evidence

- Cold review on 2026-07-26 found `_workspaceCommandGates` retains one `SemaphoreSlim` for every workspace ID until executor disposal.
