# undo-revert-failed-reason-conflates-refusal-and-disk-failure — revert-failed conflates a fail-closed refusal with disk failure

**row:** `undo-revert-failed-reason-conflates-refusal-and-disk-failure` · **pri:** `Low` · **size:** `M` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:306`
- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs:127`

## Acceptance

- [ ] A refusal from RevertFromSolutionSnapshotAsync (UndoService.cs ~:690) returns a distinct reason and message; disk failure keeps `revert-failed`; contract-care: new reason value documented in IUndoService and the changelog.

## Evidence

- undo-b report sketch 5 (PR 2141): refusal path logs "refusing the solution-snapshot revert" but surfaces the same reason as an I/O failure.
