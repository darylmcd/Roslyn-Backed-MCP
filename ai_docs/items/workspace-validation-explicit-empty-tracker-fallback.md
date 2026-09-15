# workspace-validation-explicit-empty-tracker-fallback

**row:** `workspace-validation-explicit-empty-tracker-fallback` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `src/RoslynMcp.Core/Services/IWorkspaceValidationService.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs`

## Acceptance

- [ ] Distinguish null from an explicit empty changedFilePaths list in scope resolution and reconciliation. An empty list must not read tracker state or invoke Git reconciliation. Cover an existing nonempty tracker with explicit empty scope and a clean Git-derived empty scope; retain null tracker fallback and update its documentation.

## Evidence

2026-09-15 source review: ResolveChangedFiles branches only for Count > 0, then reads the tracker. ValidateInternalAsync also reconciles null or Count == 0. This contradicts the Git-entry empty-scope intent and adds an unnecessary tracker/reconcile pass even on a clean tree.
