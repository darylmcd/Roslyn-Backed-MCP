# workspace-id-named-args-refactoring — Name workspace-sensitive refactoring arguments

**row:** `workspace-id-named-args-refactoring` · **pri:** `Low` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/RefactoringToolsIntegrationTests.cs`
- `tests/RoslynMcp.Tests/RenameSummaryModeTests.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] Convert every affected direct read-only tool invocation in the three files to fully named arguments.
- [ ] Preserve argument expressions, evaluation order, explicit optional values, and assertions.
- [ ] Make no production-signature or runtime-behavior change; require compile check and all three targeted test classes to pass.

## Evidence

The live census found positional refactoring calls whose required arguments follow `workspaceId`.

## Context

Split from `workspace-id-optional-named-argument-prerequisite`.
