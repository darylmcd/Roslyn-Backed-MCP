# rebase-persistence-routing-comment — Correct stale apply persistence routing documentation

**row:** `rebase-persistence-routing-comment` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:835` — Rebase summary names nonexistent PersistDocumentSetChangesAsync.

## Acceptance

- [ ] Re-derive current apply, rebase and persistence routing from live symbols and references.
- [ ] Replace stale routing comments with current owner/symbol pointers; probe the obsolete symbol name for all instances of the same documentation defect.
- [ ] Make no runtime changes; pass documentation and changed-file format checks plus hosted checks before closure.

## Evidence

- Fresh independent UTF-16 implementation review at fb978941fd0704b180884d00fb027b2e92f0cfe1 identified the nonexistent persistence-method pointer.

## Context

- Unrelated pre-existing documentation debt; not a prerequisite of UTF-16 qualification, the metadata rebase or the restore row.
