# editorconfig-create-undo-test-conditional-skip — Require the creation undo case

**row:** `editorconfig-create-undo-test-conditional-skip` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/EditUndoCohesionTests.cs`

## Acceptance

- [ ] Make SetEditorConfigOption_CreatesNewFile_RevertDeletesIt require its fresh-file precondition and assert CreatedNewFile.
- [ ] Remove the conditional inconclusive escape without deleting the creation or undo assertions.
- [ ] Keep all 24 cohesion cases and existing OS-specific skips/deadlines unchanged; verify the newly-created config is deleted by undo.

## Evidence

The creation test deletes its owned candidate path, then silently drops the undo assertion through Assert.Inconclusive if CreatedNewFile is false. A stale comment treats ancestor configuration as an accepted reason to omit this contract. Current EditorConfigService rejects writes outside the loaded workspace; the fixture should assert its intended creation precondition instead of treating a different operation as inconclusive.
