# type-move-unused-using-failure-observability

**row:** `type-move-unused-using-failure-observability` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] Propagate cancellation unchanged, and route unexpected cleanup failures through the established secret-safe diagnostic boundary if a best-effort fallback is retained. Pin controlled cancellation during cleanup.

## Evidence

RemoveUnusedUsingsAsync catches every exception, including OperationCanceledException, and silently returns a fallback solution. Verified during the 2026-09-14 tool-contract review.
