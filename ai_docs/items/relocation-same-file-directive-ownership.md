# relocation-same-file-directive-ownership — Preserve directive ownership during same-file namespace relocation

**row:** `relocation-same-file-directive-ownership` · **pri:** `Medium` · **size:** `S` · **verified:** `9e7a94395a5cbc09a79b0d4c6b8387ce65809d39`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:390`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Exercise a file-scoped source with active siblings and a conditional alternate around EOF. Verify reparsed preview and applied source under both defines. Preserve directive pairs and scope for every retained declaration; move only the selected declaration.

## Evidence

- At 9e7a9439 MoveTypeIntoNewNamespaceInSameFile replaces the old namespace, then appends a new top-level block before EOF trivia. A directive spanning a declaration or EOF can cross the artificial namespace boundary.
