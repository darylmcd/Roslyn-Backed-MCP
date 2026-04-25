---
category: Fixed
---

- **Fixed:** `WorkspaceExecutionGate` now retries an action exactly once after a `staleAction="auto-reloaded"` cycle when the first attempt throws a transient stale-snapshot exception (Document/key not found), eliminating the audit's "Document not found" surface for `format_document_preview` / `get_completions` / `find_references_bulk` immediately after auto-reload. Cap-1 (no cascade); cancellation short-circuits the retry; recovered + eventual-failure envelopes both stamp `_meta.retriedAfterReload=true` (`auto-reload-retry-inside-call`).
