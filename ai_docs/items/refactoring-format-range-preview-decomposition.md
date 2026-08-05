# refactoring-format-range-preview-decomposition — Decompose format-range preview orchestration

**row:** `refactoring-format-range-preview-decomposition` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`PreviewFormatRangeAsync`, `SpliceFormattedRange`)
- `tests/RoslynMcp.Tests/FormatRangeServiceTests.cs`

## Acceptance

- [ ] Extract range resolution, formatting, splice-boundary validation, and preview assembly into named helpers.
- [ ] Keep each extracted method below cyclomatic complexity 10 and 80 logical lines.
- [ ] Preserve full-line, partial-line, trivia, newline, and invalid-range regressions.

## Evidence

- The 2026-08-05 adjacent review measured both `PreviewFormatRangeAsync` and `SpliceFormattedRange` at CC 11, with the preview method at 80 logical lines.
