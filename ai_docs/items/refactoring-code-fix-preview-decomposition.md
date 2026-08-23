# refactoring-code-fix-preview-decomposition — Decompose diagnostic code-fix preview orchestration

**row:** `refactoring-code-fix-preview-decomposition` · **pri:** `Low` · **size:** `S` · **deps:** `refactoring-format-range-preview-decomposition`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`PreviewCodeFixAsync`)
- `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs`

## Acceptance

- [ ] Extract diagnostic lookup, action selection, changed-solution capture, and preview assembly into named helpers.
- [ ] Keep the orchestrator and each extracted helper below cyclomatic complexity 10 and 80 logical lines.
- [ ] Preserve default-fix selection, explicit fix-id selection, missing diagnostic, and no-action behavior.

## Evidence

- The 2026-08-05 adjacent review measured `PreviewCodeFixAsync` at CC 11/78 logical lines with three nested decision levels.
