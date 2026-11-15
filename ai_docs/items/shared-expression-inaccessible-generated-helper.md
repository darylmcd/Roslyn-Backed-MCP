# shared-expression-inaccessible-generated-helper — Refuse inaccessible generated helper calls before preview storage

**row:** `shared-expression-inaccessible-generated-helper` · **pri:** `Medium` · **size:** `S` · **verified:** `9e7a94395a5cbc09a79b0d4c6b8387ce65809d39`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:687`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] A cross-type occurrence with requested private helper access cannot receive a preview that would introduce an inaccessible call. Prove it red first through the experimental preview.
- [ ] Reuse the shared preview compilation-delta check where appropriate. Keep valid same-type private and cross-type accessible cases, including applied compilation.

## Evidence

- At 9e7a9439, the shared-expression path takes request accessibility, rewrites cross-type occurrences, and stores the resulting solution with no compilation delta check. The helper owner can bind exactly while the generated member is inaccessible.
- This is a specific producer covered by the broader audit row `preview-stores-without-compile-delta-audit`.
