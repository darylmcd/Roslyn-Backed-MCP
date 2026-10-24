# ADR 0029: Retain fork source sessions and drain fork operations before provider teardown

Status: Accepted 2026-10-24 for the next major release.

## Context

Fork apply releases its source writer after capture and replay, then restores, loads, validates and tests the isolated fork. The source remained eligible for LRU eviction and explicit close during those later phases; eviction invalidated the real source preview and disposed its workspace. Manager shutdown also joined root-retirement tasks synchronously and could dispose resources while already-admitted removal callbacks were still using them.

## Decision

The actual resolved manager retains the exact registered source session from before source-context capture until fork completion and cleanup. Logical retention does not hold the source writer or LoadLock, so authorized source edits and reloads remain possible after capture.

| Lifecycle action | Behavior |
|---|---|
| Explicit source close during a fork | Public busy refusal before removal, invalidation, disposal, slot release or lifecycle events; wait for or cancel the fork |
| LRU capacity pressure | Skip retained sources, atomically recheck after candidate writer admission, and consider later eligible sessions from a finite snapshot |
| Only the retained source fits the capacity | Public capacity refusal; preserve source solution, version, preview and disk bytes |
| Missing backing root | Retry retirement after retention ends; shutdown cancels retirement |
| Manager asynchronous disposal | Publish one shared completion, cancel outside lifecycle synchronization, drain source callbacks, retirement and admitted removals, then attempt all terminal cleanup once |
| Manager synchronous disposal | Request the same stop; finish immediately when drains are complete, otherwise defer to its observed asynchronous completion without blocking |
| Provider disposal with a resolved fork service | Asynchronous disposal cancels and drains the fork service's own operations before its dependencies; it never takes ownership of an externally supplied manager |

The fork singleton factory resolves its entire dependency graph before returning the service. Its required callback binds the actual final `IWorkspaceManager` registration, including real manager overrides. No additional manager instance is created for retention.

Cleanup failures remain observable and propagate through asynchronous disposal. Retention and removal admission share manager lifecycle synchronization; removal callbacks execute outside that synchronization and release their admission only after every mandatory effect has been attempted.

## Compatibility and migration

This is a breaking behavior correction for the next major release. Tool names, request fields, result DTOs and the stdio transport remain unchanged.

Clients must wait for or cancel an active fork before closing its source. Capacity-constrained clients should close another idle workspace or raise capacity; `evictPolicy=lru` cannot evict an actively retained source. Handle refusal by error category rather than exact exception prose.

Embedders whose provider resolves `IWorkspaceForkApplyService` must replace synchronous provider disposal with `await provider.DisposeAsync()` or `await using`. The supported stdio `await host.RunAsync(...)` path uses the host's asynchronous provider teardown. Manager-only callers may still call synchronous `Dispose`, but must await `DisposeAsync` when they require proof that deferred cleanup finished. Shared or externally supplied dependencies retain their existing caller ownership.

## Verification

Native public rename regressions exercise capacities one and two and compare source solution/workspace identity, version, snapshot, disk bytes and the complete stored preview entry. Lifecycle tests cover late candidate retention, close refusal, retirement retry, failure cleanup, removal/shutdown races, mixed and reentrant disposal, actual Host stop/cancellation, and external manager ownership. Existing source-write concurrency tests retain their behavioral assertions.
