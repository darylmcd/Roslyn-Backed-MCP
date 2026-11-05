# workspace-path-guard-extraction — Move the physical-path guard out of WorkspaceManager

**row:** `workspace-path-guard-extraction` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs`
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`

## Acceptance

- [ ] EnsurePhysicalPath and EnsureChangedDocumentPathsArePhysical live in one static guard; WorkspaceLoadFinalizer no longer depends on WorkspaceManager.
- [ ] Callers (CompositeApplyOrchestrator, DocumentSetPersistenceService, UndoService) use the guard; InvalidOperationConstructionRatchetTests baseline member keys relocated with unchanged fingerprints; behavior unchanged.

## Evidence

- PR 1993 left the inverted dependency at WorkspaceLoadFinalizer.cs:98,112 (EnsurePhysicalPath call back into WorkspaceManager); the correct end state touches four production files, so it was split from the extraction.
