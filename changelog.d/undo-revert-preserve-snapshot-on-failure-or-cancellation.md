---
category: Fixed
---

- **Fixed:** `UndoService.RevertAsync` and `RevertBySequenceAsync` no longer discard the undo snapshot/history entry before the restore completes — a cancelled or failed revert now leaves the same snapshot retryable instead of silently losing it, and `RevertBySequenceAsync` preserves dependency ordering across a failed-then-retried sequence revert.
