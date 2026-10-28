---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Fork apply retains its source session through restore, validation and cleanup; busy source close and source-only capacity now refuse with wait/cancel guidance, while LRU can evict another eligible session. Manager shutdown asynchronously drains owned work and synchronous manager disposal safely defers pending cleanup. Embedders resolving `IWorkspaceForkApplyService` must use `await provider.DisposeAsync()` or `await using`; the stdio `host.RunAsync` path drains forks before dependencies without disposing externally supplied managers. See ADR 0029. Closes `fork-source-session-retention`. Migration: replace synchronous disposal of an `IWorkspaceForkApplyService` resolution (`provider.Dispose()` / `using`) with `await provider.DisposeAsync()` or `await using`; callers that previously closed a busy fork source session must now wait for or cancel the in-flight work before retrying.
