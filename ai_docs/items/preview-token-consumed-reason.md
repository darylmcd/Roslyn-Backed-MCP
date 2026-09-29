# preview-token-consumed-reason — Distinguish applied tokens from reload

**row:** `preview-token-consumed-reason` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/BoundedStore.cs:49-52`
- `src/RoslynMcp.Core/Services/PreviewTokenStaleException.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:159-166`
- `tests/RoslynMcp.Tests/PreviewTokenStaleAcrossAutoReloadTests.cs`

## Acceptance

- [ ] Reapplying a consumed preview token reports that it was already applied, without echoing token contents.
- [ ] A genuinely reloaded or expired token remains distinguishable and uses the existing safe error category contract.
- [ ] Red-first tests compare second apply, reload, and TTL expiry through the public tool envelope.

## Evidence

- Parent `preview-token-store-mismatch-false-stale`: `BoundedStore.Invalidate` discards consumed state and `ToolErrorHandler` labels all missing tokens as a workspace reload.

## Context

- Shared lifecycle mechanism across token apply paths; probe all `Invalidate` callers before implementation.
