# host-process-metadata-tests-async-await — Remove sync-over-async metadata tests

**row:** `host-process-metadata-tests-async-await` · **pri:** `Low` · **size:** `S` · **deps:** `server-start-time-source-consolidation`

## Anchors

- `tests/RoslynMcp.Tests/HostProcessMetadataTests.cs`

## Acceptance

- [ ] Convert every `GetAwaiter().GetResult()` server-tool test to `Task`/`await`.
- [ ] Preserve existing assertions and unconditional environment/temp-root cleanup.
- [ ] The complete fixture runs without blocking waits or changing parallelization behavior.

## Evidence

- Multiple touched metadata tests synchronously block asynchronous `ServerTools` calls, obscuring cancellation/failure behavior and risking fixture deadlocks.
