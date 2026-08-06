# project-mutation-service-stale-comment-and-naming — consolidated low-severity cleanup in the snapshot-capture blocks

**row:** `project-mutation-service-stale-comment-and-naming` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:551` (stale comment contradicting the next branch)
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:554` (`normalizedProjectFilePath` computed but unused for `Exists`/read)
- `src/RoslynMcp.Roslyn/Services/EditService.cs:143` (single-letter `t` carried over from a deleted LINQ lambda)

## Acceptance

- [ ] `ProjectMutationService.cs:551`'s comment is reworded to describe the null-`OriginalText` branch as the defensive file-deleted-since-preview case (or the stale claim is dropped) instead of contradicting the branch directly beneath it.
- [ ] `ProjectMutationService.cs:554` uses `normalizedProjectFilePath` consistently for `Exists`/read/DTO construction instead of mixing normalized and un-normalized forms.
- [ ] `EditService.cs:143`'s loop variable `t` is renamed to something meaningful (e.g. `snapshot`) now that it is a statement-body loop variable, not a LINQ lambda parameter.

## Evidence

- Code-quality review of PR #1144 (`direct-mutation-undo-byte-fidelity`): two low-severity naming/comment findings in the snapshot-capture blocks, consolidated per the sweep's filing gate (no standalone rows for low cosmetic items).

## Context

Spin-off from the `direct-mutation-undo-byte-fidelity` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1144). Both findings are naming/comment hygiene, not functional bugs.
