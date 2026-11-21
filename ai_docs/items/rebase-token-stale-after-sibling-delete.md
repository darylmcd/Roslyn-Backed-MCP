# rebase-token-stale-after-sibling-delete — Check that a delete_file_apply followed by an auto-reload does not strand preview tokens of the same batch

**row:** `rebase-token-stale-after-sibling-delete` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs`
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`

## Acceptance

- [ ] Test the sibling-delete then edit-apply sequence on base and state which outcome it has (rebase reached vs stale token); close with evidence or fix.

## Evidence

- Cold plan review of rebase-changed-document-silent-drop (follow-up 2).
