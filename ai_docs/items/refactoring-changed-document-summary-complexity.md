# refactoring-changed-document-summary-complexity — Decompose changed-document summaries

**row:** `refactoring-changed-document-summary-complexity` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`AppendChangedDocumentSummariesAsync`)
- `tests/RoslynMcp.Tests/RenameSummaryModeTests.cs`

## Acceptance

- [ ] `AppendChangedDocumentSummariesAsync` measures cyclomatic complexity below 10.
- [ ] Null documents, unchanged text, path fallback, line counts, net markers, ordering, and cancellation behavior remain unchanged.

## Regression

A multi-file rename with `summary=true` emits the same ordered one-line per-file summaries, including positive, negative, and zero net-line markers.
