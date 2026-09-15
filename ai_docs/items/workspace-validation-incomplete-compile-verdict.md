# workspace-validation-incomplete-compile-verdict

**row:** `workspace-validation-incomplete-compile-verdict` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationOverallStatusTests.cs`

## Acceptance

- [ ] Distinguish a completed zero-error compile pass from Cancelled=true or CompletedProjects < TotalProjects. Preserve real compile/analyzer/test failures and emit an explicit non-clean status for incomplete compilation. Add a matrix covering cancelled, partially completed, zero-completed, and complete zero-error compilation through both validation entry points. Record an ADR and migration note for the published status change; do not weaken existing compile-error precedence.

## Evidence

2026-09-15 direct validation-scope review: WorkspaceValidationService.ComputeOverallStatus only checks ErrorCount and merged errors; MergeErrorDiagnostics recognizes incomplete compilation but cannot prevent clean when no diagnostic rows were collected. Current tests explicitly pin clean for zero completed projects.
