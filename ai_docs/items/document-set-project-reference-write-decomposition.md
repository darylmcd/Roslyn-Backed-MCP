# document-set-project-reference-write-decomposition — Decompose project-reference persistence

**row:** `document-set-project-reference-write-decomposition` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs` (`PersistProjectReferenceChangesAsync`)
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Separate project-reference discovery, exact target matching, XML mutation, and serialization into focused helpers.
- [ ] Reduce `PersistProjectReferenceChangesAsync` below cyclomatic complexity 10 and 70 executable lines.
- [ ] Preserve exact-path removal when two referenced projects share the same project-file name.

## Evidence

- Live Roslyn metrics during the 2026-07-27 ten-row cold review measured complexity 15 and 91 lines after transactional rollback landed.
