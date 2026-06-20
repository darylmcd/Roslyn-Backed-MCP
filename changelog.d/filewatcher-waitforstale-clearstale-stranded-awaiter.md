---
category: Fixed
---

- **Fixed:** `FileWatcherService.ClearStale` now `TrySetCanceled()`s the outgoing `_staleSignal` TaskCompletionSource (under the existing `_reasonLock`) before re-arming it, so an awaiter parked on `WaitForStaleAsync` unblocks deterministically instead of hanging to its `CancellationToken` deadline. Benign for the sole current (test) caller but a latent trap for the next production caller; covered by a new direct-seam regression test (`FileWatcherClearStaleAwaiterTests`, red pre-fix / green post-fix) (`filewatcher-waitforstale-clearstale-stranded-awaiter`).
