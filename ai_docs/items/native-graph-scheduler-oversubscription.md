# native-graph-scheduler-oversubscription — native-graph-scheduler-oversubscription

**row:** `native-graph-scheduler-oversubscription` · **pri:** `High` · **size:** `S`

## Problem

| Field | Evidence |
|---|---|
| Production anchors | `src/RoslynMcp.Roslyn/Services/EvaluatedBuildGraph.cs`: `ReadCore`; `src/RoslynMcp.Roslyn/Services/CompilerReferenceContextGraph.cs`: `Capture`. |
| Regression anchors | `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`: `ConcurrentUndoRoutes_ClaimTheSameInverseExactlyOnce`; `tests/RoslynMcp.Tests/ForkInvocationManagerAuthorityTests.cs`: actual native test-process control. |
| Root mechanism | Both ProjectGraph factory calls default to logical-core degree. The evaluation factory serializes native evaluation under one lock; direct-edge graphs only clone captured contexts. MSBuild still creates degree-minus-one worker tasks for every graph. |
| Scheduler evidence | Local SDK10.0.402 decompilation: ProjectGraph passes logical-core count; GraphBuilder constructs ParallelWorkSet(degree-minus-one); its constructor starts Task.Run workers; calling thread loops ExecuteWorkItem while pending work exists, including empty-queue iterations while a worker evaluates. A single-node graph can therefore consume a spinning caller plus one working thread. |
| API availability | Pinned Microsoft.Build17.14.28 ref/net9.0 XML line50963 declares explicit degree/CancellationToken overload. Installed SDK10.0.204 also exposes it. Exact declared floor10.0.400 still needs normal qualification. |

## Acceptance

- [ ] Use explicit degree1 for both owned graph factory callsites so the caller performs the graph work; retain the existing factory serialization and all native graph interpretation.
- [ ] Preserve exact contexts, dependency edges, multiplicity, cancellation and failure propagation. Do not change global thread-pool settings, test parallelism, deadlines, assertions or authority checks.
- [ ] Preserve all independent capture, current-grant, byte-hash, membership, output-receipt and native load boundaries; no cached permission or freshness optimization.
- [ ] Run the unchanged combined native acceptance set containing original undo deadline and real fork process controls; record old/new results and required full/hosted/floor gates.

## Current evidence

| Evidence | Result / limit |
|---|---|
| Combined native acceptance before scheduler correction | Parent reports three original deadline failures under competing native work. Do not classify as a flake or weaken their deadlines. |
| Isolated count-only overlap probe | Parent reports both undo cases passed in53s; False request20.27s,17712 physical captures/3.245s and61 graph evaluations/10.14s. Different concurrent load; this does not prove the correction. |
| Static implementation proof | `ProjectGraph-scheduler-review.cs`, `GraphBuilder-scheduler-review.cs`, `ParallelWorkSet-scheduler-review.cs` in current resume scratch contain local actual decompilation. |
| Fix state | Both explicit degree1 calls installed. Current-session focused28 completed 190 passed, 3 failed, 193 total in 2.9985 minutes; all three original deadline failures remain. The correction removes the scheduler mechanism but does not establish acceptance or resolve the total latency. |

Keep OPEN until corrected combined and required shipping gates pass. This restore prerequisite does not advance the paused batch.
