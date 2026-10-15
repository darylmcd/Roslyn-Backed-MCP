# fixall-provider-throw-proof-sync-over-async — Await the provider failure proof

**row:** `fixall-provider-throw-proof-sync-over-async` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/FixAllServiceTests.cs:517-533`

## Acceptance

- [ ] Make ThrowingCodeFixProvider_GetFixAllProvider_Throws_InvalidOperationException asynchronous and await the returned task through an exact async MSTest assertion.
- [ ] Preserve the InvalidOperationException and message proof without GetAwaiter().GetResult(); cover the production awaited failure shape.

## Evidence

- Source-only 2026-10-15: the test synchronously waits on GetFixAsync(null!) inside Assert.ThrowsExactly. The current shim throws synchronously, so no deadlock was reproduced; this bridge is unnecessary and obscures the awaited contract if the shim changes.
- Different mechanism from public scope diagnostics and provider selection; leave it as bounded test debt rather than changing that unrelated fixture during the selected repair.
