# workspace-validation-scope-description-contract

**row:** `workspace-validation-scope-description-contract` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Services/IWorkspaceValidationService.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs`

## Acceptance

- [ ] Describe changedFilePaths as test-discovery scope, document whole-workspace compiler and diagnostic passes, and preserve current execution behavior. Add a bounded contract regression if the descriptions are asserted by tool guidance.

## Evidence

2026-09-15 direct validation-verdict review: ValidateInternalAsync passes no project or file filter to compilation/diagnostics while IWorkspaceValidationService and WorkspaceValidationDto comments claim changed-file compilation scoping.
