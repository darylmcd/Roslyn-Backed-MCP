---
category: Fixed
---

- **Fixed:** `CompilationCache.GetCompilationAsync` now short-circuits immediately for a caller whose token is already canceled on a cache miss, instead of first starting a full, uncancelable Roslyn compile pass (and installing a cache entry for it) whose result could only ever be discarded. Added regression coverage for the two previously unexercised branches: mid-fetch cancellation decoupling (a caller canceling after its request is in flight no longer affects another caller reading the same cache entry at the same workspace version) and faulted-shared-task eviction (a broken entry is removed so the next caller re-populates instead of replaying the failure). The `ICompilationCache` contract now states the per-caller cancellation and broken-entry-eviction guarantees the implementation already documented.
