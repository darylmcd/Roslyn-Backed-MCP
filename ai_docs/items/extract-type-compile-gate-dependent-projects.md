# extract-type-compile-gate-dependent-projects — Extend the extract_type compile-delta gate beyond the source project

**row:** `extract-type-compile-gate-dependent-projects` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` — `RefuseAddedCompilerErrorsAsync` diffs only `sourceDocument.Project`.
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs` — `ExtractType_PreviewThatAddsCompilerErrors_IsRefusedWithoutToken`.

## Acceptance

- [ ] Red first: a project referencing the source project and calling the changed constructor currently gets a token; after the fix the preview is refused listing the added ids.
- [ ] Decide and test the moved-error edge: a pre-existing error inside an extracted member changes document key and is falsely counted as added; either key such errors stably or pin the refusal with a test.
- [ ] Refusal still leaves no token, version change or disk effect.

## Evidence

- Cold review of PR #1863 (2026-10-27) static finding; not runtime-reproduced.
