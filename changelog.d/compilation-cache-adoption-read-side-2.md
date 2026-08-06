---
category: Fixed
---

- **Fixed:** `ICompilationCache` no longer lets one caller's cancellation poison a shared cached compilation (or analyzer-bound compilation) for unrelated callers at the same workspace version — the underlying task is now started independent of any single caller's `CancellationToken`, each caller observes its own token via a non-canceling wrapper, and canceled/faulted entries are evicted instead of replayed until the next workspace version bump. Advances `compilation-cache-adoption-read-side` (row stays open).
