---
category: Maintenance
---

- **Maintenance:** Fix the Windows fixture-latency race in `RunWithInputAsync_BrokenPipeTerminatesAndDrainsBeforeReturningFailure` (hosted runs 42090727476 and 42092533359): its `pwsh` child compiled C# with `Add-Type` before closing stdin (6-9 s under CPU saturation, over 10 s cold), so the runner's 10 s budget expired first; the Windows child is now a precompiled `close-stdin` mode of `ProcessPipeWorkerProbe`, and a new mid-budget `close-stdin-after` test pins that the runner still reports the broken pipe. This does NOT address the intermittent Linux write-phase timeout (run 41911201587), which was not reproduced; the Unix `exec sleep` change only removes a separate ~10.2 s slow-return symptom (a surviving `sleep` grandchild stalling cleanup), not the filed failures.
