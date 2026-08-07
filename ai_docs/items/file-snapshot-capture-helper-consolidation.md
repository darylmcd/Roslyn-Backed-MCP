# file-snapshot-capture-helper-consolidation — extract a single shared byte-exact pre-apply snapshot-capture helper

**row:** `file-snapshot-capture-helper-consolidation` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Core/Services/IUndoService.cs:133` (`FileSnapshotDto.FromExistingBytes`)
- `src/RoslynMcp.Roslyn/Services/EditService.cs:80`
- `src/RoslynMcp.Roslyn/Services/EditService.cs:142`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:344`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:553`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1057`

## Acceptance

- [ ] A single shared capture helper (static factory beside `FileSnapshotDto.FromExistingBytes`, or an internal `SnapshotCapture` service) owns the exists→`ReadAllBytes` / missing→fallback-text-or-null policy.
- [ ] All five production call sites (`EditService` x2, `EditorConfigService`, `ProjectMutationService`, `RefactoringService.AddFileSnapshotAsync`) delegate to it; no call site re-implements the ternary. Existing undo/revert byte-fidelity tests stay green.

## Evidence

- Code-quality review of PR #1144 (`direct-mutation-undo-byte-fidelity`): all five sites inline the same three-way exists→bytes / missing→fallback decision that `RefactoringService.AddFileSnapshotAsync` already implements as a helper. The policy now has five independent definitions.

## Context

Spin-off from the `direct-mutation-undo-byte-fidelity` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1144). Adjacent to open row `edit-preview-validation-decomposition` (touches a different region of `EditService.cs`) — sequence to avoid PR collision.
Widen acceptance: FileSnapshotCapture.CaptureAsync (added by PR #1178) re-implements the exists/missing ternary inline instead of delegating to FromBytesOrFallback — a second independent copy of the same policy the class exists to own. Also fix the class XML doc, which overclaims "single owner... shared by every direct-file mutation path": RefactoringService.AddFileSnapshotAsync (RefactoringService.cs:1057-1089) still hand-rolls it. [source: PR #1178 code-quality review]
