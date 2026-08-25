---
category: Fixed
---

- **Fixed:** Run `evaluate_csharp` in a bounded child process, terminate non-cooperative scripts at the hard deadline, serialize progress delivery, preserve cleanup failures, and reclaim concurrency before returning. Closes `scripting-killable-worker-isolation-boundary` and `script-supervisor-cleanup-failure-observability`.
