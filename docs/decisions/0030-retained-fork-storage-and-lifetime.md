# ADR 0030: Retained fork storage and physical lifetime ownership

Status: Accepted, 2026-10-24. Experimental placement correction; wire and public service signatures are unchanged.

## Context

Repeated forks appended `.roslynmcp/forks/<timestamp>-<suffix>` beneath each immediate source. On Windows, a valid source and first fork could produce a second native working directory that `CreateProcessW` rejects with error 267. An original source can also be launchable while its first generated fork is too long.

Legacy nested forks held only their own native execution lock. Expiring an enclosing retained fork could delete the active nested invocation and its admission material.

## Decision

First-generation forks retain their existing logical workspace-local `.roslynmcp/forks` pool and persist to its authorized canonical destination. For a retained source, discover verified owned material through physical filesystem parents and select the outermost containing owner's exact physical parent pool. Link resolution may give that pool another directory name; the spelling does not establish ownership or write authority. New generated children use a reserved `.roslynmcp-fork-<timestamp>-<suffix>` name, preserving default SDK item exclusion when canonical storage lies inside an ordinary source. Source watchers ignore only this generated namespace (using the shared timestamp recognizer), preserving parent preview authority while authored lookalikes remain observable; expiry continues to recognize verified legacy timestamp names. New generations are siblings outside every source-owned root; copy, replay, admission and mapping continue to use the immediate loaded source.

Authorize the pool and exact canonical destination through the existing request-scoped configured-root validator, including existing root-expansion grants and legacy client narrowing. Owned configuration is not authorization to write outside the current boundary. Missing request context fails closed. Reauthorize collision candidates under the shared pool lock.

The internal fork implementation now requires an internal storage-validation delegate. The supported stdio Host supplies it through `AddRoslynMcpHostServices`; Roslyn-only test compositions supply explicit fixture boundaries. Calling `AddRoslynServices` alone no longer supplies all dependencies needed to resolve the fork service or validate that entire DI graph. Standalone fork embedding is not an advertised public integration contract. The Core Apply method and wire request signatures remain unchanged.

Before filesystem effects, check that the actual normalized Windows restore working directory has at most 258 UTF-16 code units. Longer paths receive an actionable public refusal directing the caller to reopen or move the source under a shorter authorized root. Other platforms retain their native behavior.

Apply retains its source session through the landed source-lifecycle callback (ADR 0029). It acquires only canonical pool ownership before the source writer, then source/ancestor physical ownership inside that writer for capture/copy/replay. Release source/ancestor ownership inside the writer before restore, fork load, LRU, validation or tests; keep pool ownership until cleanup finishes. Verify the preview version against the actual source manager inside that writer before capture; an authorized reload before capture makes the old preview stale, while a reload after capture remains allowed. Native execution acquires physical ownership in ancestor order, revalidates ancestry, and releases partial acquisitions on cancellation or deadline expiry. This protects legacy enclosing owners from same-host TTL deletion. Uncertain native completion continues to retain admission material; no new cross-process lifetime protocol is introduced.

An ordinary canonical pool may equal its source root or be a nonexcluded descendant. Copy excludes the actual destination and verified prior fork children by their original physical roots at every recursion depth, while preserving authored imports and content in the pool. Reject canonical pools inside any verified retained owner before effects.

Expiry independently verifies each victim's owned material and physical direct-child identity under its existing lock. It skips reparse-point victims and unrelated timestamp-shaped siblings. Unverifiable claimed ownership retains the victim with a warning. Verified uncertain descendant material also retains its enclosing owner; inert authored directories do not establish ownership. Shared expiry and explicit cleanup delete link entries without traversing or changing independent targets.

Expiry and explicit cleanup also reserve deletion atomically against the actual manager's logical source registrations. A registered source inside the victim, including a nested legacy owner, retains the containing material while waiting for the pool. If deletion admission wins, a later source Apply refuses before registering or copying; a missing backing project/solution remains refused after deletion completes. Slow deletion occurs outside the manager lifecycle monitor, using the existing physical ownership. A failed workspace close retains the material and reports its cleanup warning.

## Migration

Consume the returned `forkPath` rather than constructing a nested path or generated child name from the source. Existing verified retained artifacts remain readable and eligible for expiry under their legacy names. A retained-only sanctioned root or narrower client root cannot authorize the sibling pool: reopen with an authorized original root. Unverifiable retained material must be recreated. A verified canonical pool with a different basename continues to support subsequent generations. The changelog fragment carries this migration into the release changelog.

Unmarked forks predating admission material have no verifiable ownership proof and are preserved by automatic expiry. Inspect and remove those manually when no longer needed, along with unrelated timestamp-shaped directories and reparse-point entries that the server cannot own safely.
