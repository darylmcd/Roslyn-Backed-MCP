---
category: Maintenance
---

- **Maintenance:** Fix the `RunWithInputAsync_BrokenPipeTerminatesAndDrainsBeforeReturningFailure` test fixture so it does not spend the runner's 10 s hang budget on a slow-starting child. The Windows child ran `pwsh` plus an `Add-Type` compile (6-9 s under CPU saturation, over 10 s cold) before closing stdin and timed out hosted CI legs; it is now a precompiled `close-stdin` mode of `ProcessPipeWorkerProbe`. The Unix shell child now `exec`s `sleep` so no grandchild can outlive the process-tree kill and stall cleanup for its full drain budget.
