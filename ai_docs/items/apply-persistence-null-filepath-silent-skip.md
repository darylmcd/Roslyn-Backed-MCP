# apply-persistence-null-filepath-silent-skip — Refuse or report a reviewed document with a null FilePath instead of skipping it

**row:** `apply-persistence-null-filepath-silent-skip` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`

## Acceptance

- [ ] A reviewed added/changed/removed document with a null FilePath is refused in the existing result fields (ApplyResultDto Success:false), never skipped (DocumentSetPersistenceService.cs:215-217, :241-243, :260-262; WorkspaceForkApplyService.cs:~877), red-first test each; qualify against ADR 0043 with wire evidence.
- [ ] Removed null-path documents are deleted from disk or refused (PersistRemovedDocuments).

## Evidence

- Cold plan review of rebase-changed-document-silent-drop (follow-up 1, 5); the rebase refusal makes the in-lineage skip inconsistent.
