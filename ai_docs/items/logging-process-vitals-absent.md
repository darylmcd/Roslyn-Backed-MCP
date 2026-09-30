# logging-process-vitals-absent — Expose process vitals in server_heartbeat and the JSON-lines stream

**row:** `logging-process-vitals-absent` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs`
- `src/RoslynMcp.Core/Models/ConnectionStateDto.cs`
- `src/RoslynMcp.Host.Stdio/Runtime/ServerProcessMetadata.cs`
- `tests/RoslynMcp.Tests/ServerHeartbeatTests.cs`

## Acceptance

- [ ] server_heartbeat returns `vitals`: workingSetBytes, gcHeapBytes, gen0/1/2 counts, totalPauseMs, threadPoolThreads/queue, gate in-flight+queued, workspaceCount.
- [ ] Optional `ROSLYNMCP_VITALS_INTERVAL_SECONDS` writes the same block as a periodic structured record to the log stream (off by default).
- [ ] Additive output-schema change covered by the surface/schema contract tests.

## Evidence

- Logging audit 20260930-1340 (dimension A11); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

server_heartbeat/server_info return connection state, pid, start time, loaded workspace count and surface counts (good — headless, no workspace needed) plus `_meta.queuedMs/heldMs/elapsedMs`. No memory, GC or thread-pool figures exist (rg for GC.GetGCMemoryInfo/WorkingSet/ThreadPool/Meter over src finds none), and no periodic stats event is written. A long-lived host that leaks memory or starves the pool cannot be diagnosed headlessly.

Published tool surface: additive fields only (contract-care).

**Approach:** Read from GC.GetGCMemoryInfo, Process, ThreadPool and the existing execution-gate counters; one provider class injected into ServerTools; a hosted timer only when the interval is set.
