# project-file-mutation-byte-fidelity — Preserve project-file encoding on semantic writes

**row:** `project-file-mutation-byte-fidelity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs` (`PersistProjectReferenceChangesAsync`)
- `src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs` (`ProjectFileSnapshot`)
- `tests/RoslynMcp.Tests/CsprojReserializationTests.cs`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Project-reference add/remove writes preserve the original encoding and BOM while applying the intended XML semantic change.
- [ ] Transaction rollback remains byte-exact and does not duplicate project references.
- [ ] Add UTF-8 BOM and non-UTF-8 project-file regressions for successful semantic writes.

## Evidence

- The 2026-07-26 transactional-persistence review fixed byte-exact rollback and trivia-only restoration, but the successful project-reference mutation path still parses text and calls `WriteAllTextAsync`, which can replace the original BOM or encoding.
