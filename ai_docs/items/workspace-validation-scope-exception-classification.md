# workspace-validation-scope-exception-classification

**row:** `workspace-validation-scope-exception-classification` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs` — ReconcileChangeTrackerFilesAsync, ResolveChangedFiles, NormalizePathForReconcile broad catch blocks.
- `tests/RoslynMcp.Tests/ValidateWorkspaceChangeTrackerReconcileTests.cs` — scope fallback behavior.

## Acceptance

- Catch only expected path/lookup failures at each fallback boundary; propagate cancellation and classify unexpected failures through the existing safe reporter.
- Preserve malformed-path and expected unavailable-scope behavior.
- Add one exception-classification regression matrix proving unexpected failures are observable and cancellation propagates.

## Evidence

2026-09-15 source review: bare catches hide all solution-directory and normalization failures; the path-is-not-null exception filter classifies arbitrary failures as malformed input.
