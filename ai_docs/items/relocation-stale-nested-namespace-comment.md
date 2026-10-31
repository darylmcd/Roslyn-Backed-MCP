# relocation-stale-nested-namespace-comment — Correct nested namespace contract comment

**row:** `relocation-stale-nested-namespace-comment` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:118`
- `tests/RoslynMcp.Tests/MovedDeclarationFileOwnershipIntegrationTests.cs`

## Acceptance

- [ ] Replace the stale comment with a statement matching the current nested namespace behavior and its tests.

## Evidence

- Cold implementation review on 2026-10-31: the comment says nested namespaces are rejected, while the current integration tests exercise nested relocation.
