# direct-mutation-undo-byte-fidelity — Preserve exact bytes in direct mutation undo snapshots

**row:** `direct-mutation-undo-byte-fidelity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs`
- `tests/RoslynMcp.Tests/EditUndoIntegrationTests.cs`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`
- `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs`

## Acceptance

- [ ] Capture existing files through `FileSnapshotDto.FromExistingBytes` before direct text, editorconfig, and project-file mutations.
- [ ] Undo restores UTF-8-BOM and UTF-16 fixtures byte-for-byte for all three mutation paths.
- [ ] New-file undo continues to delete the created file and text-only compatibility callers remain supported.

## Evidence

- The 2026-08-05 byte-fidelity remediation converted refactoring file-set snapshots, but these three direct mutation paths still construct text-only `FileSnapshotDto` values and therefore rewrite non-UTF-8 files as default UTF-8 during undo.
