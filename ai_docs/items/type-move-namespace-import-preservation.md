# type-move-namespace-import-preservation

**row:** `type-move-namespace-import-preservation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] Preserve the full containing namespace and in-scope aliases/imports using semantic binding; remove the generic-name guess. Verify the moved type retains binding in a nested namespace with namespace-local imports.

## Evidence

The new unit copies only sourceRoot.Usings and the nearest namespace name. Enclosing namespace components and namespace-local usings are lost; a hardcoded generic-name regex adds System.Collections.Generic even for types from other namespaces. Verified during the 2026-09-14 tool-contract review.
