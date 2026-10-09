# extract-type-preserve-untouched-trivia — Avoid whole-file formatting

**row:** `extract-type-preserve-untouched-trivia` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:135-138`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] `extract_type_preview` formats only synthesized or changed syntax, leaving unchanged source regions byte-identical.
- [ ] A red-first regression includes unusual whitespace outside the extraction and compares the untouched bytes after preview/apply.

## Evidence

- Parent `extract-type-breaks-interfaces-and-publicizes-fields`: source-root `NormalizeWhitespace` rewrites unrelated source text.

## Context

- Separate regression mechanism from interface contract and accessibility handling.

2026-10-09 planning-quality finding: this initiative performance cell contained copied alias-resolution text belonging to workspace-project-alias-lookup. Corrected through audited stanza-amend; source approach, runtime scope, acceptance and estimate unchanged. This existing row tracks implementation; no runtime completion claimed.
