# type-extraction-service-dead-warnings-ternary — remove unreachable warnings ternary in PreviewExtractTypeAsync

**row:** `type-extraction-service-dead-warnings-ternary` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:56` (refusal throw when `warnings.Count > 0`)
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:126` (dead `warnings.Count > 0 ? ... : null` ternary)

## Acceptance

- [ ] The `RefactoringPreviewDto` construction no longer branches on `warnings.Count > 0`; the dead LINQ projection is removed and a comment records that the line-56 refusal guarantees an empty list at that point.
- [ ] Existing `extract_type_preview` tests still pass unchanged (the preview success path already returns a null warnings field).

## Evidence

- Code-quality review of PR #1138 (`extract-type-preview-refusal-missing-blocking-deps`) traced the method end-to-end: `warnings` is assigned once at line 49, the guard at line 56 throws whenever `Count > 0`, and there is no mutation of `warnings` between 56 and 126 — the ternary's true branch is provably unreachable.

## Context

Spin-off from the `extract-type-preview-refusal-missing-blocking-deps` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1138). Low-severity dead-code cleanup, not a functional bug.
