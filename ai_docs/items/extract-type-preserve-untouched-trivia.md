# extract-type-preserve-untouched-trivia — Avoid whole-file formatting

**row:** `extract-type-preserve-untouched-trivia` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:124-127`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] `extract_type_preview` formats only synthesized or changed syntax, leaving unchanged source regions byte-identical.
- [ ] A red-first regression includes unusual whitespace outside the extraction and compares the untouched bytes after preview/apply.

## Evidence

- Parent `extract-type-breaks-interfaces-and-publicizes-fields`: source-root `NormalizeWhitespace` rewrites unrelated source text.

## Context

- Separate regression mechanism from interface contract and accessibility handling.
