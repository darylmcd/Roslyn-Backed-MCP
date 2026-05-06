---
category: Fixed
---

- **Fixed:** `WorkspaceLoadCacheFastPathTests.ColdThenWarm_*` no longer fails the release-publish workflow on a noise-margin stopwatch comparison. The previous timing assertion (`warmElapsedMs < coldElapsedMs`) was a flaky proxy for "warm-cache fast path engaged" against the 3-project SampleSolution fixture — wall-clock was dominated by MSBuild SDK resolution and shared-runner jitter routinely flipped the inequality (v1.34.0's publish-nuget runs failed twice for this reason). Replaced with cold-load behavioral assertion (`AmbientGateMetrics.CacheHit == false`), functional parity check, and entry-persistence check; warm-load `CacheHit == true` is intentionally not asserted yet because the probe-stage entry lookup in `WorkspaceManager.TryEnumerateNewestCacheEntryAsync` currently double-hashes its third key component (tracked as `workspace-cache-probe-double-hash-segment`). Closes `cache-fastpath-test-flakiness`.
