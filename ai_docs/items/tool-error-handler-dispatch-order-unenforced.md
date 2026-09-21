# tool-error-handler-dispatch-order-unenforced — Enforce ToolErrorHandler handler ordering with a test

**row:** `tool-error-handler-dispatch-order-unenforced` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `tests/RoslynMcp.Tests/ToolErrorHandlerSpecificityTests.cs`

## Acceptance

- [ ] A test fails if a derived-exception handler (`WorkspaceEvictedException`, `WorkspaceNotFoundException`) is registered after its `KeyNotFoundException` base entry, or the dispatch walks the exception hierarchy most-specific-first so order stops being load-bearing.
- [ ] Existing category mapping for evicted, unknown-workspace, symbol and document misses is unchanged.

## Evidence

`ToolErrorHandler` selects a category by `Dictionary` insertion order; the requirement is documented only in code comments near the `WorkspaceEvictedException` entry and the `KeyNotFoundException` entry, and nothing enforces it. A reordering edit would silently regress the `WorkspaceEvicted` / `WorkspaceNotFound` categories. Surfaced by the `workspace-id-unknown-error-category` deepener (sweep 20260921T211855Z) as bad code that did not fit inside that row's scope.
