# test-discovery-direct-reference-failure-observability — Observe incomplete semantic reference discovery

**row:** `test-discovery-direct-reference-failure-observability` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:447-496`

## Acceptance

- [ ] A failed semantic reference sweep produces a payload-safe diagnostic identifying incomplete discovery; preserve useful fallback results without presenting the failed sweep as complete.
- [ ] Cancellation propagates without being classified as a discovery failure.
- [ ] Red-first tests cover failure, fallback results, cancellation and successful discovery; caller paths, source text and exception messages remain absent from public diagnostics.

## Evidence

- Source inspection at immutable c087ac4f512f2ddcddd83b89aca4e3e7c611757f: AddDirectReferenceMatchesAsync catches every non-cancellation exception at lines491-495 and discards it without logging or result diagnostics. DirectReferenceAttempted may already be true when failure occurs.
- Separate mechanism from filesystem comparison and cross-project fully-qualified-name collisions. No runtime reproduction or fix is claimed by this filing.

## Context

- Scope: one production service and one focused regression fixture. Re-derive the existing logging/result contract before choosing the diagnostic seam; reuse an existing seam when suitable.
