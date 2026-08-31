---
category: Changed
---

- **Changed:** Diagnostic queries now delegate per-project compiler, generator, analyzer, and effective-severity work to a focused collaborator with direct precedence coverage, while script-worker startup now uses an asynchronous, cancellation-aware IPC handoff with a contention-tolerant bound. Closes `diagnostic-query-project-analysis-extraction`.
